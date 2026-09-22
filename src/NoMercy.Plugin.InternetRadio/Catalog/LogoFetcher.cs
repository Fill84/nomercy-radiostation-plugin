// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio.Catalog;

/// <summary>What fetching one logo came to. Final unless it is worth trying again.</summary>
public sealed record LogoResult(string? DataUri, bool IsFinal);

/// <summary>
/// One station logo, fetched by the server and handed to the browser inline.
///
/// Inline because nothing else reaches the page. The dashboard's image policy refuses a
/// station's own host; this plugin's old relay route is refused for carrying a token in
/// its url (ABI 11 and up); and an image the host mints cannot be drawn by the web client,
/// which sends no credential for it. A data: URI needs none of them.
///
/// Nothing is resized - that would need an image library in the plugin. The cap is what
/// bounds the page instead: a grid of eighty logos at the cap is still a page, where eighty
/// full-size photographs would not be.
/// </summary>
public sealed class LogoFetcher(HttpClient http)
{
    public const int MaxBytes = 16 * 1024;

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    public async Task<LogoResult> FetchAsync(string url, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            using HttpResponseMessage response =
                await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);

            if ((int)response.StatusCode >= 500)
            {
                return new LogoResult(null, IsFinal: false);
            }

            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaxBytes)
            {
                return new LogoResult(null, IsFinal: true);
            }

            // One byte past the cap is enough to know it is over, without reading the rest.
            byte[] buffer = new byte[MaxBytes + 1];
            int total = 0;

            await using Stream body = await response.Content.ReadAsStreamAsync(budget.Token);

            int read;
            while (total < buffer.Length
                   && (read = await body.ReadAsync(buffer.AsMemory(total), budget.Token)) > 0)
            {
                total += read;
            }

            if (total is 0 or > MaxBytes)
            {
                return new LogoResult(null, IsFinal: true);
            }

            ReadOnlySpan<byte> bytes = buffer.AsSpan(0, total);

            return ImageSniffer.MediaType(bytes) is { } type
                ? new LogoResult($"data:{type};base64,{Convert.ToBase64String(bytes)}", IsFinal: true)
                : new LogoResult(null, IsFinal: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            // A timeout is the budget firing, not the caller: the logo may simply be slow today.
            return new LogoResult(null, IsFinal: false);
        }
    }
}
