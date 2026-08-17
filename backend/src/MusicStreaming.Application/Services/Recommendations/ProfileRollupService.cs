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
    /// <param name="userId">Пользователь, чей профиль пересчитывается.</param>
    /// <param name="ct">Токен отмены — прерывает цикл между батчами, не оставляя частично применённый батч.</param>
    /// <returns>Число событий, учтённых за этот вызов; 0, если новых событий с прошлого watermark не было.</returns>
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

    /// <summary>
    /// Сворачивает один батч сырых событий в изменения строк аффинити (трек/исполнитель/жанр) и
    /// счётчиков профиля. Продвигает watermark профиля до последнего обработанного события в батче.
    /// </summary>
    /// <param name="profile">Профиль пользователя — счётчики и watermark обновляются на месте.</param>
    /// <param name="batch">Упорядоченный по <c>Sequence</c> батч событий.</param>
    /// <param name="now">Текущий момент — точка отсчёта, на которую приводятся все затухающие оценки.</param>
    /// <param name="ct">Токен отмены.</param>
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

        // Строки разветвления тоже грузятся одним запросом на таблицу, а не по одной на первое
        // обращение: батч в пару тысяч событий задевает сотни исполнителей и часов, и запрос на
        // каждого из них стоил бы дороже всего остального роллапа вместе взятого. Наборы ключей
        // берутся из уже загруженных метаданных — с запасом, потому что предзагрузка лишь читает
        // существующие строки, а заводятся новые всё так же лениво, ровно там, где нужны.
        var opened = OpenedArtistsOf(batch);

        var artists = await LoadArtistAffinitiesAsync(userId, ArtistsOf(metadata, albumArtists, opened), ct);
        var genres = await LoadGenreAffinitiesAsync(userId, GenresOf(metadata), ct);
        var listening = await LoadListeningHoursAsync(userId, HoursOf(batch), ct);

        // Исполнитель мог исчезнуть между открытием его страницы и роллапом: приём событий
        // отсеивает только удалённые треки, а внешний ключ строки аффинити ничем не мягче.
        var existingArtists = await LoadExistingArtistsAsync(opened, ct);

        // Строка аффинити к исполнителю из предзагруженного набора; новая заводится при первом
        // обращении.
        UserArtistAffinity ArtistAffinity(Guid artistId)
        {
            if (artists.TryGetValue(artistId, out var existing))
                return existing;

            var created = new UserArtistAffinity { UserId = userId, ArtistId = artistId, DecayAnchor = now };
            db.UserArtistAffinities.Add(created);
            artists[artistId] = created;

            return created;
        }

        // То же самое, что ArtistAffinity выше, но для жанра.
        UserGenreAffinity GenreAffinity(Guid genreId)
        {
            if (genres.TryGetValue(genreId, out var existing))
                return existing;

            var created = new UserGenreAffinity { UserId = userId, GenreId = genreId, DecayAnchor = now };
            db.UserGenreAffinities.Add(created);
            genres[genreId] = created;

            return created;
        }

        // Строка почасовой сводки из предзагруженного набора; новая заводится при первом обращении.
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

                // Та же пара событий, что даёт аффинити прослушанные секунды, наполняет и почасовую
                // сводку — иначе «сколько я слушал» на странице статистики разошлось бы с тем, на чём
                // учится движок рекомендаций.
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

                // Открытый альбом — это интерес к тому, кто его сделал; у самого альбома строки
                // аффинити нет. Не нашедшееся событие просто пропускается: альбом или исполнитель
                // успели исчезнуть, и записывать вкус к тому, чего больше нет, не к чему — а
                // попытка стоила бы падения на внешнем ключе, уносящего весь батч.
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

    /// <summary>
    /// Применяет одно событие к строке аффинити трека: заводит новую строку при первом
    /// взаимодействии, обновляет счётчики по типу события и пересчитывает затухающую оценку.
    /// </summary>
    /// <param name="tracks">Локальный кеш строк аффинити трека в пределах батча.</param>
    /// <param name="userId">Пользователь, чья строка обновляется.</param>
    /// <param name="trackId">Трек, к которому относится событие.</param>
    /// <param name="playbackEvent">Само событие.</param>
    /// <param name="ratio">Доля прослушанного трека к моменту события.</param>
    /// <param name="weight">Знаковый вклад события в аффинити, посчитанный <see cref="EventWeights"/>.</param>
    /// <param name="now">Текущий момент — на него приводится итоговая нормализованная оценка.</param>
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

    /// <summary>
    /// Применяет вклад события к строке аффинити исполнителя или жанра: счётчики, затухающий вес и
    /// нормализованная оценка. Разница между этими двумя разрезами — только период полураспада,
    /// поэтому обоих обслуживает <see cref="IDecayingAffinity"/>.
    /// </summary>
    /// <param name="affinity">Строка аффинити, обновляемая на месте.</param>
    /// <param name="playbackEvent">Событие, из которого берётся момент и тип.</param>
    /// <param name="weight">Знаковый вклад события.</param>
    /// <param name="now">Текущий момент, на который приводится итоговая оценка.</param>
    /// <param name="halfLife">Период полураспада разреза (<c>Options.ArtistHalfLifeDays</c> или <c>Options.GenreHalfLifeDays</c>).</param>
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

    /// <summary>
    /// Обновляет метрики CTR/дослушивания для событий, пришедших с полки рекомендаций, и запоминает
    /// клик, чтобы позже сопоставить его с показом в <see cref="AttributeClicksAsync"/>.
    /// </summary>
    /// <param name="playbackEvent">Событие с <see cref="PlaybackEvent.Source"/> = <see cref="PlaybackSource.Recommendation"/>.</param>
    /// <param name="ratio">Доля прослушанного к моменту события.</param>
    /// <param name="clicked">Список кликов, накапливаемый за весь батч — сюда добавляется запись при старте прослушивания.</param>
    /// <param name="trackId">Трек, к которому относится событие.</param>
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
    /// <param name="userId">Пользователь, чьи показы сопоставляются с прослушиваниями.</param>
    /// <param name="clicked">Прослушивания из этого батча, начатые с полки рекомендаций.</param>
    /// <param name="ct">Токен отмены.</param>
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
    /// <param name="profile">Профиль, чьи производные поля (топ исполнителей/жанров, зрелость, средние показатели) обновляются на месте.</param>
    /// <param name="now">Момент, записываемый как время обновления профиля.</param>
    /// <param name="ct">Токен отмены.</param>
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
    /// <param name="profile">Профиль, чьи <c>YearCenter</c> и <c>YearSpread</c> обновляются на месте.</param>
    /// <param name="ct">Токен отмены.</param>
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

    /// <summary>Минимум метаданных трека, нужный для разноски события по строкам жанра и исполнителей.</summary>
    /// <param name="GenreId">Жанр трека, если задан.</param>
    /// <param name="ArtistIds">Все исполнители трека, включая основного — событие учитывается против каждого.</param>
    private record TrackMetadata(Guid? GenreId, IReadOnlyList<Guid> ArtistIds);

    /// <summary>Одним запросом подгружает жанр и полный список исполнителей для всех треков батча.</summary>
    /// <param name="trackIds">Идентификаторы треков, встретившихся в батче.</param>
    /// <param name="ct">Токен отмены.</param>
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

    /// <summary>Подгружает исполнителя каждого альбома, чья страница была открыта в батче — событие "открыт альбом" учитывается как интерес к его исполнителю.</summary>
    /// <param name="batch">Батч событий, из которого извлекаются идентификаторы открытых альбомов.</param>
    /// <param name="ct">Токен отмены.</param>
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

    /// <summary>Подгружает (с отслеживанием изменений, в отличие от остальных запросов файла) уже существующие строки аффинити треков батча, чтобы обновлять их, а не плодить дубликаты.</summary>
    /// <param name="userId">Пользователь, чьи строки аффинити загружаются.</param>
    /// <param name="trackIds">Треки, встретившиеся в батче.</param>
    /// <param name="ct">Токен отмены.</param>
    private async Task<Dictionary<Guid, UserTrackAffinity>> LoadAffinitiesAsync(
        Guid userId, IReadOnlyList<Guid> trackIds, CancellationToken ct)
    {
        if (trackIds.Count == 0)
            return [];

        return await db.UserTrackAffinities
            .Where(a => a.UserId == userId && trackIds.Contains(a.TrackId))
            .ToDictionaryAsync(a => a.TrackId, ct);
    }

    /// <summary>То же для строк аффинити исполнителей — тоже с отслеживанием, их обновляют на месте.</summary>
    private async Task<Dictionary<Guid, UserArtistAffinity>> LoadArtistAffinitiesAsync(
        Guid userId, List<Guid> artistIds, CancellationToken ct)
    {
        if (artistIds.Count == 0)
            return [];

        return await db.UserArtistAffinities
            .Where(a => a.UserId == userId && artistIds.Contains(a.ArtistId))
            .ToDictionaryAsync(a => a.ArtistId, ct);
    }

    /// <inheritdoc cref="LoadArtistAffinitiesAsync"/>
    private async Task<Dictionary<Guid, UserGenreAffinity>> LoadGenreAffinitiesAsync(
        Guid userId, List<Guid> genreIds, CancellationToken ct)
    {
        if (genreIds.Count == 0)
            return [];

        return await db.UserGenreAffinities
            .Where(a => a.UserId == userId && genreIds.Contains(a.GenreId))
            .ToDictionaryAsync(a => a.GenreId, ct);
    }

    /// <summary>
    /// Существующие строки почасовой сводки за те часы, которых коснётся батч.
    ///
    /// <para>
    /// Отбирается диапазоном часов, а не перечислением пар: первичный ключ сводки —
    /// <c>(UserId, Hour, TrackId)</c>, поэтому пользователь и диапазон часов ложатся ровно на его
    /// префикс. Попавшие в диапазон лишние пары безвредны — это уже существующие строки, которые
    /// батч просто не тронет.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Исполнители, чьи строки аффинити батч может задеть: все соавторы его треков плюс те, к кому
    /// ведут события об открытом альбоме и открытой странице исполнителя.
    /// </summary>
    private static List<Guid> ArtistsOf(
        Dictionary<Guid, TrackMetadata> metadata,
        Dictionary<Guid, Guid> albumArtists,
        List<Guid> opened) =>
        [.. metadata.Values
            .SelectMany(track => track.ArtistIds)
            .Concat(albumArtists.Values)
            .Concat(opened)
            .Distinct()];

    /// <summary>Исполнители, чьи страницы открывали в этом батче.</summary>
    private static List<Guid> OpenedArtistsOf(IReadOnlyList<PlaybackEvent> batch) =>
        [.. batch
            .Where(e => e.Type == PlaybackEventType.ArtistOpened && e.EntityId is not null)
            .Select(e => e.EntityId!.Value)
            .Distinct()];

    /// <summary>Из названных исполнителей — те, что ещё существуют.</summary>
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

    /// <summary>Жанры треков батча.</summary>
    private static List<Guid> GenresOf(Dictionary<Guid, TrackMetadata> metadata) =>
        [.. metadata.Values
            .Where(track => track.GenreId is not null)
            .Select(track => track.GenreId!.Value)
            .Distinct()];

    /// <summary>Часы почасовой сводки, которых коснётся батч, — по одному на каждое завершившееся проигрывание.</summary>
    private static List<(Guid TrackId, DateTimeOffset Hour)> HoursOf(IReadOnlyList<PlaybackEvent> batch) =>
        [.. batch
            .Select(PlayAttempt.From)
            .Where(attempt => attempt is not null)
            .Select(attempt => (attempt!.Value.TrackId, attempt.Value.Hour))
            .Distinct()];
}
