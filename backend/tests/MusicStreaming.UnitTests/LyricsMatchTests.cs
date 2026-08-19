// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class LyricsMatchTests
{
    private static readonly LyricsQuery Query = new("Creep", "Radiohead", "Pablo Honey", 238);

    private static LyricsCandidate Candidate(
        string title = "Creep",
        string artist = "Radiohead",
        double duration = 238,
        bool instrumental = false,
        string? synced = null,
        string? plain = "When you were here before") =>
        new(title, artist, duration, instrumental, plain, synced);

    private static LyricsCandidate? Select(params LyricsCandidate[] candidates) =>
        LyricsMatch.SelectBest(candidates, Query, toleranceSeconds: 2);

    [Fact]
    public void Nothing_to_choose_from_is_no_match() =>
        Assert.Null(Select());

    [Fact]
    public void Casing_and_stray_spacing_still_match()
    {
        var candidate = Candidate(title: "  creep ", artist: "RADIOHEAD");

        Assert.Same(candidate, Select(candidate));
    }

    [Theory]
    [InlineData("Creep;Creep", "Radiohead")]
    [InlineData("Creep (Acoustic)", "Radiohead")]
    [InlineData("Creep", "Radiohead Tribute Band")]
    public void A_different_title_or_artist_is_a_different_track(string title, string artist) =>
        Assert.Null(Select(Candidate(title: title, artist: artist)));

    [Theory]
    [InlineData(240)]
    [InlineData(236)]
    public void Durations_within_the_tolerance_are_accepted(double duration) =>
        Assert.NotNull(Select(Candidate(duration: duration)));

    [Theory]
    [InlineData(258.7)]
    [InlineData(120)]
    public void A_live_version_or_a_remix_is_rejected_by_duration(double duration) =>
        Assert.Null(Select(Candidate(duration: duration)));

    [Fact]
    public void Synced_lyrics_win_over_plain_ones()
    {
        var plain = Candidate(duration: 238);
        var timed = Candidate(duration: 239, synced: "[00:12.50]When you were here before");

        Assert.Same(timed, Select(plain, timed));
    }

    [Fact]
    public void Among_equals_the_closest_duration_wins()
    {
        var far = Candidate(duration: 240, synced: "[00:12.50]line");
        var near = Candidate(duration: 238, synced: "[00:12.50]line");

        Assert.Same(near, Select(far, near));
    }

    [Fact]
    public void A_candidate_carrying_no_text_at_all_is_not_a_match() =>
        Assert.Null(Select(Candidate(plain: null, synced: "   ")));

    [Fact]
    public void An_instrumental_candidate_matches_so_the_caller_can_stop_looking()
    {
        var candidate = Candidate(instrumental: true, plain: null);

        Assert.Same(candidate, Select(candidate));
    }
}
