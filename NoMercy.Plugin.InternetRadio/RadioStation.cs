namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// Immutable description of a single internet radio station.
/// Serialised as JSON when users supply their own station list via
/// <c>stations.json</c> in the plugin's data folder.
/// </summary>
public sealed record RadioStation
{
    /// <summary>Display name shown to users (e.g. "BBC Radio 1").</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Direct stream URL (HTTPS preferred). The server hands this to its
    /// playback layer as-is; the URL must point at a real audio stream
    /// (MP3, AAC, Opus, HLS, …) — not a station home page.
    /// </summary>
    public required string StreamUrl { get; init; }

    /// <summary>Optional logo/artwork URL used by the UI.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>Optional station homepage for "more info" links.</summary>
    public string? Homepage { get; init; }

    /// <summary>Free-text genre tag used for the optional <c>ScanAsync</c> filter.</summary>
    public string? Genre { get; init; }

    /// <summary>ISO country code or country name (informational only).</summary>
    public string? Country { get; init; }

    /// <summary>Stream bitrate in kbps (informational only).</summary>
    public int? BitrateKbps { get; init; }

    /// <summary>Audio codec used by the stream (e.g. "mp3", "aac").</summary>
    public string? Codec { get; init; }
}
