// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;
using System.Text;

namespace NoMercy.Plugin.InternetRadio;

// The one page this plugin serves as HTML rather than as components.
//
// It exists because the dashboard's player cannot play plugin media at all: it derives a
// track id from the stream url and then uses that id as a CSS selector, which a url can
// never be, so the component throws before a single byte is requested. That is not
// something a payload can work around - even an empty stream url leaves
// `plugin:{pluginId}:` and the colons alone are enough.
//
// So the station page embeds this in a webview and you get sound. It is deliberately the
// browser's own <audio> element and nothing else: no queue, no cast, no now-playing. When
// the dashboard's player is fixed, this goes and PlayMedia takes over again.
//
// Everything interpolated here is HTML-encoded. A station name comes from a community-
// edited database and reaches this page unvetted, so treating it as markup would be a
// stored cross-site scripting hole with a very short path: anyone able to edit a station's
// name on radio-browser could run script in the viewer's dashboard.
public static class PlayerPage
{
    public static string Html(RadioStation station)
    {
        string name = WebUtility.HtmlEncode(station.Name);
        string stream = WebUtility.HtmlEncode(
            MediaProxy.TokenisedStream(station.Id) ?? station.StreamUrl);
        string? cover = StationCards.CoverUrl(station) is null
            ? null
            : WebUtility.HtmlEncode(MediaProxy.Cover(station.Id));
        string subtitle = WebUtility.HtmlEncode(StationCards.Subtitle(station) ?? "Live radio");

        StringBuilder page = new();

        page.Append(
            """
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <style>
              :root { color-scheme: dark }
              body {
                margin: 0; padding: 1rem; display: flex; gap: 1rem; align-items: center;
                font: 500 0.95rem/1.4 system-ui, sans-serif;
                color: #e7e5ea; background: transparent;
              }
              img { width: 5rem; height: 5rem; object-fit: cover; border-radius: 0.5rem; flex: none }
              .meta { display: flex; flex-direction: column; gap: 0.5rem; min-width: 0; flex: 1 }
              .name { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap }
              .sub { font-size: 0.8rem; opacity: 0.65 }
              audio { width: 100% }
            </style></head><body>
            """);

        if (cover is not null)
        {
            page.Append($"""<img src="{cover}" alt="">""");
        }

        // `autoplay` is a request, not a promise: a browser may refuse it until the viewer
        // has interacted, which is exactly why `controls` is not optional here. Refused
        // autoplay then costs one click on a visible play button rather than silence with
        // nothing to press.
        page.Append(
            $"""
            <div class="meta">
              <div class="name">{name}</div>
              <div class="sub">{subtitle}</div>
              <audio controls autoplay preload="none" src="{stream}"></audio>
            </div>
            </body></html>
            """);

        return page.ToString();
    }
}
