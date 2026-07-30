// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

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
    /// </summary>
    private static readonly string DefaultCron = $"0 {RefreshHourUtc} * * *";

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
    private bool _disposed;

    // Field-initialised so Dispose has something to cancel even when the host
    // disposes a plugin whose load never completed. Every tick links this into the
    // token it runs under, which is what makes "Dispose cancels in-flight work" real
    // rather than aspirational.
    private readonly CancellationTokenSource _lifecycleCts = new();

    public string Name => PluginIdentity.Name;
    public string Description => PluginIdentity.Description;
    public Guid Id => PluginIdentity.Id;
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
            RadioRoute route = RadioRoutes.Parse(request.Route);
            StationCatalog catalog = await Provider.GetAsync(ct);

            return route.Kind switch
            {
                RadioRouteKind.Browse => BrowseView.Build(catalog),
                RadioRouteKind.Genre => GenreView.Build(catalog, route.Value),
                RadioRouteKind.AllStations => AllStationsView.Build(catalog),
                RadioRouteKind.Station => StationView.Build(catalog, route.Value),
                RadioRouteKind.Settings => SettingsView.Build(
                    catalog, context.DataFolderPath, DateTimeOffset.UtcNow, NextRefreshUtc(DateTimeOffset.UtcNow)),
                _ => PluginViews.Declarative(
                    PluginViews.EmptyState(
                        "unknown-route",
                        "Nothing here",
                        "This version of Internet Radio has no page at that address."
                    )
                ),
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
