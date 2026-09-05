// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Теги как раздел каталога. Собирают их <c>LibraryEnrichmentWorker</c> и <c>TagBackfillWorker</c>
/// ради схожести, и правило «чей это тег» здесь ровно то же, что в <c>SimilarityMaintenance</c>:
/// собственные теги трека плюс теги его исполнителей с долей
/// <see cref="TagWeights.ArtistShare"/>, максимум по имени. Разойдись эти два правила — полка и
/// страница тега спорили бы о том, что вообще считается этим тегом.
/// </summary>
public class TagBrowseService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IOptions<TagEnrichmentOptions> options)
{
    /// <summary>Сколько обложек уходит в мозаику карточки тега — как у жанров.</summary>
    private const int CoverCount = 4;

    /// <summary>
    /// Тег на двух треках — не раздел каталога, а хвост: Last.fm возвращает по дюжине тегов на
    /// запись, и без порога сетка состоит из синглтонов, каждый со своей страницей на один трек.
    /// </summary>
    private const int MinimumTracks = 3;

    private const int DefaultTagLimit = 60;
    private const int MaxTagLimit = 200;
    private const int DefaultArtistLimit = 12;
    private const int MaxArtistLimit = 60;

    /// <summary>
    /// Порог тот же, что на входе: доля исполнителя может увести унаследованный тег ниже него,
    /// и такой тег уже ничего не описывает.
    /// </summary>
    private double MinimumWeight => options.Value.MinimumTagWeight;

    /// <summary>Теги библиотеки: самые населённые вперёд, с обложками для сетки.</summary>
    public async Task<IReadOnlyList<TagDto>> GetTagsAsync(int? limit, CancellationToken ct)
    {
        var take = limit is null or < 1 ? DefaultTagLimit : Math.Min(limit.Value, MaxTagLimit);

        // Счёт по тегу и представительные обложки — два оконных прохода по одному и тому же
        // набору, поэтому один запрос: LINQ здесь дал бы либо N+1 за обложками, либо выгрузку
        // всех пар «тег — трек» в память.
        var rows = await db.Set<TagRow>().FromSql(
            $"""
            WITH effective AS (
                SELECT track_id, name, MAX(weight) AS weight
                FROM (
                    SELECT tt.track_id, tt.name, tt.weight
                    FROM track_tags tt
                    UNION ALL
                    SELECT ta.track_id, art.name, art.weight * {TagWeights.ArtistShare}
                    FROM track_artists ta
                    JOIN artist_tags art ON art.artist_id = ta.artist_id
                ) parts
                GROUP BY track_id, name
            ),
            kept AS (
                SELECT track_id, name, weight FROM effective WHERE weight >= {MinimumWeight}
            ),
            counted AS (
                SELECT name, COUNT(*)::int AS track_count
                FROM kept
                GROUP BY name
                HAVING COUNT(*) >= {MinimumTracks}
                ORDER BY COUNT(*) DESC, name
                LIMIT {take}
            ),
            covers AS (
                SELECT k.name,
                       t.album_id,
                       row_number() OVER (
                           PARTITION BY k.name
                           ORDER BY MAX(k.weight) DESC, t.album_id) AS rank
                FROM kept k
                JOIN counted c ON c.name = k.name
                JOIN tracks t ON t.id = k.track_id
                JOIN albums a ON a.id = t.album_id AND a.cover_path IS NOT NULL
                GROUP BY k.name, t.album_id
            )
            SELECT c.name, c.track_count, v.album_id
            FROM counted c
            LEFT JOIN covers v ON v.name = c.name AND v.rank <= {CoverCount}
            ORDER BY c.track_count DESC, c.name, v.rank
            """).ToListAsync(ct);

        return
        [
            .. rows
                .GroupBy(row => row.Name)
                .Select(group => new TagDto(
                    group.Key,
                    group.First().TrackCount,
                    [.. group.Where(row => row.AlbumId != null).Select(row => row.AlbumId!.Value)]))
        ];
    }

    /// <summary>
    /// Треки тега: сначала те, кому он принадлежит сильнее, при равном весе — то, что слушают.
    /// Незнакомое имя — пустая страница, а не 404: за именем тега нет сущности, которой могло
    /// бы не быть.
    /// </summary>
    public async Task<PagedResult<TrackDto>> GetTagTracksAsync(
        string? name, PageRequest page, CancellationToken ct)
    {
        if (Normalize(name) is not { } tag)
            return PagedResult<TrackDto>.Empty(page);

        var minimum = MinimumWeight;

        // Вес считается подзапросами, а не объединением двух выборок: набор из UNION уезжает
        // в SQL только целыми сущностями, а здесь нужна пара «трек — вес». Отбор по наличию
        // тега идёт первым и отдельно от веса: он ложится на индексы по имени в обеих таблицах
        // тегов, иначе оба подзапроса считались бы для каждого трека библиотеки.
        var query = db.Tracks.AsNoTracking()
            .Where(track =>
                track.Tags.Any(own => own.Name == tag)
                || track.TrackArtists.Any(link => link.Artist!.Tags.Any(borrowed => borrowed.Name == tag)))
            .Select(track => new
            {
                Track = track,
                Weight = Math.Max(
                    track.Tags
                        .Where(own => own.Name == tag)
                        .Select(own => (double?)own.Weight)
                        .Max() ?? 0,
                    (track.TrackArtists
                        .SelectMany(link => link.Artist!.Tags)
                        .Where(inherited => inherited.Name == tag)
                        .Select(inherited => (double?)inherited.Weight)
                        .Max() ?? 0) * TagWeights.ArtistShare),
            })
            .Where(row => row.Weight >= minimum)
            .OrderByDescending(row => row.Weight)
            .ThenByDescending(row => row.Track.Stats == null ? 0 : row.Track.Stats.PopularityScore)
            .ThenBy(row => row.Track.Title);

        var total = await query.CountAsync(ct);

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(row => row.Track)
            .Select(ToDto.Track(currentUser.Id))
            .ToListAsync(ct);

        return new PagedResult<TrackDto>(items, total, page.Page, page.PageSize);
    }

    /// <summary>
    /// Исполнители тега берутся только по своим тегам: наследование идёт вниз, к трекам, и
    /// обратный ход означал бы «этот исполнитель — джаз, потому что джаз у его же трека».
    /// </summary>
    public async Task<IReadOnlyList<ArtistDto>> GetTagArtistsAsync(
        string? name, int? limit, CancellationToken ct)
    {
        if (Normalize(name) is not { } tag)
            return [];

        var take = limit is null or < 1 ? DefaultArtistLimit : Math.Min(limit.Value, MaxArtistLimit);

        return await db.ArtistTags.AsNoTracking()
            .Where(row => row.Name == tag && row.Weight >= MinimumWeight)
            .OrderByDescending(row => row.Weight)
            .ThenBy(row => row.Artist!.Name)
            .Take(take)
            .Select(row => row.Artist!)
            .Select(ToDto.Artist)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Теги трека — по запросу, как и разбор записи: в списках они не нужны. Свои и
    /// унаследованные складываются здесь, а не в базе: их вместе не больше пары десятков, а
    /// объединение двух выборок в SQL стоило бы запроса, который EF отдаёт только сущностями.
    /// </summary>
    public async Task<IReadOnlyList<TagWeightDto>> GetTrackTagsAsync(Guid trackId, CancellationToken ct)
    {
        await db.RequireTrackAsync(trackId, ct);

        var own = await db.TrackTags.AsNoTracking()
            .Where(tag => tag.TrackId == trackId)
            .Select(tag => new TagWeightDto(tag.Name, tag.Weight))
            .ToListAsync(ct);

        var inherited = await db.TrackArtists.AsNoTracking()
            .Where(link => link.TrackId == trackId)
            .SelectMany(link => link.Artist!.Tags)
            .Select(tag => new TagWeightDto(tag.Name, tag.Weight * TagWeights.ArtistShare))
            .ToListAsync(ct);

        return
        [
            .. own.Concat(inherited)
                .GroupBy(tag => tag.Name)
                .Select(group => new TagWeightDto(group.Key, group.Max(tag => tag.Weight)))
                .Where(tag => tag.Weight >= MinimumWeight)
                .OrderByDescending(tag => tag.Weight)
                .ThenBy(tag => tag.Name, StringComparer.Ordinal)
                .Take(options.Value.MaxTagsPerEntity)
        ];
    }

    /// <summary>Имена хранятся в нижнем регистре — так же приводится и то, что пришло снаружи.</summary>
    private static string? Normalize(string? name)
    {
        var trimmed = name?.Trim().ToLowerInvariant();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

/// <summary>Строка сводки по тегу: счёт повторяется в каждой строке обложек одного тега.</summary>
public class TagRow
{
    public string Name { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public Guid? AlbumId { get; set; }
}
