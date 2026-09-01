// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Integrations;

public enum EnrichmentStatus
{
    Saved,
    Skipped,
    NotFound,
    Ambiguous,
    Instrumental,
}

public record EnrichmentResult(EnrichmentStatus Status, bool Synced = false);

public class LibraryEnrichment(
    IApplicationDbContext db,
    IArtistImageProvider artistImages,
    ILyricsProvider lyricsProvider,
    IMusicStorage storage,
    IImageStorage images,
    IImageProcessor imageProcessor,
    IMusicTagProvider tagProvider,
    IOptions<TagEnrichmentOptions> tagOptions,
    TimeProvider clock)
{
    private TagEnrichmentOptions TagOptions => tagOptions.Value;

    public async Task<EnrichmentResult> EnrichArtistAsync(Guid artistId, CancellationToken ct = default)
    {
        var artist = await db.Artists.FirstOrDefaultAsync(candidate => candidate.Id == artistId, ct);
        if (artist is null || artist.ImagePath is not null)
            return new EnrichmentResult(EnrichmentStatus.Skipped);

        var found = await artistImages.LookupAsync(artist.Name, ct);
        if (found.Status == ArtistImageLookupStatus.NotFound)
            return new EnrichmentResult(EnrichmentStatus.NotFound);
        if (found.Status == ArtistImageLookupStatus.Ambiguous)
            return new EnrichmentResult(EnrichmentStatus.Ambiguous);
        if (found.Content is not { Length: > 0 } content)
            return new EnrichmentResult(EnrichmentStatus.NotFound);

        using var source = new MemoryStream(content, writable: false);
        var renditions = await imageProcessor.ToSquareWebpSetAsync(source, CoverVariants.Edges, ct);
        var path = await images.SaveArtistImageAsync(artist.Id, renditions, ct);

        try
        {
            artist.ImagePath = path;
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            storage.Delete(path);
            throw;
        }

        return new EnrichmentResult(EnrichmentStatus.Saved);
    }

    public async Task<EnrichmentResult> EnrichLyricsAsync(Guid trackId, CancellationToken ct = default)
    {
        var track = await db.Tracks
            .Include(candidate => candidate.Artist)
            .Include(candidate => candidate.Album)
            .Include(candidate => candidate.Lyrics)
            .FirstOrDefaultAsync(candidate => candidate.Id == trackId, ct);

        if (track is null || track.Lyrics is not null)
            return new EnrichmentResult(EnrichmentStatus.Skipped);

        var query = new LyricsQuery(
            track.Title,
            track.Artist?.Name ?? string.Empty,
            track.Album?.Title,
            track.DurationSeconds);

        var found = await lyricsProvider.LookupAsync(query, ct);
        if (found.Status == LyricsLookupStatus.NotFound)
            return new EnrichmentResult(EnrichmentStatus.NotFound);
        if (found.Status == LyricsLookupStatus.Instrumental)
            return new EnrichmentResult(EnrichmentStatus.Instrumental);

        var parsed = LyricsText.Parse(found.Text);
        if (parsed.IsEmpty)
            return new EnrichmentResult(EnrichmentStatus.NotFound);

        db.TrackLyrics.Add(new TrackLyrics
        {
            TrackId = track.Id,
            Plain = parsed.Plain,
            Synced = parsed.Lines,
            Source = LyricsSource.Provider,
            UpdatedAt = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(ct);

        return new EnrichmentResult(EnrichmentStatus.Saved, parsed.Lines.Count > 0);
    }

    /// <summary>
    /// Теги артиста: контентная схожесть двух записей разных исполнителей иначе держится на одном
    /// жанровом ярлыке, а тег-вектор даёт ей нормальную размерность.
    /// </summary>
    public async Task<EnrichmentResult> EnrichArtistTagsAsync(
        Guid artistId, CancellationToken ct = default)
    {
        if (!TagOptions.Enabled || !tagProvider.IsConfigured)
            return new EnrichmentResult(EnrichmentStatus.Skipped);

        var artist = await db.Artists.FirstOrDefaultAsync(candidate => candidate.Id == artistId, ct);
        if (artist is null || IsFresh(artist.TagsFetchedAt))
            return new EnrichmentResult(EnrichmentStatus.Skipped);

        var found = await tagProvider.ArtistTagsAsync(artist.Name, ct);

        await db.ArtistTags.Where(tag => tag.ArtistId == artistId).ExecuteDeleteAsync(ct);

        foreach (var tag in Distinct(found))
            db.ArtistTags.Add(new ArtistTag { ArtistId = artistId, Name = tag.Name, Weight = tag.Weight });

        // Отметка ставится и на пустой ответ: иначе безвестного артиста будут спрашивать вечно.
        artist.TagsFetchedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return new EnrichmentResult(
            found.Count > 0 ? EnrichmentStatus.Saved : EnrichmentStatus.NotFound);
    }

    public async Task<EnrichmentResult> EnrichTrackTagsAsync(Guid trackId, CancellationToken ct = default)
    {
        if (!TagOptions.Enabled || !tagProvider.IsConfigured)
            return new EnrichmentResult(EnrichmentStatus.Skipped);

        var track = await db.Tracks
            .Include(candidate => candidate.Artist)
            .FirstOrDefaultAsync(candidate => candidate.Id == trackId, ct);

        if (track is null || IsFresh(track.TagsFetchedAt))
            return new EnrichmentResult(EnrichmentStatus.Skipped);

        var artistName = track.Artist?.Name ?? string.Empty;
        var found = artistName.Length == 0
            ? []
            : await tagProvider.TrackTagsAsync(artistName, track.Title, ct);

        await db.TrackTags.Where(tag => tag.TrackId == trackId).ExecuteDeleteAsync(ct);

        foreach (var tag in Distinct(found))
            db.TrackTags.Add(new TrackTag { TrackId = trackId, Name = tag.Name, Weight = tag.Weight });

        track.TagsFetchedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return new EnrichmentResult(
            found.Count > 0 ? EnrichmentStatus.Saved : EnrichmentStatus.NotFound);
    }

    private bool IsFresh(DateTimeOffset? fetchedAt) =>
        fetchedAt is { } moment
        && clock.GetUtcNow() - moment < TimeSpan.FromDays(TagOptions.RefreshAfterDays);

    private IEnumerable<ProviderTag> Distinct(IReadOnlyList<ProviderTag> tags) =>
        tags.GroupBy(tag => tag.Name, StringComparer.Ordinal)
            .Select(group => group.MaxBy(tag => tag.Weight)!)
            .OrderByDescending(tag => tag.Weight)
            .Take(TagOptions.MaxTagsPerEntity);
}
