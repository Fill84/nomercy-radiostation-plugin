// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using NoMercy.Plugins.Abstractions;

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
    /// Writes down what an NMSearchInput submitted, and changes nothing.
    ///
    /// The whole point is the log line. See InternetRadioController.Submit for why this
    /// exists and what is decided by what turns up in it.
    /// </summary>
    public Task<PluginActionOutcome> RecordSearchSubmissionAsync(string? body, CancellationToken ct)
    {
        _context?.Logger.LogInformation(
            "Internet Radio received a search input submission. Body: {Body}",
            string.IsNullOrWhiteSpace(body) ? "(empty)" : body);

        return Task.FromResult(PluginActionOutcome.Ok(
            "Typing is not wired up yet - spell the name with the keys, or put it in the address bar."));
    }

    /// <summary>
    /// The search page for whatever has been spelled so far.
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
        if (term.Length < SearchTerms.MinLength)
        {
            return SearchView.Build(term, [], queryFailed: false, state);
        }

        try
        {
            RadioBrowserClient client = new(Context.HttpClient);
            IReadOnlyList<RadioBrowserStation> wire =
                await client.SearchByNameAsync(term, RadioBrowserClient.SearchLimit, ct);

            return SearchView.Build(term, [.. StationGates.Admitted(wire)], queryFailed: false, state);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _context?.Logger.LogWarning(
                exception, "Internet Radio could not search for {Term}.", term);

            return SearchView.Build(term, [], queryFailed: true, state);
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
        if (catalog.ById(id) is { } known)
        {
            return StationView.Build(known, state);
        }

        try
        {
            FavouriteResolver resolver = new(catalog, new RadioBrowserClient(Context.HttpClient));

            return StationView.Build(await resolver.ResolveAsync(id, ct), state);
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
    /// Fetches a station's stream or cover, so the browser never talks to the station.
    ///
    /// The dashboard's Content-Security-Policy allows media and images from NoMercy's own
    /// hosts and nowhere else, and every radio stream and station logo is on somebody
    /// else's domain. Handing the client those urls directly is what made every station
    /// silent and every cover blank. Fetched here, the browser only ever sees this
    /// server - which the policy already allows - and the owner's consent stays the thing
    /// that governs where the bytes come from.
    ///
    /// Returns null when the station cannot be resolved. The caller turns that into a 404
    /// rather than an empty 200, because a media element retries a 200 with no body.
    /// </summary>
    public async Task<HttpResponseMessage?> FetchStationMediaAsync(
        string stationId, bool cover, string? range, CancellationToken ct)
    {
        StationCatalog catalog = await Provider.GetAsync(ct);
        RadioBrowserClient client = new(Context.HttpClient);
        FavouriteResolver resolver = new(catalog, client);

        if (await resolver.ResolveAsync(stationId, ct) is not { } station)
        {
            return null;
        }

        string? url = cover ? StationCards.CoverUrl(station) : station.StreamUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        HttpRequestMessage request = new(HttpMethod.Get, url);

        // Forwarded verbatim so seeking and buffering keep working: a player asks for a
        // byte range and expects a 206 back. Swallowing the header would turn every
        // request into a fetch of the whole stream from the beginning, which for a live
        // stream never ends.
        if (!string.IsNullOrWhiteSpace(range))
        {
            request.Headers.TryAddWithoutValidation("Range", range);
        }

        // ResponseHeadersRead, not the default: this must start relaying as soon as the
        // headers arrive rather than buffering a live stream into memory first.
        return await Context.HttpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    /// <summary>
    /// The bearer token of the viewer this view is being rendered for, or null.
    ///
    /// Media elements cannot send a header, so the relay urls carry it as a query
    /// parameter, which the host's TokenParamAuthMiddleware promotes back to one. Taking it
    /// from the live request rather than storing one keeps a url valid only for the viewer
    /// it was built for.
    /// </summary>
    private string? CallerBearerToken()
    {
        // Wrapped, like PublicBaseUrl: this is a convenience, and a host that mediates
        // nothing must not cost the viewer the page. Unwrapped it took every route down to
        // the error view, which six lifecycle tests caught before anyone saw it.
        try
        {
            return ReadCallerBearerToken();
        }
        catch (Exception exception)
        {
            _context?.Logger.LogWarning(
                exception, "Internet Radio could not read the caller's token.");

            return null;
        }
    }

    private string? ReadCallerBearerToken()
    {
        if (_context?.Services.GetService(typeof(IHttpContextAccessor)) is not IHttpContextAccessor accessor)
        {
            return null;
        }

        string? header = accessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header))
        {
            // Already a query parameter on the request that reached us - the app does this
            // for its own media, so a view request can arrive that way too.
            return accessor.HttpContext?.Request.Query
                .FirstOrDefault(entry => entry.Key is "token" or "access_token").Value.ToString()
                is { Length: > 0 } fromQuery
                ? fromQuery
                : null;
        }

        const string bearer = "Bearer ";

        return header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
            ? header[bearer.Length..].Trim()
            : header.Trim();
    }

    /// <summary>
    /// Scheme and authority of the request currently being served, or null outside one.
    ///
    /// Read through the host's own accessor rather than guessed from configuration: the
    /// server answers on several addresses - a LAN ip, a tunnelled hostname - and the only
    /// correct one is whichever this viewer actually reached.
    /// </summary>
    private string? PublicBaseUrl()
    {
        try
        {
            if (_context?.Services.GetService(typeof(IHttpContextAccessor)) is not IHttpContextAccessor accessor)
            {
                return null;
            }

            HttpRequest? request = accessor.HttpContext?.Request;

            return request is null ? null : $"{request.Scheme}://{request.Host}";
        }
        catch (Exception exception)
        {
            _context?.Logger.LogWarning(
                exception, "Internet Radio could not determine this server's public address.");

            return null;
        }
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

        StationCatalog catalog = await Provider.RefreshAsync(linked.Token);

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

    // One entry per manifest mount. DiscoveryContractTests asserts the two agree,
    // since PluginUiDescriptorDto prefers this over the manifest and nothing else
    // would catch them drifting.
    public IReadOnlyList<PluginNavEntry> NavEntries { get; } =
        [
            new PluginNavEntry
            {
                Section = PluginUiSection.Music,
                Label = PluginIdentity.Name,
                Icon = "portableRadio",
                Route = RadioRoutes.Browse,
            },
            new PluginNavEntry
            {
                Section = PluginUiSection.Settings,
                Label = PluginIdentity.Name,
                Icon = "portableRadio",
                Route = RadioRoutes.Settings,
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

            // The server's own public address, learned from the request being served. It
            // is not on IPluginContext and cannot be: the plugin is loaded once and the
            // address is a property of how a client reached it.
            MediaProxy.Remember(PublicBaseUrl(), CallerBearerToken());

            RadioRoute route = RadioRoutes.Parse(request.Route);
            StationCatalog catalog = await Provider.GetAsync(ct);

            // Read once per request, not once per card. Every view that shows a station
            // needs to know whether this viewer kept it, and a per-card read would open
            // the file eighteen times to answer the same question.
            UserState state = request.UserId is null
                ? UserState.Empty
                : await StateStore.GetAsync(request.UserId, ct);

            return route.Kind switch
            {
                RadioRouteKind.Browse => BrowseView.Build(catalog, state),
                RadioRouteKind.Search => await BuildSearchAsync(route.Value, state, ct),
                RadioRouteKind.Genre => GenreView.Build(catalog, route.Value, state),
                RadioRouteKind.AllStations => AllStationsView.Build(catalog),
                RadioRouteKind.Station => await BuildStationAsync(catalog, route.Value, state, ct),
                RadioRouteKind.Settings => SettingsView.Build(
                    catalog, context.DataFolderPath, DateTimeOffset.UtcNow, NextRefreshUtc(DateTimeOffset.UtcNow), state),
                _ => UnknownRoute(request.Route),
            };
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
        _lifecycleCts.Cancel();
        _lifecycleCts.Dispose();
    }
}
