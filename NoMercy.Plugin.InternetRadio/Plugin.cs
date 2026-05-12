using System.Text.Json;
using Microsoft.Extensions.Logging;
using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// Entry point for the Internet Radio Provider plugin.
///
/// Implements <see cref="IMediaSourcePlugin"/> (which itself extends
/// <see cref="IPlugin"/>) so the NoMercy MediaServer treats the bundled
/// station list as a music media source.
///
/// Discovery contract (see <c>PluginManager.LoadPluginFromManifestAsync</c>):
///   1. Server scans <c>&lt;server&gt;/plugins/&lt;PluginFolder&gt;/plugin.json</c>.
///   2. Loads the assembly named by the manifest's <c>assembly</c> field.
///   3. Reflects every public, non-abstract type assignable to <see cref="IPlugin"/>.
///   4. Instantiates it via parameterless constructor (<c>Activator.CreateInstance</c>).
///   5. Calls <see cref="Initialize"/> with the server-supplied context.
///   6. When the media library wants to enumerate this source, it calls
///      <see cref="ScanAsync"/>.
///
/// IMPORTANT: the <see cref="Id"/> here must match the <c>id</c> field in
/// <c>plugin.json</c>; the server uses it as the lifecycle identity across restarts.
/// </summary>
public sealed class Plugin : IMediaSourcePlugin
{
    // === IPlugin metadata ===================================================

    /// <inheritdoc />
    public string Name => "Internet Radio Provider";

    /// <inheritdoc />
    public string Description =>
        "Adds a curated list of internet radio stations as a music media source.";

    /// <inheritdoc />
    public Guid Id { get; } = Guid.Parse("b3d4f1a2-7c5e-4d8a-9f10-1c2b3a4d5e6f");

    /// <inheritdoc />
    public Version Version { get; } = new(1, 0, 0);

    // === Internal state =====================================================

    /// <summary>Filename of an optional user-supplied station override list.</summary>
    private const string OverrideFileName = "stations.json";

    private IPluginContext? _context;
    private IReadOnlyList<RadioStation> _stations = RadioStations.Defaults;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

    // === IPlugin lifecycle ==================================================

    /// <summary>
    /// Called once by the PluginManager after the assembly is loaded.
    ///
    /// Responsibilities:
    ///   - Stash the context so logging/event-bus access is available later.
    ///   - Attempt to load a user-supplied <c>stations.json</c> from the
    ///     plugin's per-instance data folder; fall back to the built-in list.
    /// </summary>
    public void Initialize(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        _stations = TryLoadStationOverrides(context) ?? RadioStations.Defaults;

        context.Logger.LogInformation(
            "{PluginName} v{Version} initialised with {Count} station(s).",
            Name,
            Version,
            _stations.Count
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to release — kept for IDisposable contract.
        _context = null;
        GC.SuppressFinalize(this);
    }

    // === IMediaSourcePlugin ================================================

    /// <summary>
    /// Returns the list of radio stations as <see cref="MediaFile"/> records.
    ///
    /// The <paramref name="path"/> argument is normally a filesystem path for
    /// disk-backed providers; for this network-backed provider it is treated
    /// as an optional case-insensitive substring filter against the station
    /// genre (e.g. <c>"rock"</c>, <c>"ambient"</c>). Pass <c>null</c> or
    /// empty for the full list.
    /// </summary>
    public Task<IEnumerable<MediaFile>> ScanAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IEnumerable<RadioStation> stations = _stations;

        if (!string.IsNullOrWhiteSpace(path))
        {
            stations = stations.Where(s =>
                s.Genre is not null
                && s.Genre.Contains(path, StringComparison.OrdinalIgnoreCase)
            );
        }

        IEnumerable<MediaFile> result = stations.Select(ToMediaFile);
        return Task.FromResult(result);
    }

    // === Helpers ============================================================

    /// <summary>
    /// Convert a <see cref="RadioStation"/> to a server-shaped
    /// <see cref="MediaFile"/>.
    ///
    /// Conventions:
    ///   - <c>Path</c> holds the stream URL (the playback layer treats it as
    ///     an opaque, FFmpeg-resolvable source).
    ///   - <c>FileName</c> is the human-readable station name.
    ///   - <c>Size</c> is zero — live streams have no fixed length.
    ///   - <c>Type</c> is <see cref="MediaType.Music"/>.
    ///   - <c>Properties</c> carries every optional field so downstream
    ///     metadata/UI layers can read it without re-parsing the URL.
    /// </summary>
    private static MediaFile ToMediaFile(RadioStation station)
    {
        Dictionary<string, string> props = new(StringComparer.OrdinalIgnoreCase)
        {
            ["streamUrl"] = station.StreamUrl,
            ["stationName"] = station.Name,
            ["isLive"] = "true",
        };

        if (!string.IsNullOrWhiteSpace(station.LogoUrl))
            props["logoUrl"] = station.LogoUrl;
        if (!string.IsNullOrWhiteSpace(station.Homepage))
            props["homepage"] = station.Homepage;
        if (!string.IsNullOrWhiteSpace(station.Genre))
            props["genre"] = station.Genre;
        if (!string.IsNullOrWhiteSpace(station.Country))
            props["country"] = station.Country;
        if (station.BitrateKbps is { } br)
            props["bitrateKbps"] = br.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(station.Codec))
            props["codec"] = station.Codec;

        return new MediaFile
        {
            Path = station.StreamUrl,
            FileName = station.Name,
            Size = 0,
            Type = MediaType.Music,
            Properties = props,
        };
    }

    /// <summary>
    /// Looks for a user-supplied <c>stations.json</c> override in the plugin's
    /// data folder (<see cref="IPluginContext.DataFolderPath"/>). When present
    /// and parseable, replaces the built-in station list.
    ///
    /// Returns <c>null</c> if no override exists or parsing fails — in which
    /// case the caller falls back to <see cref="RadioStations.Defaults"/>.
    /// </summary>
    private static IReadOnlyList<RadioStation>? TryLoadStationOverrides(IPluginContext context)
    {
        string overridePath = Path.Combine(context.DataFolderPath, OverrideFileName);
        if (!File.Exists(overridePath))
            return null;

        try
        {
            string json = File.ReadAllText(overridePath);
            List<RadioStation>? parsed = JsonSerializer.Deserialize<List<RadioStation>>(
                json,
                JsonOptions
            );

            if (parsed is null || parsed.Count == 0)
                return null;

            // Defensive: drop entries missing the absolutely-required fields.
            List<RadioStation> valid = parsed
                .Where(s =>
                    !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.StreamUrl)
                )
                .ToList();

            return valid.Count > 0 ? valid : null;
        }
        catch (Exception ex)
        {
            context.Logger.LogWarning(
                ex,
                "Failed to load station overrides from {Path}; using built-in defaults.",
                overridePath
            );
            return null;
        }
    }
}
