// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public sealed class StationCardsNowPlayingTests
{
    private static RadioStation Station() => new()
    {
        Id = "960cf833-0601",
        Name = "SomaFM Groove Salad",
        StreamUrl = "https://ice.somafm.test/groovesalad"
    };

    [Fact]
    public void APlayIntentSaysThatThisItemAnnouncesItsTrack()
    {
        // Without this the client has to guess, which it did - it polled every item
        // whose id began with "plugin:", on an interval it chose itself.
        object? declared = StationCards.Play(Station()).Payload[StationCards.NowPlayingKey];

        Dictionary<string, object> block = Assert.IsType<Dictionary<string, object>>(declared);

        block["method"].Should().Be(InternetRadioController.NowPlayingMethod);
        block["intervalSeconds"].Should().Be(15);
        block["firstIntervalSeconds"].Should().Be(2);
    }

    [Fact]
    public void QueueingAStationSaysTheSameThing()
    {
        // Enqueue plays the same broadcast. An item that announces on one route and
        // not the other is the same station going quiet for no reason a listener
        // could name.
        StationCards.Enqueue(Station()).Payload
            .Should().ContainKey(StationCards.NowPlayingKey);
    }
}
