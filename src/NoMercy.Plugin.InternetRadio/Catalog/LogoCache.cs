// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// Station logos as data: URIs, kept on disk so a restart does not fetch them all again.
///
/// A view only ever reads from here. A logo that is not here yet is noted as wanted and
/// the card draws its placeholder; the plugin fetches what is wanted after the page has
/// gone out, so no page ever waits on a logo server. A logo that was refused - too big,
/// not an image, gone - is stored as "none", or every page would fetch it again.
///
/// Keyed by station and logo address together, so a station that moves its logo gets the
/// new one. Bounded at <see cref="DefaultCapacity"/> entries, least recently used first
/// out; at the 16 KB fetch cap that is at most about 40 MB of text on disk.
/// </summary>
public sealed class LogoCache : IStationLogos
{
    public const int DefaultCapacity = 2000;

    private const string FolderName = "logos";

    private readonly string _folder;
    private readonly int _capacity;
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, RadioStation> _pending = new();
    private readonly Lock _evicting = new();
    private long _clock;

    public LogoCache(string dataFolderPath, int capacity = DefaultCapacity)
    {
        _folder = Path.Combine(dataFolderPath, FolderName);
        _capacity = capacity;
        Directory.CreateDirectory(_folder);

        // Oldest first, so the ones read back most recently are the ones that stay.
        foreach (FileInfo file in new DirectoryInfo(_folder).GetFiles("*.txt").OrderBy(file => file.LastWriteTimeUtc))
        {
            _entries[Path.GetFileNameWithoutExtension(file.Name)] = new Entry(null, Loaded: false, Tick());
        }

        Evict();
    }

    /// <summary>An absolute http or https address, or null. The browser never sees it.</summary>
    public static string? SourceOf(RadioStation station) =>
        Uri.TryCreate(station.LogoUrl, UriKind.Absolute, out Uri? parsed)
        && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
            ? station.LogoUrl
            : null;

    public string? DataUriFor(RadioStation station)
    {
        if (SourceOf(station) is not { } source)
        {
            return null;
        }

        string key = Key(station.Id, source);

        if (!_entries.TryGetValue(key, out Entry? entry))
        {
            _pending.TryAdd(key, station);
            return null;
        }

        if (!entry.Loaded)
        {
            entry = entry with { DataUri = Read(key), Loaded = true };
        }

        _entries[key] = entry with { LastUsed = Tick() };

        return entry.DataUri;
    }

    public bool Has(RadioStation station) =>
        SourceOf(station) is { } source && _entries.ContainsKey(Key(station.Id, source));

    /// <summary>Everything views asked for since the last call, each once.</summary>
    public IReadOnlyList<RadioStation> TakePending()
    {
        List<RadioStation> taken = [];

        foreach (string key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out RadioStation? station))
            {
                taken.Add(station);
            }
        }

        return taken;
    }

    /// <summary>A fetched logo, or null for one that was refused for good.</summary>
    public async Task StoreAsync(RadioStation station, string? dataUri, CancellationToken ct)
    {
        if (SourceOf(station) is not { } source)
        {
            return;
        }

        string key = Key(station.Id, source);

        // Written whole, then moved into place: a crash mid-write must not leave a half
        // data URI that draws as a broken image on every page from then on.
        string path = PathOf(key);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, dataUri ?? string.Empty, ct);
        File.Move(temporary, path, overwrite: true);

        _entries[key] = new Entry(dataUri, Loaded: true, Tick());
        _pending.TryRemove(key, out _);

        Evict();
    }

    private void Evict()
    {
        lock (_evicting)
        {
            int excess = _entries.Count - _capacity;
            if (excess <= 0)
            {
                return;
            }

            foreach (string key in _entries.OrderBy(pair => pair.Value.LastUsed).Take(excess).Select(pair => pair.Key).ToList())
            {
                _entries.TryRemove(key, out _);
                File.Delete(PathOf(key));
            }
        }
    }

    private string? Read(string key)
    {
        string path = PathOf(key);
        if (!File.Exists(path))
        {
            return null;
        }

        string text = File.ReadAllText(path);
        return text.Length == 0 ? null : text;
    }

    private string PathOf(string key) => Path.Combine(_folder, $"{key}.txt");

    private long Tick() => Interlocked.Increment(ref _clock);

    // Hashed rather than used as-is: a station id and a url are not file names.
    private static string Key(string stationId, string source) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{stationId}\n{source}")))[..32];

    private sealed record Entry(string? DataUri, bool Loaded, long LastUsed);
}
