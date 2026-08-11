using System.Linq.Expressions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Common;


public static class Projections
{
    public static Expression<Func<Track, TrackDto>> Track(Guid userId) => t => new TrackDto(
        t.Id,
        t.Title,
        t.ArtistId,
        t.Artist!.Name,
        t.TrackArtists
            .OrderBy(ta => ta.Position)
            .Select(ta => new ArtistRefDto(ta.ArtistId, ta.Artist!.Name))
            .ToList(),
        t.AlbumId,
        t.Album == null ? null : t.Album.Title,
        t.GenreId,
        t.Genre == null ? null : t.Genre.Name,
        t.TrackNumber,
        t.DiscNumber,
        t.Year,
        t.DurationSeconds,
        t.FileSize,
        t.OriginalFileName,
        t.Favorites.Any(f => f.UserId == userId),
        t.Album != null && t.Album.CoverPath != null,
        t.CreatedAt);

    public static Expression<Func<Album, AlbumDto>> Album => a => new AlbumDto(
        a.Id,
        a.Title,
        a.ArtistId,
        a.Artist!.Name,
        a.Year,
        a.Tracks.Count,
        a.Tracks.Sum(t => t.DurationSeconds),
        a.CoverPath != null,
        a.CreatedAt);

    public static Expression<Func<Artist, ArtistDto>> Artist => a => new ArtistDto(
        a.Id,
        a.Name,
        a.Albums.Count,

        a.TrackCredits.Count,
        a.ImagePath != null);

    public static Expression<Func<Genre, GenreDto>> Genre => g => new GenreDto(
        g.Id,
        g.Name,
        g.Tracks.Count);
}
