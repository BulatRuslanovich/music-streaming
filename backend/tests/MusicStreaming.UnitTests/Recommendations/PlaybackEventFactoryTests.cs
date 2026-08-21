// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class PlaybackEventFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Guid Track = Guid.CreateVersion7();

    private static PlaybackEventRequest Request(
        string? type = "trackCompleted",
        Guid? trackId = null,
        Guid? entityId = null,
        DateTimeOffset? occurredAt = null,
        int? position = 100,
        int? listened = 100,
        int? duration = 200,
        string? source = "home",
        string? platform = "web") =>
        new(type, trackId ?? Track, entityId, occurredAt, position, listened, duration,
            Guid.CreateVersion7(), source, null, platform);

    [Fact]
    public void A_well_formed_report_is_accepted()
    {
        var created = PlaybackEventFactory.TryCreate(Request(), User, Now);

        Assert.NotNull(created);
        Assert.Equal(PlaybackEventType.TrackCompleted, created.Type);
        Assert.Equal(PlaybackSource.Home, created.Source);
        Assert.Equal(User, created.UserId);
        Assert.Equal(Track, created.TrackId);
    }

    [Theory]
    [InlineData("trackCompleted")]
    [InlineData("TrackCompleted")]
    [InlineData("TRACKCOMPLETED")]
    public void Type_names_are_matched_case_insensitively(string type) =>
        Assert.Equal(PlaybackEventType.TrackCompleted, PlaybackEventFactory.ParseType(type));

    [Theory]
    [InlineData("somethingNew")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("42")]
    public void An_unknown_type_is_rejected_quietly(string? type) =>
        Assert.Null(PlaybackEventFactory.TryCreate(Request(type: type), User, Now));

    [Fact]
    public void An_unknown_source_falls_back_to_unknown() =>
        Assert.Equal(PlaybackSource.Unknown, PlaybackEventFactory.ParseSource("somewhere-else"));

    [Fact]
    public void A_symbolic_source_identifier_is_ignored_instead_of_rejecting_the_event() =>
        Assert.Null(PlaybackEventFactory.ParseSourceId("dailyMix"));

    [Fact]
    public void A_guid_source_identifier_is_preserved()
    {
        var sourceId = Guid.CreateVersion7();

        Assert.Equal(sourceId, PlaybackEventFactory.ParseSourceId(sourceId.ToString()));
    }

    [Fact]
    public void A_track_event_without_a_track_is_rejected()
    {
        var request = new PlaybackEventRequest(
            "trackCompleted", null, null, Now, 10, 10, 200, Guid.CreateVersion7(), "home", null, "web");

        Assert.Null(PlaybackEventFactory.TryCreate(request, User, Now));
    }

    [Fact]
    public void An_entity_event_without_an_entity_is_rejected()
    {
        var request = new PlaybackEventRequest(
            "artistOpened", null, null, Now, 0, 0, 0, Guid.CreateVersion7(), "search", null, "web");

        Assert.Null(PlaybackEventFactory.TryCreate(request, User, Now));
    }

    [Fact]
    public void An_entity_event_does_not_carry_a_track()
    {
        var created = PlaybackEventFactory.TryCreate(
            Request(type: "artistOpened", entityId: Guid.CreateVersion7()), User, Now);

        Assert.NotNull(created);
        Assert.Null(created.TrackId);
    }

    [Fact]
    public void A_timestamp_from_the_future_is_pulled_back_to_now()
    {
        var created = PlaybackEventFactory.TryCreate(
            Request(occurredAt: Now.AddDays(30)), User, Now);

        Assert.Equal(Now, created!.OccurredAt);
    }

    [Fact]
    public void An_ancient_timestamp_is_pulled_forward()
    {
        var created = PlaybackEventFactory.TryCreate(
            Request(occurredAt: Now.AddYears(-2)), User, Now);

        Assert.Equal(Now.AddDays(-PlaybackEventFactory.MaxBacklogDays), created!.OccurredAt);
    }

    [Fact]
    public void A_believable_timestamp_is_kept()
    {
        var reported = Now.AddHours(-3);

        Assert.Equal(reported, PlaybackEventFactory.TryCreate(
            Request(occurredAt: reported), User, Now)!.OccurredAt);
    }

    [Fact]
    public void A_missing_timestamp_defaults_to_now() =>
        Assert.Equal(Now, PlaybackEventFactory.TryCreate(Request(occurredAt: null), User, Now)!.OccurredAt);

    [Theory]
    [InlineData(-50, 0)]
    [InlineData(0, 0)]
    [InlineData(500, 500)]
    [InlineData(999_999, PlaybackEventFactory.MaxSeconds)]
    public void Nonsensical_counters_are_clamped(int reported, int expected)
    {
        var created = PlaybackEventFactory.TryCreate(
            Request(listened: reported, position: reported, duration: reported), User, Now);

        Assert.Equal(expected, created!.ListenedSeconds);
        Assert.Equal(expected, created.PositionSeconds);
        Assert.Equal(expected, created.DurationSeconds);
    }

    [Fact]
    public void A_missing_counter_becomes_zero()
    {
        var created = PlaybackEventFactory.TryCreate(
            Request(listened: null, position: null, duration: null), User, Now);

        Assert.Equal(0, created!.ListenedSeconds);
    }

    [Theory]
    [InlineData(null, "web")]
    [InlineData("", "web")]
    [InlineData("   ", "web")]
    [InlineData(" pwa ", "pwa")]
    public void Platform_is_normalised(string? reported, string expected) =>
        Assert.Equal(expected, PlaybackEventFactory.TryCreate(
            Request(platform: reported), User, Now)!.Platform);

    [Fact]
    public void An_overlong_platform_is_truncated_to_fit_its_column()
    {
        var created = PlaybackEventFactory.TryCreate(
            Request(platform: new string('x', 500)), User, Now);

        Assert.Equal(32, created!.Platform.Length);
    }

    [Fact]
    public void A_missing_session_is_tolerated()
    {
        var request = new PlaybackEventRequest(
            "trackCompleted", Track, null, Now, 10, 10, 200, null, "home", null, "web");

        var created = PlaybackEventFactory.TryCreate(request, User, Now);

        Assert.NotNull(created);
        Assert.Equal(Guid.Empty, created.SessionId);
    }

    [Fact]
    public void Each_event_gets_its_own_identifier()
    {
        var first = PlaybackEventFactory.TryCreate(Request(), User, Now)!;
        var second = PlaybackEventFactory.TryCreate(Request(), User, Now)!;

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }
}
