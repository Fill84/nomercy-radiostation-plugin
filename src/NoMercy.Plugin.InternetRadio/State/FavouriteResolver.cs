// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

// Turns a station id into the record a favourite stores.
//
// A button's action carries nothing but its path, so a favourite toggle arrives with an
// id and nothing else. The catalogue answers for anything the sweep found or the user's
// own stations.json supplied; radio-browser answers for the rest, which is the whole
// reason GetByUuidsAsync survived the removal of the seed list.
public sealed class FavouriteResolver(StationCatalog catalog, RadioBrowserClient client)
{
    public async Task<RadioStation?> ResolveAsync(string stationId, CancellationToken ct)
    {
        // The catalogue already merges the user's override, so one lookup covers both a
        // swept station and a hand-written one - and costs no request either way.
        if (catalog.ById(stationId) is { } known)
        {
            return known;
        }

        // A user-supplied station has a slug for an id, never a uuid. Asking
        // radio-browser about a slug is a request that cannot succeed, so it is not
        // made: the answer is the same and the round trip is not.
        if (!Guid.TryParse(stationId, out _))
        {
            return null;
        }

        try
        {
            IReadOnlyList<RadioBrowserStation> wire = await client.GetByUuidsAsync([stationId], ct);

            // Admitted, not just mapped. A station that cannot play is not worth
            // storing, and the same gates judge it here as judged the sweep - otherwise
            // a stream refused on the browse page could be favourited from search.
            return StationGates.Admitted(wire).FirstOrDefault();
        }
        // A third party being unreachable is a favourite that did not get added, which
        // the controller reports. It is not an exception escaping into a view.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }
}
