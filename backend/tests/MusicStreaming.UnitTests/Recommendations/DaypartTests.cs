// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class DaypartTests
{
    [Theory]
    [InlineData(5, Daypart.Morning)]
    [InlineData(10, Daypart.Morning)]
    [InlineData(11, Daypart.Day)]
    [InlineData(16, Daypart.Day)]
    [InlineData(17, Daypart.Evening)]
    [InlineData(22, Daypart.Evening)]
    [InlineData(23, Daypart.Night)]
    [InlineData(3, Daypart.Night)]
    public void The_clock_maps_onto_parts_of_the_day(int hour, Daypart expected) =>
        Assert.Equal(expected, Dayparts.Of(hour));

    [Fact]
    public void Midnight_belongs_to_the_night_it_continues()
    {
        Assert.Equal(Daypart.Night, Dayparts.Of(0));
        Assert.Equal(Daypart.Night, Dayparts.Of(24));
        Assert.Equal(Daypart.Night, Dayparts.Of(-1));
    }

    [Fact]
    public void An_unknown_time_zone_falls_back_to_utc()
    {
        Assert.Equal(TimeZoneInfo.Utc, Dayparts.ZoneOrUtc("Mars/Olympus"));
        Assert.Equal(TimeZoneInfo.Utc, Dayparts.ZoneOrUtc(null));
        Assert.Equal(TimeZoneInfo.Utc, Dayparts.ZoneOrUtc("  "));
    }

    [Fact]
    public void The_hour_is_read_where_the_listener_is()
    {
        var zone = Dayparts.ZoneOrUtc("Asia/Yekaterinburg");
        var moment = new DateTimeOffset(2026, 8, 26, 20, 0, 0, TimeSpan.Zero);

        // 20:00 UTC — это уже час ночи следующего дня в Екатеринбурге.
        Assert.Equal(Daypart.Evening, Dayparts.Of(moment, TimeZoneInfo.Utc));
        Assert.Equal(Daypart.Night, Dayparts.Of(moment, zone));
    }

    [Fact]
    public void A_candidate_from_the_genre_of_the_hour_fits_better()
    {
        var evening = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();

        var taste = new DaypartTaste(
            Daypart.Evening, 0.5, null, [new TasteEntry(evening, "Ambient", 0.8)]);

        Assert.True(DaypartFit.For(Candidate(evening), taste) > DaypartFit.For(Candidate(other), taste));
    }

    [Fact]
    public void Energy_pulls_towards_what_the_hour_usually_sounds_like()
    {
        var taste = new DaypartTaste(Daypart.Night, 0.4, 0.2, []);

        var calm = Candidate(null, energy: 0.22);
        var loud = Candidate(null, energy: 0.9);

        Assert.True(DaypartFit.For(calm, taste) > 0.8);
        Assert.True(DaypartFit.For(loud, taste) < 0.2);
    }

    [Fact]
    public void Without_anything_to_go_on_the_fit_stays_neutral()
    {
        var taste = new DaypartTaste(Daypart.Day, 0.3, null, []);

        Assert.Equal(0.5, DaypartFit.For(Candidate(null), taste));
    }

    private static RecommendationCandidate Candidate(Guid? genreId, double? energy = null) => new()
    {
        TrackId = Guid.CreateVersion7(),
        ArtistId = Guid.CreateVersion7(),
        GenreId = genreId,
        AudioProfile = energy is { } value ? new TrackAudioProfile(120, value, 0.5) : null,
    };
}
