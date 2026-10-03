// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities.Recommendations;

public class UserTrackAffinity : IDecayingAffinity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public int PlayCount { get; set; }
    public int CompletedCount { get; set; }
    public int SkipCount { get; set; }
    public int ReplayCount { get; set; }
    public int PlaylistAdds { get; set; }
    public double CompletionSum { get; set; }
    public int CompletionSamples { get; set; }
    public double DecayedWeight { get; set; }
    public DateTimeOffset DecayAnchor { get; set; }
    public DateTimeOffset LastPlayedAt { get; set; }
    public double AverageCompletion => CompletionSamples == 0 ? 0 : CompletionSum / CompletionSamples;
}

public interface IDecayingAffinity
{
    double DecayedWeight { get; set; }
    DateTimeOffset DecayAnchor { get; set; }
}

public class UserArtistAffinity : IDecayingAffinity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public double DecayedWeight { get; set; }
    public DateTimeOffset DecayAnchor { get; set; }
}

public class UserGenreAffinity : IDecayingAffinity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid GenreId { get; set; }
    public Genre? Genre { get; set; }
    public double DecayedWeight { get; set; }
    public DateTimeOffset DecayAnchor { get; set; }
}
