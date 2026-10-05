// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;

namespace App.Recommendations;

public class ProfileRollupService(
    ApplicationDbContext db,
    TimeProvider clock,
    ILogger<ProfileRollupService> logger)
{
    public const int BatchSize = 2000;

    // −3 при мягкости 3 даёт счёт трека −0.5; ниже порога отвержения он остаётся около 50 дней.
    private const double DismissedTrackWeight = -3.0;
    private const double DismissedContextShare = 0.25;

    public async Task<int> RollupAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var profile = await db.UserTasteProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new UserTasteProfile { UserId = userId, SignalDecayAnchor = now };
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

            HashSet<Guid> existingArtists = opened.Count == 0
                ? []
                : [.. await db.Artists.AsNoTracking().Where(a => opened.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct)];


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

                // Вес сигнала: явные действия (лайк, плейлист) весят больше прослушивания,
                // скип — по тому, как рано бросили трек.
                var weight = playbackEvent.TrackId is null ? 0 : playbackEvent.Type switch
                {
                    PlaybackEventType.TrackSkipped => ratio switch
                    {
                        < 0.05 => -1.0,
                        < 0.20 => -0.5,
                        < 0.50 => -0.1,
                        < 0.80 => 0.3,
                        _ => 0.8,
                    },
                    PlaybackEventType.TrackCompleted => 1.0,
                    PlaybackEventType.TrackReplayed => 0.8,
                    PlaybackEventType.TrackLiked => 2.5,
                    PlaybackEventType.TrackUnliked => -2.5,
                    PlaybackEventType.TrackAddedToPlaylist => 2.0,
                    PlaybackEventType.TrackRemovedFromPlaylist => -1.5,
                    PlaybackEventType.TrackAddedToQueue => 0.8,
                    PlaybackEventType.TrackDismissed => DismissedTrackWeight,
                    _ => 0,
                };

                // Отказ от трека — не отказ от артиста: на артистов и жанр ложится лишь часть веса.
                var contextWeight = playbackEvent.Type == PlaybackEventType.TrackDismissed
                    ? weight * DismissedContextShare
                    : weight;

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
                            if (ratio < 0.20)
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

                    Decay(trackAffinity, playbackEvent, weight, RecommendationTuning.Decay.TrackHalfLifeDays);

                    foreach (var artistId in track.ArtistIds)
                        Decay(ArtistAffinity(artistId), playbackEvent, contextWeight, RecommendationTuning.Decay.ArtistHalfLifeDays);

                    if (track.GenreId is { } genreId)
                    {
                        if (!genres.TryGetValue(genreId, out var genre))
                        {
                            genre = new UserGenreAffinity { UserId = userId, GenreId = genreId, DecayAnchor = now };
                            db.UserGenreAffinities.Add(genre);
                            genres[genreId] = genre;
                        }

                        Decay(genre, playbackEvent, contextWeight, RecommendationTuning.Decay.GenreHalfLifeDays);
                    }
                }
                else if (playbackEvent.EntityId is { } entityId)
                {
                    var entityWeight = playbackEvent.Type is PlaybackEventType.ArtistOpened or PlaybackEventType.AlbumOpened
                        ? 0.2
                        : 0;
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
                        Decay(ArtistAffinity(resolved), playbackEvent, entityWeight, RecommendationTuning.Decay.ArtistHalfLifeDays);
                }
            }

            await db.SaveChangesAsync(ct);

            processed += batch.Count;

            if (batch.Count < BatchSize)
                break;
        }

        await db.SaveChangesAsync(ct);

        if (processed > 0)
            logger.LogDebug("Folded {Count} events into the profile of user {UserId}", processed, userId);

        return processed;
    }

    private static void Decay(IDecayingAffinity affinity, PlaybackEvent playbackEvent, double weight, double halfLife)
    {
        if (weight == 0)
            return;

        (affinity.DecayedWeight, affinity.DecayAnchor) = RecencyDecay.Accumulate(
            affinity.DecayedWeight, affinity.DecayAnchor, weight, playbackEvent.OccurredAt, halfLife);
    }
}
