// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using App.Recommendations.Embeddings;
using Domain.Entities;

namespace UnitTests;

public class DuplicateDetectorTests
{
    private const int Dimension = 16;

    [Fact]
    public void The_same_recording_twice_is_one_group()
    {
        var original = Track(Direction(1), "a|song");
        var copy = Track(Nudge(Direction(1), 0.02f), "a|song (flac)");
        var other = Track(Direction(2), "b|other");

        var groups = Find([original, copy, other], Durations((original, 200), (copy, 201), (other, 200)));

        var group = Assert.Single(groups);
        Assert.Equal(new[] { original.Id, copy.Id }.Order(), group.TrackIds.Order());
        Assert.True(group.Similarity >= DuplicateDetector.SoundAlike);
    }

    [Fact]
    public void Sound_alike_tracks_of_different_length_are_not_duplicates()
    {
        var album = Track(Direction(1), "a|song");
        var extended = Track(Nudge(Direction(1), 0.01f), "a|song");

        Assert.Empty(Find([album, extended], Durations((album, 200), (extended, 320))));
    }

    [Fact]
    public void A_matching_title_lets_a_remaster_through_at_lower_similarity()
    {
        var original = Track(Direction(1), "a|song");
        var remaster = Track(Nudge(Direction(1), 0.5f), "a|song");
        var stranger = Track(Nudge(Direction(1), 0.5f, axis: Dimension - 2), "c|unrelated");

        var durations = Durations((original, 180), (remaster, 181), (stranger, 182));
        var similarity = Similarity(original, remaster);
        Assert.InRange(similarity, DuplicateDetector.SoundAlikeSameSong, DuplicateDetector.SoundAlike);

        var group = Assert.Single(Find([original, remaster, stranger], durations));
        Assert.Equal(new[] { original.Id, remaster.Id }.Order(), group.TrackIds.Order());
    }

    [Fact]
    public void A_dismissed_pair_is_not_offered_again()
    {
        var a = Track(Direction(1), "a|song");
        var b = Track(Nudge(Direction(1), 0.01f), "a|song");

        var dismissed = new HashSet<(Guid, Guid)> { DuplicateDismissal.Ordered(b.Id, a.Id) };

        Assert.Empty(DuplicateDetector.Find(Snapshot([a, b]), Durations((a, 100), (b, 100)), dismissed));
    }

    [Fact]
    public void Different_songs_of_one_artist_are_not_duplicates_however_alike_they_sound()
    {
        var artist = Guid.NewGuid();
        var war = Track(Direction(4), "кино|война", artist);
        var mama = Track(Nudge(Direction(4), 0.01f), "кино|мама, мы все сошли с ума", artist);

        Assert.True(Similarity(war, mama) >= DuplicateDetector.SoundAlike);
        Assert.Empty(Find([war, mama], Durations((war, 245), (mama, 247))));
    }

    [Fact]
    public void A_remaster_of_the_same_song_by_the_same_artist_is_still_a_duplicate()
    {
        var artist = Guid.NewGuid();
        var original = Track(Direction(5), "a|song", artist);
        var remaster = Track(Nudge(Direction(5), 0.02f), "a|song (remastered 2011)", artist);

        var group = Assert.Single(Find([original, remaster], Durations((original, 200), (remaster, 201))));
        Assert.Equal(new[] { original.Id, remaster.Id }.Order(), group.TrackIds.Order());
    }

    [Fact]
    public void Three_copies_chain_into_one_group()
    {
        var tracks = Enumerable.Range(0, 3).Select(i => Track(Nudge(Direction(3), 0.01f * i), "x|y")).ToArray();

        var group = Assert.Single(Find(tracks, Durations([.. tracks.Select(track => (track, 240))])));
        Assert.Equal(3, group.TrackIds.Count);
    }

    private sealed record Sample(Guid Id, float[] Vector, string SongKey, Guid ArtistId);

    private static Sample Track(float[] vector, string songKey, Guid? artistId = null) =>
        new(Guid.NewGuid(), vector, songKey, artistId ?? Guid.NewGuid());

    private static float[] Direction(int axis)
    {
        var vector = new float[Dimension];
        vector[axis] = 1;
        return vector;
    }

    private static float[] Nudge(float[] vector, float amount, int axis = Dimension - 1)
    {
        var copy = vector.ToArray();
        copy[axis] += amount;
        return Vectors.Unit(copy);
    }

    private static double Similarity(Sample a, Sample b) => Vectors.Dot(Vectors.Unit(a.Vector), Vectors.Unit(b.Vector));

    private static Dictionary<Guid, int> Durations(params (Sample Track, int Seconds)[] pairs) =>
        pairs.ToDictionary(pair => pair.Track.Id, pair => pair.Seconds);

    private static IReadOnlyList<DuplicateGroup> Find(Sample[] tracks, Dictionary<Guid, int> durations) =>
        DuplicateDetector.Find(Snapshot(tracks), durations, new HashSet<(Guid, Guid)>());

    private static EmbeddingSnapshot Snapshot(Sample[] tracks) => new(
        [.. tracks.SelectMany(track => Vectors.Unit(track.Vector))],
        [.. tracks.Select(track => new TrackVectorMeta(track.Id, track.ArtistId, track.Id.ToString("N"), track.SongKey, DateTimeOffset.UnixEpoch, 0))],
        Dimension);
}
