// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text;
using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class ImageSnifferTests
{
    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0 }, "image/gif")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61, 0 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50, 0 }, "image/webp")]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x00, 1, 0 }, "image/x-icon")]
    public void RecognisesTheRasterFormatsByTheirOwnMagic(byte[] bytes, string expected)
    {
        ImageSniffer.MediaType(bytes).Should().Be(expected);
    }

    [Theory]
    [InlineData("<svg viewBox='0 0 1 1'></svg>")]
    [InlineData("﻿  <?xml version='1.0'?><svg></svg>")]
    [InlineData("<!-- logo --><svg></svg>")]
    public void RecognisesAnSvgDocument(string text)
    {
        ImageSniffer.MediaType(Encoding.UTF8.GetBytes(text)).Should().Be("image/svg+xml");
    }

    // The ordinary failure: a logo url that rotted into the station's HTML home page,
    // which may well contain an inline svg icon somewhere in it.
    [Theory]
    [InlineData("<!DOCTYPE html><html><body><svg></svg></body></html>")]
    [InlineData("<html><svg></svg></html>")]
    [InlineData("Not Found")]
    [InlineData("")]
    public void RefusesAnythingThatIsNotAnImage(string text)
    {
        ImageSniffer.MediaType(Encoding.UTF8.GetBytes(text)).Should().BeNull();
    }
}
