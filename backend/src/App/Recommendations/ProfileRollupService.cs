// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Domain.Entities;
using Domain.Entities.Recommendations;
using App.Recommendations.Embeddings;
using App.Recommendations.Radio;

namespace App.Recommendations;

public class ProfileRollupService(
    IApplicationDbContext db,
    DerivedTasteRefresher derived,
    TasteVectorFolder tasteVectors,
    TransitionRecorder transitions,
    TimeProvider clock,
    ILogger<ProfileRollupService> logger)
{
    public const int BatchSize = 2000;

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

            List<Guid> Opened(PlaybackEventType type) =>
                [.. batch.Where(e => e.Type == type && e.EntityId is not null).Select(e => e.EntityId!.Value).Distinct()];

            List<Guid> trackIds = [.. batch.Where(e => e.TrackId is not null).Select(e => e.TrackId!.Value).Distinct()];
            var albumIds = Opened(PlaybackEventType.AlbumOpened);
            var opened = Opened(PlaybackEventType.ArtistOpened);

            Dictionary<Guid, (Guid? GenreId, IReadOnlyList<Guid> ArtistIds)> metadata = trackIds.Count == 0
                ? []
                : (await db.Tracks.AsNoTracking()
                    .Where(t => trackIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.GenreId, t.ArtistId, Credits = t.TrackArtists.Select(ta => ta.ArtistId).ToList() })
                    .ToListAsync(ct))
                .ToDictionary(
                    row => row.Id,
                    row => (row.GenreId, (IReadOnlyList<Guid>)(row.Credits.Contains(row.ArtistId)
                        ? row.Credits
                        : [.. row.Credits, row.ArtistId])));

            Dictionary<Guid, Guid> albumArtists = albumIds.Count == 0
                ? []
                : await db.Albums.AsNoTracking()
                    .Where(a => albumIds.Contains(a.Id))
                    .ToDictionaryAsync(a => a.Id, a => a.ArtistId, ct);

            Dictionary<Guid, UserTrackAffinity> tracks = trackIds.Count == 0
                ? []
                : await db.UserTrackAffinities
                    .Where(a => a.UserId == userId && trackIds.Contains(a.TrackId))
                    .ToDictionaryAsync(a => a.TrackId, ct);

            List<Guid> artistIds =
                [.. metadata.Values.SelectMany(track => track.ArtistIds).Concat(albumArtists.Values).Concat(opened).Distinct()];

            Dictionary<Guid, UserArtistAffinity> artists = artistIds.Count == 0
                ? []
                : await db.UserArtistAffinities
                    .Where(a => a.UserId == userId && artistIds.Contains(a.ArtistId))
                    .ToDictionaryAsync(a => a.ArtistId, ct);

            List<Guid> genreIds = [.. metadata.Values.Where(t => t.GenreId is not null).Select(t => t.GenreId!.Value).Distinct()];

            Dictionary<Guid, UserGenreAffinity> genres = genreIds.Count == 0
                ? []
                : await db.UserGenreAffinities
                    .Where(a => a.UserId == userId && genreIds.Contains(a.GenreId))
                    .ToDictionaryAsync(a => a.GenreId, ct);

            var attempts = batch.Select(PlayAttempt.From).OfType<PlayAttempt>().ToList();
            var listening = new Dictionary<(Guid TrackId, DateTimeOffset Hour), ListeningStat>();

            if (attempts.Count > 0)
            {
                var from = attempts.Min(a => a.Hour);
                var to = attempts.Max(a => a.Hour);
                var hourTrackIds = attempts.Select(a => a.TrackId).Distinct().ToList();

                listening = (await db.ListeningStats
                        .Where(s => s.UserId == userId && s.Hour >= from && s.Hour <= to && hourTrackIds.Contains(s.TrackId))
                        .ToListAsync(ct))
                    .ToDictionary(row => (row.TrackId, row.Hour));
            }

            HashSet<Guid> existingArtists = opened.Count == 0
                ? []
                : [.. await db.Artists.AsNoTracking().Where(a => opened.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct)];

            var vector = await tasteVectors.LoadAsync(userId, now, ct);

            UserArtistAffinity ArtistAffinity(Guid artistId)
            {
                if (artists.TryGetValue(artistId, out var existing))
                    return existing;

                var created = new UserArtistAffinity { UserId = userId, ArtistId = artistId, DecayAnchor = now };
                db.UserArtistAffinities.Add(created);
                artists[artistId] = created;

                return created;
            }

            foreach (var playbackEvent in batch)
            {
                profile.EventsWatermark = playbackEvent.Sequence;

                var ratio = EventWeights.CompletionRatio(
                    playbackEvent.ListenedSeconds, playbackEvent.DurationSeconds);

                var weight = playbackEvent.TrackId is null
                    ? 0
                    : EventWeights.ForTrack(playbackEvent.Type, ratio);

                if (weight > 0)
                {
                    var (mass, anchor) = RecencyDecay.Accumulate(
                        profile.PositiveSignalMass,
                        profile.SignalDecayAnchor,
                        1,
                        playbackEvent.OccurredAt,
                        RecommendationTuning.Decay.ProfileHalfLifeDays);

                    profile.PositiveSignalMass = mass;
                    profile.SignalDecayAnchor = anchor;
                }

                tasteVectors.Apply(vector, playbackEvent, ratio);

                if (playbackEvent.TrackId is { } trackId && metadata.TryGetValue(trackId, out var track))
                {
                    if (!tracks.TryGetValue(trackId, out var trackAffinity))
                    {
                        trackAffinity = new UserTrackAffinity
                        {
                            UserId = userId,
                            TrackId = trackId,
                            DecayAnchor = playbackEvent.OccurredAt,
                            LastPlayedAt = playbackEvent.OccurredAt,
                        };

                        db.UserTrackAffinities.Add(trackAffinity);
                        tracks[trackId] = trackAffinity;
                    }

                    switch (playbackEvent.Type)
                    {
                        case PlaybackEventType.TrackCompleted:
                            trackAffinity.PlayCount++;
                            trackAffinity.CompletedCount++;
                            trackAffinity.CompletionSum += ratio;
                            trackAffinity.CompletionSamples++;
                            break;

                        case PlaybackEventType.TrackSkipped:
                            trackAffinity.PlayCount++;
                            if (EventWeights.IsSkip(playbackEvent.Type, ratio))
                                trackAffinity.SkipCount++;
                            trackAffinity.CompletionSum += ratio;
                            trackAffinity.CompletionSamples++;
                            break;

                        case PlaybackEventType.TrackReplayed:
                            trackAffinity.ReplayCount++;
                            break;

                        case PlaybackEventType.TrackAddedToPlaylist:
                            trackAffinity.PlaylistAdds++;
                            break;

                        case PlaybackEventType.TrackRemovedFromPlaylist:
                            trackAffinity.PlaylistAdds = Math.Max(0, trackAffinity.PlaylistAdds - 1);
                            break;
                    }

                    if (playbackEvent.OccurredAt > trackAffinity.LastPlayedAt)
                        trackAffinity.LastPlayedAt = playbackEvent.OccurredAt;

                    Decay(trackAffinity, playbackEvent, weight, now, RecommendationTuning.Decay.TrackHalfLifeDays);

                    if (PlayAttempt.From(playbackEvent) is { } attempt)
                    {
                        if (!listening.TryGetValue((attempt.TrackId, attempt.Hour), out var hour))
                        {
                            hour = new ListeningStat { UserId = userId, TrackId = attempt.TrackId, Hour = attempt.Hour };
                            db.ListeningStats.Add(hour);
                            listening[(attempt.TrackId, attempt.Hour)] = hour;
                        }

                        hour.PlayCount++;
                        hour.ListenedSeconds += attempt.ListenedSeconds;
                    }

                    foreach (var artistId in track.ArtistIds)
                        Decay(ArtistAffinity(artistId), playbackEvent, weight, now, RecommendationTuning.Decay.ArtistHalfLifeDays);

                    if (track.GenreId is { } genreId)
                    {
                        if (!genres.TryGetValue(genreId, out var genre))
                        {
                            genre = new UserGenreAffinity { UserId = userId, GenreId = genreId, DecayAnchor = now };
                            db.UserGenreAffinities.Add(genre);
                            genres[genreId] = genre;
                        }

                        Decay(genre, playbackEvent, weight, now, RecommendationTuning.Decay.GenreHalfLifeDays);
                    }
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
                        Decay(ArtistAffinity(resolved), playbackEvent, entityWeight, now, RecommendationTuning.Decay.ArtistHalfLifeDays);
                }
            }

            await transitions.ApplyAsync(batch, now, ct);

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

    private static void Decay(
        IDecayingAffinity affinity, PlaybackEvent playbackEvent, double weight, DateTimeOffset now, double halfLife)
    {
        if (weight != 0)
        {
            var (accumulated, anchor) = RecencyDecay.Accumulate(
                affinity.DecayedWeight, affinity.DecayAnchor, weight, playbackEvent.OccurredAt, halfLife);

            affinity.DecayedWeight = accumulated;
            affinity.DecayAnchor = anchor;
        }

        // Затухший вес сжимается в (-1, 1): w / (|w| + softness).
        var decayed = RecencyDecay.ValueAt(affinity.DecayedWeight, affinity.DecayAnchor, now, halfLife);
        affinity.Score = decayed / (Math.Abs(decayed) + RecommendationTuning.Decay.ScoreSoftness);
    }
}
