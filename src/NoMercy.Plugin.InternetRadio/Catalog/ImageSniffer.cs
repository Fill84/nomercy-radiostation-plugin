// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text;

namespace NoMercy.Plugin.InternetRadio.Catalog;

/// <summary>
/// What kind of image a response is, judged by its bytes.
///
/// Not by its Content-Type. A logo url on a volunteer-edited directory rots in ordinary
/// ways - into the station's home page, a 200 "not found" page, a parked domain - and
/// every one of those still says it is whatever the server felt like saying. Handing any
/// of them to an img as a data: URI draws a broken icon that reads as this plugin being
/// broken.
/// </summary>
public static class ImageSniffer
{
    public static string? MediaType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0x01, 0x00]))
        {
            return "image/x-icon";
        }

        return IsSvgDocument(bytes) ? "image/svg+xml" : null;
    }

    // An svg document, not a document that happens to contain one. Safe to hand over:
    // an img element never runs an svg's scripts.
    private static bool IsSvgDocument(ReadOnlySpan<byte> bytes)
    {
        string head = Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 1024)])
            .TrimStart('﻿', ' ', '\t', '\r', '\n');

        if (head.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return (head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("<?xml", StringComparison.Ordinal)
                || head.StartsWith("<!--", StringComparison.Ordinal))
            && head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }
}
