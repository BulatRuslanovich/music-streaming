// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Linq.Expressions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Common;


public static class ToDto
{
    public static Expression<Func<User, UserDto>> UserProjection { get; } =
        user => new UserDto(user.Id, user.Username, user.DisplayName, user.IsAdmin);

    public static Expression<Func<User, AuthUserDto>> AuthUserProjection { get; } =
        user => new AuthUserDto(
            user.Id,
            user.Username,
            user.DisplayName,
            user.IsAdmin,
            user.IsActive,
            user.CreatedAt);

    private static readonly Func<User, UserDto> MapUser = UserProjection.Compile();
    private static readonly Func<User, AuthUserDto> MapAuthUser = AuthUserProjection.Compile();

    public static UserDto User(User user) => MapUser(user);

    public static AuthUserDto AuthUser(User user) => MapAuthUser(user);

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
        t.OriginalFileName,
        t.Favorites.Any(f => f.UserId == userId),
        t.Album != null && t.Album.CoverPath != null,
        t.Lyrics != null,
        t.CreatedAt,
        t.Codec,
        t.BitrateKbps,
        t.SampleRateHz,
        t.BitsPerSample);

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

    // Обложки жанра докладываются отдельным запросом: коррелированный подзапрос в проекции
    // стрелял бы и там, где мозаика не нужна — например на каждом жанровом запросе поиска.
    public static Expression<Func<Genre, GenreDto>> Genre => g => new GenreDto(
        g.Id,
        g.Name,
        g.Tracks.Count,
        Array.Empty<Guid>());

    public static Expression<Func<Playlist, PlaylistDto>> Playlist => p => new PlaylistDto(
        p.Id,
        p.Name,
        p.Description,
        p.IsPublic,
        p.UserId,
        p.User!.DisplayName,
        p.Tracks.Count,
        p.Tracks.Sum(pt => pt.Track!.DurationSeconds),
        p.CoverPath != null,
        p.Tracks
            .OrderBy(pt => pt.Position)
            .Where(pt => pt.Track!.Album != null && pt.Track.Album.CoverPath != null)
            .Select(pt => (Guid?)pt.TrackId)
            .FirstOrDefault(),
        p.CreatedAt,
        p.UpdatedAt);
}
