// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.State;

public sealed class UserStateStoreTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"nm-radio-state-{Guid.NewGuid():N}");

    private UserStateStore Store() => new(_folder);

    private static RadioStation Station(string id) =>
        new() { Id = id, Name = $"Station {id}", StreamUrl = $"https://example.com/{id}" };

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // The first read happens before anything has ever been written, on every fresh
    // install. Empty, not an exception, and not a created file either.
    [Fact]
    public async Task GetAsync_ReturnsEmptyStateWhenNothingWasEverWritten()
    {
        UserState state = await Store().GetAsync("user-1", CancellationToken.None);

        state.Favourites.Should().BeEmpty();
    }

    [Fact]
    public async Task AddFavouriteAsync_StoresTheWholeRecordAndNotJustAnId()
    {
        UserStateStore store = Store();
        await store.AddFavouriteAsync("user-1", Station("a"), CancellationToken.None);

        RadioStation stored = (await store.GetAsync("user-1", CancellationToken.None))
            .Favourites.Should().ContainSingle().Subject;

        stored.Id.Should().Be("a");
        stored.Name.Should().Be("Station a");
        stored.StreamUrl.Should().Be("https://example.com/a");
    }

    // The whole point of per-user state: one viewer's list must not be readable from,
    // or damageable by, another's write.
    [Fact]
    public async Task Favourites_AreSeparatePerUser()
    {
        UserStateStore store = Store();
        await store.AddFavouriteAsync("user-1", Station("a"), CancellationToken.None);
        await store.AddFavouriteAsync("user-2", Station("b"), CancellationToken.None);

        (await store.GetAsync("user-1", CancellationToken.None))
            .Favourites.Should().ContainSingle().Which.Id.Should().Be("a");
        (await store.GetAsync("user-2", CancellationToken.None))
            .Favourites.Should().ContainSingle().Which.Id.Should().Be("b");
    }

    [Fact]
    public async Task AddFavouriteAsync_IsIdempotentAndReportsIt()
    {
        UserStateStore store = Store();

        (await store.AddFavouriteAsync("user-1", Station("a"), CancellationToken.None))
            .Should().BeTrue();
        (await store.AddFavouriteAsync("user-1", Station("a"), CancellationToken.None))
            .Should().BeFalse();

        (await store.GetAsync("user-1", CancellationToken.None)).Favourites.Should().HaveCount(1);
    }

    // Removal needs no resolution, so an unknown id is a no-op - but it must say it
    // removed nothing rather than claim success, or the controller cannot tell a stale
    // button from a real one.
    [Fact]
    public async Task RemoveFavouriteAsync_ReportsWhetherAnythingWasThere()
    {
        UserStateStore store = Store();
        await store.AddFavouriteAsync("user-1", Station("a"), CancellationToken.None);

        (await store.RemoveFavouriteAsync("user-1", "a", CancellationToken.None)).Should().BeTrue();
        (await store.RemoveFavouriteAsync("user-1", "a", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveFavouriteAsync_LeavesTheOtherFavouritesAlone()
    {
        UserStateStore store = Store();
        await store.AddFavouriteAsync("user-1", Station("a"), CancellationToken.None);
        await store.AddFavouriteAsync("user-1", Station("b"), CancellationToken.None);

        await store.RemoveFavouriteAsync("user-1", "a", CancellationToken.None);

        (await store.GetAsync("user-1", CancellationToken.None))
            .Favourites.Should().ContainSingle().Which.Id.Should().Be("b");
    }


    // Two viewers clicking at the same moment is the ordinary case on a family server,
    // and the file holds every user's list - so a lost write is not one favourite gone,
    // it is somebody else's whole list gone.
    [Fact]
    public async Task ConcurrentWritesFromDifferentUsersAllSurvive()
    {
        UserStateStore store = Store();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            store.AddFavouriteAsync($"user-{index}", Station($"s{index}"), CancellationToken.None)));

        foreach (int index in Enumerable.Range(0, 20))
        {
            (await store.GetAsync($"user-{index}", CancellationToken.None))
                .Favourites.Should().ContainSingle().Which.Id.Should().Be($"s{index}");
        }
    }

    [Fact]
    public async Task ConcurrentWritesForOneUserAllSurvive()
    {
        UserStateStore store = Store();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            store.AddFavouriteAsync("user-1", Station($"s{index}"), CancellationToken.None)));

        (await store.GetAsync("user-1", CancellationToken.None)).Favourites.Should().HaveCount(20);
    }

    // A corrupt file must not take the plugin down. The catalogue cache beside it makes
    // the same choice, and for the same reason: a blank favourites row is recoverable,
    // a screen that will not render is not.
    [Fact]
    public async Task GetAsync_TreatsAnUnreadableFileAsEmpty()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(
            Path.Combine(_folder, UserStateStore.FileName), "{ not json");

        (await Store().GetAsync("user-1", CancellationToken.None)).Favourites.Should().BeEmpty();
    }

    // The temp file is an implementation detail, but one left behind would be swept into
    // a backup or confuse the next reader. The move must consume it.
    [Fact]
    public async Task WritingLeavesNoTemporaryFileBehind()
    {
        await Store().AddFavouriteAsync("user-1", Station("a"), CancellationToken.None);

        Directory.EnumerateFiles(_folder).Should()
            .ContainSingle().Which.Should().EndWith(UserStateStore.FileName);
    }
}
