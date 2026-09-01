// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Правила, по которым одно событие меняет одну привязанность: счётчики, накопленный вес с
/// затуханием и итоговый скор. Отдельно от свёртки пачки — здесь только арифметика над строкой.
/// </summary>
public class AffinityUpdater(IApplicationDbContext db, IOptions<RecommendationOptions> options)
{
    private RecommendationOptions Options => options.Value;

    public void ApplyToTrack(
        Dictionary<Guid, UserTrackAffinity> tracks,
        Guid userId,
        Guid trackId,
        PlaybackEvent playbackEvent,
        double ratio,
        double weight,
        DateTimeOffset now)
    {
        if (!tracks.TryGetValue(trackId, out var affinity))
        {
            affinity = new UserTrackAffinity
            {
                UserId = userId,
                TrackId = trackId,
                DecayAnchor = playbackEvent.OccurredAt,
                FirstPlayedAt = playbackEvent.OccurredAt,
                LastPlayedAt = playbackEvent.OccurredAt,
            };

            db.UserTrackAffinities.Add(affinity);
            tracks[trackId] = affinity;
        }

        switch (playbackEvent.Type)
        {
            case PlaybackEventType.TrackCompleted:
                affinity.PlayCount++;
                affinity.CompletedCount++;
                CountCompletion(affinity, ratio, playbackEvent.ListenedSeconds);
                break;

            case PlaybackEventType.TrackSkipped:
                affinity.PlayCount++;
                if (EventWeights.IsSkip(playbackEvent.Type, ratio))
                    affinity.SkipCount++;
                CountCompletion(affinity, ratio, playbackEvent.ListenedSeconds);
                break;

            case PlaybackEventType.TrackReplayed:
                affinity.ReplayCount++;
                break;

            case PlaybackEventType.TrackAddedToPlaylist:
                affinity.PlaylistAdds++;
                break;

            case PlaybackEventType.TrackRemovedFromPlaylist:
                affinity.PlaylistAdds = Math.Max(0, affinity.PlaylistAdds - 1);
                break;
        }

        if (playbackEvent.OccurredAt > affinity.LastPlayedAt)
            affinity.LastPlayedAt = playbackEvent.OccurredAt;

        if (playbackEvent.OccurredAt < affinity.FirstPlayedAt || affinity.FirstPlayedAt == default)
            affinity.FirstPlayedAt = playbackEvent.OccurredAt;

        if (weight != 0)
        {
            var (accumulated, anchor) = RecencyDecay.Accumulate(
                affinity.DecayedWeight,
                affinity.DecayAnchor,
                weight,
                playbackEvent.OccurredAt,
                Options.TrackHalfLifeDays);

            affinity.DecayedWeight = accumulated;
            affinity.DecayAnchor = anchor;
        }

        affinity.Score = AffinityMath.Normalize(
            RecencyDecay.ValueAt(affinity.DecayedWeight, affinity.DecayAnchor, now, Options.TrackHalfLifeDays),
            Options.ScoreSoftness);

        affinity.UpdatedAt = now;
    }

    public void Apply(
        IDecayingAffinity affinity, PlaybackEvent playbackEvent, double weight, DateTimeOffset now, double halfLife)
    {
        if (playbackEvent.Type == PlaybackEventType.TrackCompleted)
            affinity.PlayCount++;

        if (weight <= EventWeights.DroppedWeight)
            affinity.SkipCount++;

        if (weight != 0)
        {
            var (accumulated, anchor) = RecencyDecay.Accumulate(
                affinity.DecayedWeight, affinity.DecayAnchor, weight, playbackEvent.OccurredAt, halfLife);

            affinity.DecayedWeight = accumulated;
            affinity.DecayAnchor = anchor;
        }

        if (playbackEvent.OccurredAt > affinity.LastPlayedAt)
            affinity.LastPlayedAt = playbackEvent.OccurredAt;

        affinity.Score = AffinityMath.Normalize(
            RecencyDecay.ValueAt(affinity.DecayedWeight, affinity.DecayAnchor, now, halfLife),
            Options.ScoreSoftness);

        affinity.UpdatedAt = now;
    }

    private static void CountCompletion(UserTrackAffinity affinity, double ratio, int listenedSeconds)
    {
        affinity.CompletionSum += ratio;
        affinity.CompletionSamples++;
        affinity.TotalListenedSeconds += listenedSeconds;
    }
}
