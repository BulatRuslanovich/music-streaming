// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using App.Recommendations.Moods;

namespace UnitTests.Recommendations;

public class MoodShelfTests
{
    [Fact]
    public void Covers_are_distinct_albums_in_track_order_and_skip_tracks_without_one()
    {
        var tracks = Enumerable.Range(0, 6).Select(_ => Guid.CreateVersion7()).ToArray();
        var albumA = Guid.CreateVersion7();
        var albumB = Guid.CreateVersion7();
        var albumC = Guid.CreateVersion7();
        var albumD = Guid.CreateVersion7();

        // tracks[1] без обложки; tracks[2] — тот же альбом, что tracks[0].
        var albums = new Dictionary<Guid, Guid>
        {
            [tracks[0]] = albumA,
            [tracks[2]] = albumA,
            [tracks[3]] = albumB,
            [tracks[4]] = albumC,
            [tracks[5]] = albumD,
        };

        Assert.Equal([albumA, albumB, albumC], MoodShelf.Pick(tracks, albums));
    }
}
