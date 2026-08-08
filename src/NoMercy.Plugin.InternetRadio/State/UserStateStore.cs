// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json;

namespace NoMercy.Plugin.InternetRadio;

// Per-user favourites and the last search term, in one file beside the catalogue cache.
//
// One file rather than two. The search term needs exactly what favourites need - state
// per viewer that survives a form submit - and splitting them would mean two locks, two
// atomic writes, and two chances to get the same problem wrong in different ways.
//
// The data folder rather than IPluginConfiguration: configuration is for a plugin's
// settings, which an owner edits and which have a fixed shape. This is user data that
// grows, and it belongs where catalog-cache.json already lives.
public sealed class UserStateStore(string dataFolderPath)
{
    public const string FileName = "user-state.json";

    private static readonly JsonSerializerOptions Json =
        new() { WriteIndented = true };

    // Every read-modify-write goes through this. Two viewers clicking a toggle in the
    // same second is the ordinary case on a family server, and last-writer-wins on a
    // whole-file rewrite would silently drop the other's favourite.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string Path => System.IO.Path.Combine(dataFolderPath, FileName);

    public async Task<UserState> GetAsync(string userId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Dictionary<string, UserState> all = await ReadAsync(ct);
            return all.TryGetValue(userId, out UserState? state) ? state : UserState.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>False when the station was already a favourite, so nothing changed.</summary>
    public Task<bool> AddFavouriteAsync(string userId, RadioStation station, CancellationToken ct) =>
        MutateAsync(userId, state =>
        {
            if (state.Favourites.Any(existing => existing.Id == station.Id))
            {
                return (state, false);
            }

            return (state with { Favourites = [.. state.Favourites, station] }, true);
        }, ct);

    /// <summary>False when there was nothing to remove. Not an error - see the controller.</summary>
    public Task<bool> RemoveFavouriteAsync(string userId, string stationId, CancellationToken ct) =>
        MutateAsync(userId, state =>
        {
            RadioStation[] kept = [.. state.Favourites.Where(station => station.Id != stationId)];

            return kept.Length == state.Favourites.Count
                ? (state, false)
                : (state with { Favourites = kept }, true);
        }, ct);

    /// <summary>Remembers what was searched for, so a refresh can run it again.</summary>
    public Task SetLastSearchAsync(string userId, string? term, CancellationToken ct) =>
        MutateAsync(userId, state => (state with { LastSearch = term }, true), ct);

    private async Task<bool> MutateAsync(
        string userId,
        Func<UserState, (UserState State, bool Changed)> mutate,
        CancellationToken ct
    )
    {
        await _gate.WaitAsync(ct);
        try
        {
            Dictionary<string, UserState> all = await ReadAsync(ct);
            UserState current = all.TryGetValue(userId, out UserState? found) ? found : UserState.Empty;

            (UserState next, bool changed) = mutate(current);
            if (!changed)
            {
                return false;
            }

            all[userId] = next;
            await WriteAsync(all, ct);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // A file that cannot be read is treated as empty rather than thrown. Losing
    // favourites is bad; refusing to render any screen because one JSON file is corrupt
    // is worse, and the catalogue cache next to it already makes the same choice.
    private async Task<Dictionary<string, UserState>> ReadAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(Path))
            {
                return [];
            }

            string text = await File.ReadAllTextAsync(Path, ct);

            return JsonSerializer.Deserialize<Dictionary<string, UserState>>(text, Json) ?? [];
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Written whole, to a temp file in the same directory, then moved over the target.
    // Same directory so the move is a rename rather than a cross-volume copy, which is
    // what makes it atomic: a reader sees the old file or the new one, never a truncated
    // one holding every user's list at once.
    private async Task WriteAsync(Dictionary<string, UserState> all, CancellationToken ct)
    {
        Directory.CreateDirectory(dataFolderPath);

        string temporary = Path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(all, Json), ct);
        File.Move(temporary, Path, overwrite: true);
    }
}
