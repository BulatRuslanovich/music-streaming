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
            profile = new UserTasteProfile { UserId = userId, UpdatedAt = now };
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

        await RefreshDerivedAsync(profile, now, ct);
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

        var trackIds = batch.Where(e => e.TrackId is not null).Select(e => e.TrackId!.Value).Distinct().ToList();
        var metadata = await LoadTrackMetadataAsync(trackIds, ct);
        var albumArtists = await LoadAlbumArtistsAsync(batch, ct);

        var tracks = await LoadAffinitiesAsync(userId, trackIds, ct);

        var opened = OpenedArtistsOf(batch);

        var artists = await LoadArtistAffinitiesAsync(userId, ArtistsOf(metadata, albumArtists, opened), ct);
        var genres = await LoadGenreAffinitiesAsync(userId, GenresOf(metadata), ct);
        var listening = await LoadListeningHoursAsync(userId, HoursOf(batch), ct);

        var existingArtists = await LoadExistingArtistsAsync(opened, ct);

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
                profile.PositiveSignalCount++;

            if (playbackEvent.TrackId is { } trackId && metadata.TryGetValue(trackId, out var track))
            {
                ApplyToTrack(tracks, userId, trackId, playbackEvent, ratio, weight, now);

                if (PlayAttempt.From(playbackEvent) is { } attempt)
                {
                    var hour = ListeningHour(attempt.TrackId, attempt.Hour);
                    hour.PlayCount++;
                    hour.ListenedSeconds += attempt.ListenedSeconds;
                }

                foreach (var artistId in track.ArtistIds)
                    Apply(ArtistAffinity(artistId), playbackEvent, weight, now, Options.ArtistHalfLifeDays);

                if (track.GenreId is { } genreId)
                    Apply(GenreAffinity(genreId), playbackEvent, weight, now, Options.GenreHalfLifeDays);

                if (playbackEvent.Source == PlaybackSource.Recommendation)
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
                    Apply(ArtistAffinity(resolved), playbackEvent, entityWeight, now, Options.ArtistHalfLifeDays);
            }
        }

        await AttributeClicksAsync(userId, clickedFromRecommendations, ct);
    }

    private void ApplyToTrack(
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

            case PlaybackEventType.TrackAddedToQueue:
                affinity.QueueAdds++;
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

    private static void CountCompletion(UserTrackAffinity affinity, double ratio, int listenedSeconds)
    {
        affinity.CompletionSum += ratio;
        affinity.CompletionSamples++;
        affinity.TotalListenedSeconds += listenedSeconds;
    }

    private void Apply(
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

    private void RecordRecommendationOutcome(
        PlaybackEvent playbackEvent,
        double ratio,
        List<(Guid TrackId, DateTimeOffset At)> clicked,
        Guid trackId)
    {
        switch (playbackEvent.Type)
        {
            case PlaybackEventType.TrackStarted:
                metrics.RecordPlay();
                clicked.Add((trackId, playbackEvent.OccurredAt));
                break;

            case PlaybackEventType.TrackCompleted:
            case PlaybackEventType.TrackSkipped:
                metrics.RecordCompletion(ratio);
                if (EventWeights.IsSkip(playbackEvent.Type, ratio))
                    metrics.RecordSkip();
                break;
        }
    }

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

    private async Task RefreshDerivedAsync(UserTasteProfile profile, DateTimeOffset now, CancellationToken ct)
    {
        var userId = profile.UserId;

        var totals = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == userId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Tracks = g.Count(),
                Listened = g.Sum(a => a.TotalListenedSeconds),
                CompletionSum = g.Sum(a => a.CompletionSum),
                CompletionSamples = g.Sum(a => a.CompletionSamples),
                Plays = g.Sum(a => a.PlayCount),
                Skips = g.Sum(a => a.SkipCount),
            })
            .FirstOrDefaultAsync(ct);

        profile.DistinctTracks = totals?.Tracks ?? 0;
        profile.TotalListeningSeconds = totals?.Listened ?? 0;
        profile.AverageCompletion = totals is { CompletionSamples: > 0 }
            ? totals.CompletionSum / totals.CompletionSamples
            : 0;
        profile.SkipRate = totals is { Plays: > 0 } ? (double)totals.Skips / totals.Plays : 0;

        profile.TopArtists = await db.UserArtistAffinities.AsNoTracking()
            .Where(a => a.UserId == userId && a.Score > 0)
            .OrderByDescending(a => a.Score)
            .Take(20)
            .Select(a => new TasteEntry(a.ArtistId, a.Artist!.Name, a.Score))
            .ToListAsync(ct);

        profile.DistinctArtists = await db.UserArtistAffinities
            .CountAsync(a => a.UserId == userId, ct);

        profile.TopGenres = await db.UserGenreAffinities.AsNoTracking()
            .Where(a => a.UserId == userId && a.Score > 0)
            .OrderByDescending(a => a.Score)
            .Take(10)
            .Select(a => new TasteEntry(a.GenreId, a.Genre!.Name, a.Score))
            .ToListAsync(ct);

        await RefreshYearTasteAsync(profile, ct);

        profile.Maturity = AffinityMath.MaturityFor(
            profile.PositiveSignalCount, Options.WarmThreshold, Options.MatureThreshold);

        profile.UpdatedAt = now;
    }

    private async Task RefreshYearTasteAsync(UserTasteProfile profile, CancellationToken ct)
    {
        var years = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == profile.UserId && a.Score > 0 && a.Track!.Year != null)
            .Select(a => new { Year = a.Track!.Year!.Value, a.Score })
            .ToListAsync(ct);

        if (years.Count == 0)
        {
            profile.YearCenter = null;
            profile.YearSpread = 0;
            return;
        }

        var totalWeight = years.Sum(y => y.Score);
        if (totalWeight <= 0)
        {
            profile.YearCenter = null;
            profile.YearSpread = 0;
            return;
        }

        var center = years.Sum(y => y.Year * y.Score) / totalWeight;
        var variance = years.Sum(y => y.Score * Math.Pow(y.Year - center, 2)) / totalWeight;

        profile.YearCenter = center;
        profile.YearSpread = Math.Sqrt(variance);
    }

    private record TrackMetadata(Guid? GenreId, IReadOnlyList<Guid> ArtistIds);

    private async Task<Dictionary<Guid, TrackMetadata>> LoadTrackMetadataAsync(
        IReadOnlyList<Guid> trackIds, CancellationToken ct)
    {
        if (trackIds.Count == 0)
            return [];

        var rows = await db.Tracks.AsNoTracking()
            .Where(t => trackIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.GenreId,
                t.ArtistId,
                Credits = t.TrackArtists.Select(ta => ta.ArtistId).ToList(),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(
            row => row.Id,
            row => new TrackMetadata(
                row.GenreId,
                row.Credits.Contains(row.ArtistId) ? row.Credits : [.. row.Credits, row.ArtistId]));
    }

    private async Task<Dictionary<Guid, Guid>> LoadAlbumArtistsAsync(
        IReadOnlyList<PlaybackEvent> batch, CancellationToken ct)
    {
        var albumIds = batch
            .Where(e => e.Type == PlaybackEventType.AlbumOpened && e.EntityId is not null)
            .Select(e => e.EntityId!.Value)
            .Distinct()
            .ToList();

        if (albumIds.Count == 0)
            return [];

        return await db.Albums.AsNoTracking()
            .Where(a => albumIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.ArtistId, ct);
    }

    private async Task<Dictionary<Guid, UserTrackAffinity>> LoadAffinitiesAsync(
        Guid userId, IReadOnlyList<Guid> trackIds, CancellationToken ct)
    {
        if (trackIds.Count == 0)
            return [];

        return await db.UserTrackAffinities
            .Where(a => a.UserId == userId && trackIds.Contains(a.TrackId))
            .ToDictionaryAsync(a => a.TrackId, ct);
    }

    private async Task<Dictionary<Guid, UserArtistAffinity>> LoadArtistAffinitiesAsync(
        Guid userId, List<Guid> artistIds, CancellationToken ct)
    {
        if (artistIds.Count == 0)
            return [];

        return await db.UserArtistAffinities
            .Where(a => a.UserId == userId && artistIds.Contains(a.ArtistId))
            .ToDictionaryAsync(a => a.ArtistId, ct);
    }

    private async Task<Dictionary<Guid, UserGenreAffinity>> LoadGenreAffinitiesAsync(
        Guid userId, List<Guid> genreIds, CancellationToken ct)
    {
        if (genreIds.Count == 0)
            return [];

        return await db.UserGenreAffinities
            .Where(a => a.UserId == userId && genreIds.Contains(a.GenreId))
            .ToDictionaryAsync(a => a.GenreId, ct);
    }

    private async Task<Dictionary<(Guid TrackId, DateTimeOffset Hour), ListeningStat>> LoadListeningHoursAsync(
        Guid userId, List<(Guid TrackId, DateTimeOffset Hour)> hours, CancellationToken ct)
    {
        if (hours.Count == 0)
            return [];

        var from = hours.Min(h => h.Hour);
        var to = hours.Max(h => h.Hour);
        var trackIds = hours.Select(h => h.TrackId).Distinct().ToList();

        var rows = await db.ListeningStats
            .Where(s => s.UserId == userId
                        && s.Hour >= from
                        && s.Hour <= to
                        && trackIds.Contains(s.TrackId))
            .ToListAsync(ct);

        return rows.ToDictionary(row => (row.TrackId, row.Hour));
    }

    private static List<Guid> ArtistsOf(
        Dictionary<Guid, TrackMetadata> metadata,
        Dictionary<Guid, Guid> albumArtists,
        List<Guid> opened) =>
        [.. metadata.Values
            .SelectMany(track => track.ArtistIds)
            .Concat(albumArtists.Values)
            .Concat(opened)
            .Distinct()];

    private static List<Guid> OpenedArtistsOf(IReadOnlyList<PlaybackEvent> batch) =>
        [.. batch
            .Where(e => e.Type == PlaybackEventType.ArtistOpened && e.EntityId is not null)
            .Select(e => e.EntityId!.Value)
            .Distinct()];

    private async Task<HashSet<Guid>> LoadExistingArtistsAsync(List<Guid> artistIds, CancellationToken ct)
    {
        if (artistIds.Count == 0)
            return [];

        var found = await db.Artists.AsNoTracking()
            .Where(a => artistIds.Contains(a.Id))
            .Select(a => a.Id)
            .ToListAsync(ct);

        return [.. found];
    }

    private static List<Guid> GenresOf(Dictionary<Guid, TrackMetadata> metadata) =>
        [.. metadata.Values
            .Where(track => track.GenreId is not null)
            .Select(track => track.GenreId!.Value)
            .Distinct()];

    private static List<(Guid TrackId, DateTimeOffset Hour)> HoursOf(IReadOnlyList<PlaybackEvent> batch) =>
        [.. batch
            .Select(PlayAttempt.From)
            .Where(attempt => attempt is not null)
            .Select(attempt => (attempt!.Value.TrackId, attempt.Value.Hour))
            .Distinct()];
}
