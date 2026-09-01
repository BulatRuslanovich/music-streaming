// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations;

/// <summary>Трек-затравка и его вес: чем ближе и чем теплее принят, тем сильнее.</summary>
public readonly record struct RecommendationSeed(Guid TrackId, double Weight);

public record UserRecommendationContext(
    Guid UserId,
    UserTasteProfile Profile,
    RankingContext Ranking,
    IReadOnlyList<RecommendationSeed> Seeds,
    IReadOnlyDictionary<Guid, double> GenreShare)
{
    /// <summary>Треки и артисты, которым пользователь сказал «не интересно».</summary>
    public IReadOnlySet<Guid> SuppressedTracks { get; init; } = new HashSet<Guid>();
    public IReadOnlySet<Guid> SuppressedArtists { get; init; } = new HashSet<Guid>();

    public bool IsColdStart => Profile.PositiveSignalCount == 0;
    public IReadOnlyList<Guid> SeedTrackIds => Seeds.Select(seed => seed.TrackId).ToList();

    public bool IsSuppressed(Guid trackId, IReadOnlyList<Guid> credits)
    {
        if (SuppressedTracks.Contains(trackId))
            return true;

        foreach (var artistId in credits)
        {
            if (SuppressedArtists.Contains(artistId))
                return true;
        }

        return false;
    }
}
