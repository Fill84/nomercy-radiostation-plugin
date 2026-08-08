// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Globalization;
using Microsoft.Extensions.Logging;
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
    /// Stores the term so the refreshed /search view can run it. Blank clears it, which
    /// is what returns the page to its "search for a station" state rather than leaving
    /// it insisting nothing matched an empty query.
    /// </summary>
    public async Task<PluginActionOutcome> StoreSearchAsync(
        string? userId, string? query, CancellationToken ct)
    {
        if (userId is null)
        {
            return PluginActionOutcome.Failed("Sign in to search.");
        }

        string? term = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        await StateStore.SetLastSearchAsync(userId, term, ct);

        return PluginActionOutcome.Ok(term is null ? "Search cleared." : $"Searching for {term}.");
    }

    /// <summary>
    /// The landing page, with the stored search run live if there is one.
    ///
    /// The query runs here rather than in the view: views are pure Build methods, which is
    /// what makes them cheap to test exhaustively and keeps this class the only thing that
    /// touches the wire.
    ///
    /// A failed query renders as failed, not as "nothing found". The two ask the viewer to
    /// do different things, and reporting an outage as an empty result set has them
    /// retrying a search that was never the problem.
    /// </summary>
    private async Task<PluginView> BuildBrowseAsync(
        StationCatalog catalog, UserState state, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state.LastSearch))
        {
            return BrowseView.Build(catalog, state);
        }

        try
        {
            RadioBrowserClient client = new(Context.HttpClient);
            IReadOnlyList<RadioBrowserStation> wire =
                await client.SearchByNameAsync(state.LastSearch, RadioBrowserClient.SearchLimit, ct);

            return BrowseView.Build(catalog, state, [.. StationGates.Admitted(wire)]);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _context?.Logger.LogWarning(
                exception, "Internet Radio could not search for {Term}.", state.LastSearch);

            return BrowseView.Build(catalog, state, [], searchFailed: true);
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
    /// <summary>
    /// The pages this plugin serves. Declaring them is what lets the host list them, pick
    /// a shell per page, and tell a client that a path is real - none of which it can do
    /// for a route that exists only as a case in this plugin's own switch.
    /// </summary>
    public PluginRouteTable Routes => RadioRoutes.Table;

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
                RadioRouteKind.Browse => await BuildBrowseAsync(catalog, state, ct),
                // The same page. Results live under the field that produced them, so
                // /search has nothing of its own to draw - it stays declared and resolves
                // so an old link is not a dead end.
                RadioRouteKind.Search => await BuildBrowseAsync(catalog, state, ct),
                RadioRouteKind.Genre => GenreView.Build(catalog, route.Value, state),
                RadioRouteKind.AllStations => AllStationsView.Build(catalog),
                RadioRouteKind.Station => StationView.Build(catalog, route.Value, state),
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
