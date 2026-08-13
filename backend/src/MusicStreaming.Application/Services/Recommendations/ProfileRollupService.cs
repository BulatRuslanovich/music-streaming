using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Сворачивает новые события воспроизведения в долговечную модель вкуса пользователя.
///
/// <para>
/// Возобновляемо и идемпотентно: каждый проход стартует от watermark, записанного в профиле, и
/// останавливается на самом свежем увиденном событии. Поэтому второй запуск ничего не меняет, а
/// падение посреди прохода стоит лишь незавершённого батча. Именно это делает безопасной
/// последующую чистку сырых событий: память — это строки аффинити, а не журнал событий.
/// </para>
/// </summary>
public class ProfileRollupService(
    IApplicationDbContext db,
    TimeProvider clock,
    IOptions<RecommendationOptions> options,
    RecommendationMetrics metrics,
    ILogger<ProfileRollupService> logger)
{
    /// <summary>Сколько событий сворачивается за одно обращение к базе.</summary>
    public const int BatchSize = 2000;

    private RecommendationOptions Options => options.Value;

    /// <summary>
    /// Приводит профиль одного пользователя в актуальное состояние. Возвращает число учтённых событий.
    /// </summary>
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

            // Сохраняем побатчево, а не один раз в конце. Производные итоги ниже читаются
            // агрегирующими запросами, поэтому агрегируемые строки должны быть уже в базе; заодно
            // сброс здесь не даёт трекеру изменений расти вместе с журналом событий.
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
        var artists = new Dictionary<Guid, UserArtistAffinity>();
        var genres = new Dictionary<Guid, UserGenreAffinity>();

        // Обе таблицы разветвления грузятся лениво в пределах батча: сессия прослушивания
        // затрагивает куда меньше исполнителей, чем треков, поэтому подгрузка по требованию
        // выгоднее загрузки всего вкуса пользователя.
        async Task<UserArtistAffinity> ArtistAffinity(Guid artistId)
        {
            if (artists.TryGetValue(artistId, out var existing))
                return existing;

            var loaded = await db.UserArtistAffinities
                .FirstOrDefaultAsync(a => a.UserId == userId && a.ArtistId == artistId, ct);

            if (loaded is null)
            {
                loaded = new UserArtistAffinity { UserId = userId, ArtistId = artistId, DecayAnchor = now };
                db.UserArtistAffinities.Add(loaded);
            }

            artists[artistId] = loaded;
            return loaded;
        }

        async Task<UserGenreAffinity> GenreAffinity(Guid genreId)
        {
            if (genres.TryGetValue(genreId, out var existing))
                return existing;

            var loaded = await db.UserGenreAffinities
                .FirstOrDefaultAsync(a => a.UserId == userId && a.GenreId == genreId, ct);

            if (loaded is null)
            {
                loaded = new UserGenreAffinity { UserId = userId, GenreId = genreId, DecayAnchor = now };
                db.UserGenreAffinities.Add(loaded);
            }

            genres[genreId] = loaded;
            return loaded;
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

                foreach (var artistId in track.ArtistIds)
                    Apply(await ArtistAffinity(artistId), playbackEvent, weight, now, Options.ArtistHalfLifeDays);

                if (track.GenreId is { } genreId)
                    Apply(await GenreAffinity(genreId), playbackEvent, weight, now, Options.GenreHalfLifeDays);

                if (playbackEvent.Source == PlaybackSource.Recommendation)
                    RecordRecommendationOutcome(playbackEvent, ratio, clickedFromRecommendations, trackId);
            }
            else if (playbackEvent.EntityId is { } entityId)
            {
                var entityWeight = EventWeights.ForEntity(playbackEvent.Type);
                if (entityWeight == 0)
                    continue;

                // Открытый альбом — это интерес к тому, кто его сделал; у самого альбома строки аффинити нет.
                var artistId = playbackEvent.Type == PlaybackEventType.AlbumOpened
                    ? albumArtists.GetValueOrDefault(entityId)
                    : playbackEvent.Type == PlaybackEventType.ArtistOpened
                        ? entityId
                        : (Guid?)null;

                if (artistId is { } resolved)
                    Apply(await ArtistAffinity(resolved), playbackEvent, entityWeight, now, Options.ArtistHalfLifeDays);
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

    /// <summary>
    /// В общий счёт прослушанные секунды вносят только завершающие события. Heartbeat сообщает
    /// секунды, услышанные к этому моменту в <em>том же</em> прослушивании, поэтому их сложение
    /// посчитало бы одно прослушивание несколько раз.
    /// </summary>
    private static void CountCompletion(UserTrackAffinity affinity, double ratio, int listenedSeconds)
    {
        affinity.CompletionSum += ratio;
        affinity.CompletionSamples++;
        affinity.TotalListenedSeconds += listenedSeconds;
    }

    private void Apply(
        UserArtistAffinity affinity, PlaybackEvent playbackEvent, double weight, DateTimeOffset now, double halfLife)
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

    private void Apply(
        UserGenreAffinity affinity, PlaybackEvent playbackEvent, double weight, DateTimeOffset now, double halfLife)
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

    /// <summary>
    /// Отмечает показы, которые привели к прослушиванию. Кликабельность за счёт этого измеряется
    /// без единой записи на пути чтения: полки фиксируют показанное в момент сборки, а роллап
    /// замыкает круг, когда приходит прослушивание.
    /// </summary>
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

    /// <summary>
    /// Пересчитывает всё, что дешевле вывести из таблиц аффинити, чем тащить инкрементально. Один
    /// такой проход заодно самостоятельно чинит профиль, у которого разъехались текущие итоги.
    /// </summary>
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

    /// <summary>
    /// Помещает пользователя во времени: к какому среднему году выпуска он тяготеет и насколько
    /// далеко от него уходит. У того, кто слушает только записи 1970-х, и у того, кто слушает всё
    /// подряд, среднее окажется около 1975 — различает их именно разброс, поэтому ранжирование
    /// использует обе величины.
    /// </summary>
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

        // Основной исполнитель указывается всегда — даже у трека, чьи записи об авторстве старше него.
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
}
