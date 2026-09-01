// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public class ProfileRollupService(
    IApplicationDbContext db,
    ProfileBatchLoader loader,
    AffinityUpdater affinities,
    DerivedTasteRefresher derived,
    TimeProvider clock,
    IOptions<RecommendationOptions> options,
    RecommendationMetrics metrics,
    ILogger<ProfileRollupService> logger)
{
    public const int BatchSize = 2000;

    private RecommendationOptions Options => options.Value;

    public async Task<int> RollupAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var profile = await db.UserTasteProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new UserTasteProfile { UserId = userId, UpdatedAt = now, SignalDecayAnchor = now };
            db.UserTasteProfiles.Add(profile);
        }

        var processed = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.PlaybackEvents.AsNoTracking()
                .Where(e => e.UserId == userId && e.Sequence > profile.EventsWatermark)
                .OrderBy(e => e.Sequence)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            await ApplyBatchAsync(profile, batch, now, ct);

            await db.SaveChangesAsync(ct);

            processed += batch.Count;

            if (batch.Count < BatchSize)
                break;
        }

        await derived.RefreshAsync(profile, now, ct);
        await db.SaveChangesAsync(ct);

        if (processed > 0)
            logger.LogDebug("Folded {Count} events into the profile of user {UserId}", processed, userId);

        return processed;
    }

    private async Task ApplyBatchAsync(
        UserTasteProfile profile,
        IReadOnlyList<PlaybackEvent> batch,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var userId = profile.UserId;

        var (metadata, albumArtists, tracks, artists, genres, listening, existingArtists) =
            await loader.LoadAsync(userId, batch, ct);

        UserArtistAffinity ArtistAffinity(Guid artistId)
        {
            if (artists.TryGetValue(artistId, out var existing))
                return existing;

            var created = new UserArtistAffinity { UserId = userId, ArtistId = artistId, DecayAnchor = now };
            db.UserArtistAffinities.Add(created);
            artists[artistId] = created;

            return created;
        }

        UserGenreAffinity GenreAffinity(Guid genreId)
        {
            if (genres.TryGetValue(genreId, out var existing))
                return existing;

            var created = new UserGenreAffinity { UserId = userId, GenreId = genreId, DecayAnchor = now };
            db.UserGenreAffinities.Add(created);
            genres[genreId] = created;

            return created;
        }

        ListeningStat ListeningHour(Guid trackId, DateTimeOffset hour)
        {
            if (listening.TryGetValue((trackId, hour), out var existing))
                return existing;

            var created = new ListeningStat { UserId = userId, TrackId = trackId, Hour = hour };
            db.ListeningStats.Add(created);
            listening[(trackId, hour)] = created;

            return created;
        }

        var clickedFromRecommendations = new List<(Guid TrackId, DateTimeOffset At)>();

        foreach (var playbackEvent in batch)
        {
            profile.TotalEventCount++;
            profile.EventsWatermark = playbackEvent.Sequence;

            var ratio = EventWeights.CompletionRatio(
                playbackEvent.ListenedSeconds, playbackEvent.DurationSeconds);

            var weight = playbackEvent.TrackId is null
                ? 0
                : EventWeights.ForTrack(playbackEvent.Type, ratio);

            if (weight > 0)
            {
                profile.PositiveSignalCount++;

                // Масса копится по одному за сигнал, чтобы пороги зрелости оставались в тех же единицах.
                var (mass, anchor) = RecencyDecay.Accumulate(
                    profile.PositiveSignalMass,
                    profile.SignalDecayAnchor,
                    1,
                    playbackEvent.OccurredAt,
                    Options.ProfileHalfLifeDays);

                profile.PositiveSignalMass = mass;
                profile.SignalDecayAnchor = anchor;
            }

            if (playbackEvent.TrackId is { } trackId && metadata.TryGetValue(trackId, out var track))
            {
                affinities.ApplyToTrack(tracks, userId, trackId, playbackEvent, ratio, weight, now);

                if (PlayAttempt.From(playbackEvent) is { } attempt)
                {
                    var hour = ListeningHour(attempt.TrackId, attempt.Hour);
                    hour.PlayCount++;
                    hour.ListenedSeconds += attempt.ListenedSeconds;
                }

                foreach (var artistId in track.ArtistIds)
                    affinities.Apply(ArtistAffinity(artistId), playbackEvent, weight, now, Options.ArtistHalfLifeDays);

                if (track.GenreId is { } genreId)
                    affinities.Apply(GenreAffinity(genreId), playbackEvent, weight, now, Options.GenreHalfLifeDays);

                if (IsRecommendationSource(playbackEvent.Source))
                    RecordRecommendationOutcome(playbackEvent, ratio, clickedFromRecommendations, trackId);
            }
            else if (playbackEvent.EntityId is { } entityId)
            {
                var entityWeight = EventWeights.ForEntity(playbackEvent.Type);
                if (entityWeight == 0)
                    continue;

                var artistId = playbackEvent.Type switch
                {
                    PlaybackEventType.AlbumOpened =>
                        albumArtists.TryGetValue(entityId, out var owner) ? owner : null,
                    PlaybackEventType.ArtistOpened =>
                        existingArtists.Contains(entityId) ? entityId : null,
                    _ => (Guid?)null,
                };

                if (artistId is { } resolved)
                    affinities.Apply(ArtistAffinity(resolved), playbackEvent, entityWeight, now, Options.ArtistHalfLifeDays);
            }
        }

        await AttributeClicksAsync(userId, clickedFromRecommendations, ct);
    }

    private void RecordRecommendationOutcome(
        PlaybackEvent playbackEvent,
        double ratio,
        List<(Guid TrackId, DateTimeOffset At)> clicked,
        Guid trackId)
    {
        var source = playbackEvent.Source.ToString().ToLowerInvariant();

        switch (playbackEvent.Type)
        {
            case PlaybackEventType.TrackStarted:
                metrics.RecordPlay(source);
                clicked.Add((trackId, playbackEvent.OccurredAt));
                break;

            case PlaybackEventType.TrackCompleted:
            case PlaybackEventType.TrackSkipped:
                metrics.RecordCompletion(ratio, source);
                if (EventWeights.IsSkip(playbackEvent.Type, ratio))
                    metrics.RecordSkip(source);
                break;
        }
    }

    private static bool IsRecommendationSource(PlaybackSource source) => source is
        PlaybackSource.Recommendation or PlaybackSource.Dj or PlaybackSource.Radio;

    private async Task AttributeClicksAsync(
        Guid userId, List<(Guid TrackId, DateTimeOffset At)> clicked, CancellationToken ct)
    {
        if (clicked.Count == 0)
            return;

        var trackIds = clicked.Select(c => c.TrackId).Distinct().ToList();
        var earliest = clicked.Min(c => c.At).AddDays(-Options.ImpressionCooldownDays);

        var impressions = await db.RecommendationImpressions
            .Where(i => i.UserId == userId
                        && i.ClickedAt == null
                        && trackIds.Contains(i.TrackId)
                        && i.ShownAt >= earliest)
            .ToListAsync(ct);

        foreach (var impression in impressions)
        {
            var play = clicked
                .Where(c => c.TrackId == impression.TrackId && c.At >= impression.ShownAt)
                .Select(c => (DateTimeOffset?)c.At)
                .FirstOrDefault();

            if (play is null)
                continue;

            impression.ClickedAt = play;
            metrics.RecordClick();
        }
    }

}
