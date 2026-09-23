// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Globalization;
using Microsoft.Extensions.Logging;
using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.Hosting;
using NoMercy.Plugin.InternetRadio.Playback;
using NoMercy.Plugin.InternetRadio.State;
using NoMercy.Plugin.InternetRadio.Views;

namespace NoMercy.Plugin.InternetRadio;

// The class the server loads.
//
// Two contracts, one lifecycle. IUiPlugin serves five routes; IScheduledTaskPlugin
// keeps the catalogue current so a view never waits on the network when it can help
// it. Everything that touches IPluginContext lives here: the views are pure
// functions and the provider takes what it needs as arguments.
public sealed class InternetRadioPlugin : IUiPlugin, IScheduledTaskPlugin
{
    /// <summary>The single job's name. It appears in the server's job list as plugin:{id}:refresh.</summary>
    public const string RefreshJobName = "refresh";

    /// <summary>
    /// The quiet hour, UTC, the refresh job runs at. The one source for that hour -
    /// <see cref="DefaultCron"/> and <see cref="NextRefreshUtc"/> both read it,
    /// rather than each carrying its own copy of "4" that could drift apart.
    /// </summary>
    private const int RefreshHourUtc = 4;

    /// <summary>
    /// Daily, at a quiet hour. radio-browser is a volunteer-run service and this
    /// plugin has no reason to poll it harder than the catalogue actually changes.
    /// Not `const`: a numeric substitution into a const interpolated string is not
    /// itself a compile-time constant in C#, so this is `static readonly` instead.
    /// Built via <see cref="string.Create(IFormatProvider, ref DefaultInterpolatedStringHandler)"/>
    /// with <see cref="CultureInfo.InvariantCulture"/> rather than a bare `$"..."` -
    /// a plain interpolated int otherwise formats through CurrentCulture, and some
    /// cultures substitute non-ASCII native digits, which would corrupt the cron
    /// expression a scheduler has to parse as plain ASCII.
    /// </summary>
    private static readonly string DefaultCron =
        string.Create(CultureInfo.InvariantCulture, $"0 {RefreshHourUtc} * * *");

    /// <summary>
    /// The next time <see cref="DefaultCron"/> fires from <paramref name="now"/>.
    /// Static and cron-shaped rather than reading a saved schedule, because there is
    /// no setting to read - see <see cref="Jobs"/>. Exposed so the settings page can
    /// show it without duplicating the schedule itself.
    /// </summary>
    public static DateTimeOffset NextRefreshUtc(DateTimeOffset now)
    {
        DateTimeOffset todaysRun = new(now.Year, now.Month, now.Day, RefreshHourUtc, 0, 0, TimeSpan.Zero);
        return now < todaysRun ? todaysRun : todaysRun.AddDays(1);
    }

    private IPluginContext? _context;
    private CatalogProvider? _provider;
    private UserStateStore? _userState;
    private bool _disposed;

    // Field-initialised so Dispose has something to cancel even when the host
    // disposes a plugin whose load never completed. Every tick links this into the
    // token it runs under, which is what makes "Dispose cancels in-flight work" real
    // rather than aspirational.
    private readonly CancellationTokenSource _lifecycleCts = new();

    public string Name => PluginIdentity.Name;
    public string Description => PluginIdentity.Description;
    public Ulid Id => PluginIdentity.Id;
    public Version Version => PluginIdentity.Version;

    // Captures the context and nothing else. No I/O, no network, no config read: a
    // plugin that throws from here fails to load, and Initialize is synchronous with
    // nowhere to await a fix. Real work belongs on the first view or the first tick.
    public void Initialize(IPluginContext context)
    {
        _context = context;
    }

    private IPluginContext Context =>
        _context ?? throw new InvalidOperationException("the plugin was used before Initialize");

    private CatalogProvider Provider =>
        _provider ??= new CatalogProvider(
            new RadioBrowserClient(Context.HttpClient),
            new CatalogCache(Context.DataFolderPath),
            Context.DataFolderPath,
            Context.Logger
        );

    private UserStateStore StateStore => _userState ??= new UserStateStore(Context.DataFolderPath);

    private LogoCache? _logos;

    private LogoCache Logos => _logos ??= new LogoCache(Context.DataFolderPath);

    private StationGrants? _grants;

    private StationGrants Grants => _grants ??= new StationGrants(Context.Grants, Context.Logger);

    private IStationStreams? _streams;

    /// <summary>
    /// Where a station page gets its playable url. Settable so a test can stand in for the
    /// host; the plugin itself always uses the context's media proxy.
    /// </summary>
    // Plain `??=`, unlike Listener below: two station pages racing here can still
    // publish two HostStationStreams, but neither holds a connection or any other
    // resource that outlives the call that made it, so the worst a race costs is one
    // redundant instance that is never used again.
    internal IStationStreams Streams
    {
        get => _streams ??= HostStationStreams.For(Context);
        set => _streams = value;
    }

    // 0 or 1: whether a background logo fetch is running. One at a time is plenty - a
    // page of placeholders fills in on the next view either way.
    private int _fetchingLogos;

    /// <summary>
    /// Fetches every logo views asked for since the last run, and stores what came of it.
    ///
    /// Internal so a test can run it to completion instead of racing the background copy.
    /// A failure worth retrying is not stored, so the next view asks again.
    /// </summary>
    internal async Task FetchPendingLogosAsync(CancellationToken ct)
    {
        LogoFetcher fetcher = new(Context.HttpClient);

        while (Logos.TakePending() is { Count: > 0 } wanted)
        {
            await FetchLogosAsync(fetcher, wanted, ct);
        }
    }

    private async Task FetchLogosAsync(LogoFetcher fetcher, IEnumerable<RadioStation> stations, CancellationToken ct)
    {
        foreach (RadioStation station in stations)
        {
            ct.ThrowIfCancellationRequested();

            if (Logos.Has(station) || LogoCache.SourceOf(station) is not { } source)
            {
                continue;
            }

            LogoResult result = await fetcher.FetchAsync(source, ct);

            if (result.IsFinal)
            {
                await Logos.StoreAsync(station, result.DataUri, ct);
            }
        }
    }

    /// <summary>Starts a background fetch for whatever the page just asked for, unless one is running.</summary>
    private void FetchLogosInBackground()
    {
        if (Interlocked.CompareExchange(ref _fetchingLogos, 1, 0) != 0)
        {
            return;
        }

        CancellationToken lifetime = _lifecycleCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await FetchPendingLogosAsync(lifetime);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                // Unloading. The logos wait for the next start.
            }
            catch (Exception exception)
            {
                _context?.Logger.LogWarning(exception, "Internet Radio could not fetch station logos.");
            }
            finally
            {
                Volatile.Write(ref _fetchingLogos, 0);
            }
        });
    }

    /// <summary>The choices the search form offers, as radio-browser publishes them.</summary>
    private readonly SearchFacets _facets = new();

    /// <summary>
    /// The lists, refreshed when they have aged out.
    ///
    /// Failure is swallowed: an unreachable radio-browser costs the selects their
    /// options and the form falls back to text boxes, which is a smaller loss than
    /// a page that will not draw at all.
    /// </summary>
    private async Task<SearchFacets> FacetsAsync(CancellationToken ct)
    {
        if (_facets.IsStale(DateTimeOffset.UtcNow))
        {
            try
            {
                await _facets.RefreshAsync(
                    new RadioBrowserClient(Context.HttpClient), DateTimeOffset.UtcNow, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _context?.Logger.LogWarning(
                    exception, "Internet Radio could not read radio-browser's own lists.");
            }
        }

        return _facets;
    }

    /// <summary>What each station is playing right now, as it last announced.</summary>
    public NowPlaying NowPlaying { get; } = new();

    private NowPlayingListener? _listener;

    // LazyInitializer, not `??=`: two now-playing polls can race here, and `??=` on a
    // plain field can publish two listeners, one of which Dispose never reaches - its
    // connections would then outlive plugin unload. LazyInitializer guarantees only
    // the one it returns is ever the field's value.
    private NowPlayingListener Listener =>
        LazyInitializer.EnsureInitialized(
            ref _listener, () => new NowPlayingListener(Context.HttpClient, NowPlaying, Context.Logger));

    /// <summary>
    /// What the station is announcing, and a renewed lease on hearing it.
    ///
    /// The player polls this while the station plays, so the poll itself is what keeps
    /// the metadata connection open. The first answer after pressing play is usually
    /// empty: the station has not announced anything on the new connection yet.
    /// </summary>
    public async Task<string?> NowPlayingForAsync(string stationId, CancellationToken ct)
    {
        try
        {
            if (await ResolveStationAsync(stationId, ct) is { } station)
            {
                Listener.Touch(station);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A poll that cannot resolve the station still answers with whatever is known.
            _context?.Logger.LogDebug(exception, "Internet Radio could not resolve {Station} for now playing.", stationId);
        }

        return NowPlaying.Get(stationId);
    }

    /// <summary>
    /// Every station this process has already shown, by id.
    ///
    /// Held for the same reason the titles are: a station the viewer is looking at
    /// is one this plugin already fetched, and asking the network again for what
    /// it just handed out turns a service outage into a dead page.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RadioStation> _seen = new();

    private void Remember(IEnumerable<RadioStation> stations)
    {
        foreach (RadioStation station in stations)
        {
            _seen[station.Id] = station;
        }
    }

    // === Actions the controller calls ======================================

    /// <summary>
    /// Adds the station when it is not a favourite, removes it when it is.
    ///
    /// Removal needs no resolution - the id alone identifies the entry - so a station
    /// that has since vanished from radio-browser can still be un-favourited. Only
    /// adding needs a record, which is what makes the two halves asymmetric.
    /// </summary>
    public async Task<PluginActionOutcome> ToggleFavouriteAsync(
        string? userId, string stationId, CancellationToken ct)
    {
        if (userId is null)
        {
            return PluginActionOutcome.Failed("Sign in to keep favourites.");
        }

        if (await StateStore.RemoveFavouriteAsync(userId, stationId, ct))
        {
            return PluginActionOutcome.Ok("Removed from favourites.");
        }

        StationCatalog catalog = await Provider.GetAsync(ct);
        FavouriteResolver resolver = new(catalog, new RadioBrowserClient(Context.HttpClient));

        if (await resolver.ResolveAsync(stationId, ct) is not { } station)
        {
            // Deliberately not a success. A toggle that silently stores nothing reads to
            // the user as a broken button, and to the next reader of the file as data
            // that went missing on its own.
            return PluginActionOutcome.Failed("That station could not be found.");
        }

        await StateStore.AddFavouriteAsync(userId, station, ct);

        return PluginActionOutcome.Ok("Added to favourites.");
    }

    /// <summary>
    /// Remembers a submitted term so the refresh that follows can run it.
    ///
    /// Blank clears it, which returns the page to its "search for a station" state rather
    /// than leaving it insisting that nothing matched an empty query.
    /// </summary>
    public async Task<PluginActionOutcome> StoreSearchAsync(
        string? userId,
        string? query,
        string? genre,
        string? country,
        string? language,
        CancellationToken ct
    )
    {
        if (userId is null)
        {
            return PluginActionOutcome.Failed("Sign in to search.");
        }

        string? term = Clean(query);
        string? tag = Clean(genre);
        string? where = Clean(country);
        string? spoken = Clean(language);

        await StateStore.SetLastSearchAsync(userId, term, tag, where, spoken, ct);

        // Named back so the viewer can see the search was understood. A form with
        // four fields and one flat "Searching." leaves them guessing which of the
        // four the server actually took.
        string?[] said =
        [
            term is null ? null : $"\"{term}\"",
            tag,
            where,
            spoken,
        ];

        string summary = string.Join(", ", said.Where(part => part is not null));

        return PluginActionOutcome.Ok(
            summary.Length == 0 ? "Search cleared." : $"Searching for {summary}.");
    }

    /// <summary>What this viewer last asked for, on every axis.</summary>
    private static StationQuery StoredQuery(UserState state) =>
        new()
        {
            Name = Clean(state.LastSearch),
            Tag = Clean(state.LastGenre),
            Country = Clean(state.LastCountry),
            Language = Clean(state.LastLanguage),
        };

    /// <summary>
    /// Whether a query asks for anything at all.
    ///
    /// An empty one is not a search for nothing - it is the most-voted stations
    /// everywhere, which is the browse page's own content and not a result set to
    /// draw underneath it.
    /// </summary>
    private static bool IsAsking(StationQuery query) =>
        query.Name is not null
        || query.Tag is not null
        || query.Country is not null
        || query.Language is not null;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The landing page, with the box's own results under it when there are any.
    ///
    /// The query runs here rather than in the view: views are pure Build methods, which is
    /// what makes them cheap to test exhaustively and keeps this class the only thing that
    /// touches the wire.
    /// </summary>
    private async Task<PluginView> BuildBrowseAsync(
        StationCatalog catalog, UserState state, CancellationToken ct)
    {
        StationQuery stored = StoredQuery(state);
        if (!IsAsking(stored))
        {
            return BrowseView.Build(catalog, state, facets: await FacetsAsync(ct), logos: Logos);
        }

        try
        {
            return BrowseView.Build(
                catalog, state, await SearchAsync(stored, ct), facets: await FacetsAsync(ct), logos: Logos);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _context?.Logger.LogWarning(
                exception, "Internet Radio could not search for {Term}.", state.LastSearch);

            return BrowseView.Build(
                catalog, state, [], searchFailed: true, facets: await FacetsAsync(ct), logos: Logos);
        }
    }

    private async Task<IReadOnlyList<RadioStation>> SearchAsync(
        StationQuery query, CancellationToken ct)
    {
        RadioBrowserClient client = new(Context.HttpClient);

        return [.. StationGates.Admitted(
            await client.SearchAsync(query, RadioBrowserClient.SearchLimit, ct))];
    }

    /// <summary>
    /// The search page for whatever was asked for.
    ///
    /// The query runs here rather than in the view: views are pure Build methods, which is
    /// what makes them cheap to test exhaustively and keeps this class the only thing that
    /// touches the wire.
    ///
    /// A failed query renders as failed, not as "nothing found". The two ask the viewer to
    /// do different things, and reporting an outage as an empty result set has them
    /// respelling a search that was never the problem.
    /// </summary>
    private async Task<PluginView> BuildSearchAsync(
        string term, UserState state, CancellationToken ct)
    {
        // The route wins over the stored term, so /search/<term> is what it says it is and
        // a shared link does not show somebody else's last search.
        term = SearchTerms.Sanitise(term.Length > 0 ? term : state.LastSearch);

        // A genre or a country is a search on its own. Requiring a name as well
        // made every filter useless without one, which is backwards for a database
        // nobody knows the station names in.
        StationQuery query = StoredQuery(state) with { Name = term.Length > 0 ? term : null };

        if (term.Length is > 0 and < SearchTerms.MinLength || !IsAsking(query))
        {
            return SearchView.Build(term, [], queryFailed: false, state, await FacetsAsync(ct), Logos);
        }

        try
        {
            IReadOnlyList<RadioStation> found = await SearchAsync(query, ct);
            Remember(found);

            return SearchView.Build(term, found, queryFailed: false, state, await FacetsAsync(ct), Logos);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _context?.Logger.LogWarning(
                exception, "Internet Radio could not search for {Term}.", term);

            return SearchView.Build(term, [], queryFailed: true, state, await FacetsAsync(ct), Logos);
        }
    }

    /// <summary>
    /// One station's page, for a station that need not be in the catalogue.
    ///
    /// A search result is opened by id and was never cached - it came straight off
    /// radio-browser - so the catalogue alone cannot answer this. FavouriteResolver already
    /// knows the order to try: the catalogue first, then the network, then give up.
    /// </summary>
    private async Task<PluginView> BuildStationAsync(
        StationCatalog catalog, string id, UserState state, CancellationToken ct)
    {
        async Task<PluginView> Page(RadioStation? station) =>
            station is null
                ? StationView.Build(station: null, state)
                : StationView.Build(
                    station, state, NowPlaying.Get(id), await Streams.ForAsync(station, ct), Logos);

        if (catalog.ById(id) is { } known)
        {
            return await Page(known);
        }

        // A station this session already put on screen. Opening a search result
        // asked radio-browser for it a second time, so the page said "Station not
        // found" whenever that service was unreachable - about a station whose
        // name and stream url were in the very list the viewer just tapped.
        if (_seen.TryGetValue(id, out RadioStation? recent))
        {
            return await Page(recent);
        }

        try
        {
            FavouriteResolver resolver = new(catalog, new RadioBrowserClient(Context.HttpClient));

            return await Page(await resolver.ResolveAsync(id, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _context?.Logger.LogWarning(
                exception, "Internet Radio could not resolve the station {Station}.", id);

            // The "not found" page, not the error page: the viewer's next move is the same
            // either way, and it is on that page.
            return StationView.Build(station: null, state);
        }
    }

    /// <summary>
    /// The fallback, and a log line naming the path that reached it.
    ///
    /// Without the log this page is a dead end for whoever has to diagnose it: the viewer
    /// is told there is no page at that address, and nothing anywhere records which
    /// address that was. That is exactly how /genre/:slug went unnoticed - every test
    /// passed, because the tests hand Parse the string this plugin builds rather than the
    /// one a client actually sends.
    /// </summary>
    private PluginView UnknownRoute(string? route)
    {
        _context?.Logger.LogWarning(
            "Internet Radio has no page for the route {Route}.", route ?? "(null)");

        return PluginViews.Declarative(
            PluginViews.EmptyState(
                "unknown-route",
                "Nothing here",
                "This version of Internet Radio has no page at that address."
            )
        );
    }

    /// <summary>
    /// One station by id, from the catalogue or from radio-browser.
    ///
    /// Public because the player page needs it too, and a search result is by definition
    /// not in the catalogue.
    /// </summary>
    public async Task<RadioStation?> ResolveStationAsync(string stationId, CancellationToken ct)
    {
        StationCatalog catalog = await Provider.GetAsync(ct);

        if (catalog.ById(stationId) is { } known)
        {
            return known;
        }

        if (_seen.TryGetValue(stationId, out RadioStation? recent))
        {
            return recent;
        }

        FavouriteResolver resolver = new(catalog, new RadioBrowserClient(Context.HttpClient));

        return await resolver.ResolveAsync(stationId, ct);
    }

    // === IScheduledTaskPlugin ==============================================

    public string CronExpression => DefaultCron;

    // Read before Initialize by the host while it registers the plugin, so this must
    // not reach for the context. A constant cadence is the honest answer anyway:
    // there is no setting to read, because there is no way to save one.
    public IReadOnlyList<PluginScheduledJob> Jobs { get; } =
        [new PluginScheduledJob(RefreshJobName, DefaultCron)];

    public Task ExecuteAsync(CancellationToken ct = default) => ExecuteAsync(RefreshJobName, ct);

    public async Task ExecuteAsync(string jobName, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (jobName != RefreshJobName)
        {
            throw new ArgumentOutOfRangeException(nameof(jobName), jobName, "Unknown job name.");
        }

        IPluginContext context = Context;
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(ct, _lifecycleCts.Token);

        // Before the catalogue, so the owner is asked once, early, and not by a page that
        // was already refused.
        await Grants.EnsureAsync(linked.Token);

        StationCatalog catalog = await Provider.RefreshAsync(linked.Token);

        // The catalogue's own logos are fetched here, on the quiet hour, so the pages most
        // people open never show a placeholder. A failure costs logos, not the refresh.
        try
        {
            await FetchLogosAsync(new LogoFetcher(context.HttpClient), catalog.Stations, linked.Token);
        }
        catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            context.Logger.LogWarning(exception, "Internet Radio could not fetch the catalogue's logos.");
        }

        // RefreshAsync deliberately does not consult StationOverrides - it keeps the
        // cache warm for the day a user deletes their override file - but every view
        // still resolves through GetAsync, which checks the override first. When one
        // is active, "fetched" here is not what any screen is showing, and the log
        // has to say so rather than let an operator watching both conclude something
        // is broken because the log says Fetched while the settings page says
        // UserOverride. This changes only the wording: the refresh still runs, still
        // hits the network and still rewrites the cache exactly as before.
        bool overrideActive = StationOverrides.TryLoad(context.DataFolderPath, context.Logger) is not null;

        if (overrideActive)
        {
            context.Logger.LogInformation(
                "Internet Radio refreshed its catalogue in the background: {Count} stations from {Source}. "
                    + "A user override is active, so views are unaffected by this refresh.",
                catalog.Count,
                catalog.Source
            );
        }
        else
        {
            context.Logger.LogInformation(
                "Internet Radio refreshed its catalogue: {Count} stations from {Source}.",
                catalog.Count,
                catalog.Source
            );
        }
    }

    // === IUiPlugin =========================================================

    /// <summary>
    /// The pages this plugin has, as the host advertises them.
    ///
    /// Without this the host registers no named page: every view falls back to the
    /// standard layout, no page has a title of its own, and the client reaches them all
    /// through one wildcard. RadioRoutes.Parse still decides what a request means - this
    /// is the list, not the dispatcher - and RadioRoutesTests holds the two together.
    /// </summary>
    public static readonly PluginRouteTable Table = new(
        new PluginRoute
        {
            Name = "browse",
            Path = RadioRoutes.Browse,
            Label = "stations.title",
            Layout = PluginLayout.Grid,
        },
        new PluginRoute
        {
            Name = "all",
            Path = RadioRoutes.AllStations,
            Label = "stations.all",
            Layout = PluginLayout.Grid,
        },
        new PluginRoute
        {
            Name = "search",
            Path = RadioRoutes.SearchRoot,
            Label = "search.title",
            Layout = PluginLayout.Grid,
        },
        new PluginRoute
        {
            Name = "search-term",
            Path = $"{RadioRoutes.SearchRoot}/:term",
            Label = "search.title",
            Layout = PluginLayout.Grid,
        },
        new PluginRoute
        {
            Name = "genre",
            Path = $"{RadioRoutes.GenreRoot}/:slug",
            Label = "genre.title",
            Layout = PluginLayout.Grid,
        },
        new PluginRoute
        {
            Name = "station",
            Path = $"{RadioRoutes.StationRoot}/:id",
            Label = "station.title",
            Layout = PluginLayout.Standard,
        },
        new PluginRoute
        {
            Name = "settings",
            Path = RadioRoutes.Settings,
            Label = "settings.title",
            Layout = PluginLayout.Form,
            Access = PluginRouteAccess.Owner,
        }
    );

    public PluginRouteTable Routes => Table;

    // One entry per manifest mount; DiscoveryContractTests asserts the two agree.
    public IReadOnlyList<PluginNavEntry> NavEntries { get; } =
        [
            new PluginNavEntry
            {
                Section = PluginUiSection.Music,
                Label = "stations.title",
                Icon = "portableRadio",
                Route = RadioRoutes.Browse,
            },
            new PluginNavEntry
            {
                Section = PluginUiSection.Settings,
                Label = "settings.title",
                Icon = "portableRadio",
                Route = RadioRoutes.Settings,
                Access = PluginRouteAccess.Owner,
                Slot = PluginSlot.Section,
            },
        ];

    public async Task<PluginView> GetViewAsync(PluginViewRequest request, CancellationToken ct)
    {
        // A view request racing Dispose is not the caller's bug the way a tick is:
        // the host may still be draining a page render while tearing down. Answer
        // with something renderable instead of throwing into the request pipeline.
        if (_disposed)
        {
            return PluginViews.Declarative(
                PluginViews.EmptyState(
                    "plugin-unavailable",
                    "Internet Radio is unavailable",
                    "This plugin is disabled or is being unloaded."
                )
            );
        }

        // Context and route parsing sit inside the try along with everything else,
        // not resolved ahead of it: a view request that somehow arrives before
        // Initialize is unreachable given the host's ordering, but it is not this
        // caller's bug the way a tick out of order is, and it must land on the same
        // error view as any other failure to build one rather than throw into the
        // request pipeline.
        try
        {
            IPluginContext context = Context;

            RadioRoute route = RadioRoutes.Parse(request.Route);
            StationCatalog catalog = await Provider.GetAsync(ct);

            // Through UserKey, never request.UserId: that is the Ulid spelling of the same
            // person, and state is keyed by the Guid spelling the controller writes.
            string? userKey = UserKey.FromCaller(request.CallerId);
            UserState state = userKey is null
                ? UserState.Empty
                : await StateStore.GetAsync(userKey, ct);

            PluginView view = route.Kind switch
            {
                RadioRouteKind.Browse => await BuildBrowseAsync(catalog, state, ct),
                RadioRouteKind.Search => await BuildSearchAsync(route.Value, state, ct),
                RadioRouteKind.Genre => GenreView.Build(catalog, route.Value, state, Logos),
                RadioRouteKind.AllStations => AllStationsView.Build(catalog, state, Logos),
                RadioRouteKind.Station => await BuildStationAsync(catalog, route.Value, state, ct),
                RadioRouteKind.Settings => SettingsView.Build(
                    catalog, context.DataFolderPath, DateTimeOffset.UtcNow, NextRefreshUtc(DateTimeOffset.UtcNow), state),
                _ => UnknownRoute(request.Route),
            };

            FetchLogosInBackground();

            return view;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // These pages are the plugin's only diagnostic surface, so a failure that
            // throws through them hides its own cause: the owner sees a broken panel
            // instead of learning what went wrong. The rendered text names what
            // failed and never the exception detail. Logged only when a context
            // exists to log through - a request this early has nowhere sanctioned to
            // report to, and that is not this caller's fault either.
            _context?.Logger.LogError(exception, "Internet Radio could not build the view for {Route}.", request.Route);

            return PluginViews.Declarative(
                PluginViews.Container(
                    "view-error",
                    PluginViews.Badge("view-error-badge", "Unavailable", PluginBadgeVariant.Danger),
                    PluginViews.EmptyState(
                        "view-error-empty",
                        "This page could not be built",
                        "Check the server log for Internet Radio."
                    ),
                    PluginViews.Button("view-error-retry", "Try again", PluginActionIntent.RefreshView())
                )
            );
        }
    }

    // Null-safe before Initialize (the host may dispose a plugin whose load failed)
    // and idempotent (a double dispose is not worth throwing over).
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener?.Dispose();
        _lifecycleCts.Cancel();
        _lifecycleCts.Dispose();
    }
}
