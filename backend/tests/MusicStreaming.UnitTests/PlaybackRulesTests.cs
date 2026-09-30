// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;
using Xunit;

namespace MusicStreaming.UnitTests;

public class PlayAttemptTests
{
    [Theory]
    [InlineData(PlaybackEventType.TrackStarted)]
    [InlineData(PlaybackEventType.TrackPlayed)]
    [InlineData(PlaybackEventType.TrackLiked)]
    public void Only_the_closing_events_describe_a_finished_play(PlaybackEventType type) =>
        Assert.Null(PlayAttempt.From(Event(type)));

    [Theory]
    [InlineData(PlaybackEventType.TrackCompleted)]
    [InlineData(PlaybackEventType.TrackSkipped)]
    public void A_closing_event_becomes_a_play(PlaybackEventType type) =>
        Assert.NotNull(PlayAttempt.From(Event(type)));

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void Less_than_thirty_listened_seconds_is_not_a_play(int listened) =>
        Assert.Null(PlayAttempt.From(Event(PlaybackEventType.TrackSkipped, listened: listened)));

    [Fact]
    public void Thirty_listened_seconds_is_a_play() =>
        Assert.NotNull(PlayAttempt.From(Event(PlaybackEventType.TrackSkipped, listened: 30)));

    [Fact]
    public void An_event_without_a_track_describes_nothing()
    {
        var opened = new PlaybackEvent { Type = PlaybackEventType.TrackCompleted, TrackId = null };

        Assert.Null(PlayAttempt.From(opened));
    }

    [Fact]
    public void The_start_is_the_event_time_minus_the_position()
    {
        var at = new DateTimeOffset(2026, 3, 1, 12, 30, 0, TimeSpan.Zero);
        var attempt = PlayAttempt.From(Event(PlaybackEventType.TrackCompleted, at: at, position: 180));

        Assert.Equal(at.AddSeconds(-180), attempt!.Value.StartedAt);
    }

    [Fact]
    public void The_hour_bucket_is_the_hour_the_play_began()
    {
        var ended = new DateTimeOffset(2026, 3, 1, 21, 1, 0, TimeSpan.Zero);
        var attempt = PlayAttempt.From(Event(PlaybackEventType.TrackCompleted, at: ended, position: 180))!.Value;

        Assert.Equal(new DateTimeOffset(2026, 3, 1, 20, 0, 0, TimeSpan.Zero), attempt.Hour);
    }

    [Fact]
    public void Absurd_values_from_a_broken_client_are_clamped()
    {
        var attempt = PlayAttempt.From(Event(
            PlaybackEventType.TrackCompleted, position: -5, listened: int.MaxValue))!.Value;

        Assert.Equal(24 * 60 * 60, attempt.ListenedSeconds);
        Assert.Equal(Now, attempt.StartedAt);
    }

    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static PlaybackEvent Event(
        PlaybackEventType type,
        DateTimeOffset? at = null,
        int position = 0,
        int listened = 100) => new()
        {
            Type = type,
            TrackId = Guid.CreateVersion7(),
            OccurredAt = at ?? Now,
            PositionSeconds = position,
            ListenedSeconds = listened,
            DurationSeconds = 200,
        };
}

public class AudioQualityTests
{
    [Theory]
    [InlineData(AudioQuality.Low, 64)]
    [InlineData(AudioQuality.Normal, 128)]
    public void Every_transcoded_step_has_a_bitrate(AudioQuality quality, int expected) =>
        Assert.Equal(expected, AudioBitrates.For(quality));

    [Fact]
    public void The_original_is_never_transcoded() =>
        Assert.Null(AudioBitrates.For(AudioQuality.Original));

    [Fact]
    public void Data_saver_overrides_the_chosen_step_without_replacing_it()
    {
        var settings = new UserSettings { Quality = AudioQuality.Original, DataSaver = true };

        Assert.Equal(AudioQuality.Low, settings.EffectiveQuality);

        settings.DataSaver = false;
        Assert.Equal(AudioQuality.Original, settings.EffectiveQuality);
    }
}

public class PasswordPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sh2")]
    public void Too_short_is_refused(string? password) =>
        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate(password));

    [Fact]
    public void Longer_than_bcrypt_can_read_is_refused()
    {
        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate(new string('a', 73)));
    }

    [Fact]
    public void A_valid_password_comes_back_unchanged() =>
        Assert.Equal("good-password", PasswordPolicy.Validate("good-password"));
}
