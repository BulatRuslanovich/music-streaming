namespace MusicStreaming.Domain.Entities.Recommendations;

public class UserTrackAffinity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public int PlayCount { get; set; }
    public int CompletedCount { get; set; }
    public int SkipCount { get; set; }
    public int ReplayCount { get; set; }
    public int QueueAdds { get; set; }
    public int PlaylistAdds { get; set; }
    public long TotalListenedSeconds { get; set; }
    public double CompletionSum { get; set; }
    public int CompletionSamples { get; set; }
    public double DecayedWeight { get; set; }
    public DateTimeOffset DecayAnchor { get; set; }
    public double Score { get; set; }
    public DateTimeOffset FirstPlayedAt { get; set; }
    public DateTimeOffset LastPlayedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public double AverageCompletion => CompletionSamples == 0 ? 0 : CompletionSum / CompletionSamples;
}

public interface IDecayingAffinity
{
    int PlayCount { get; set; }
    int SkipCount { get; set; }

    double DecayedWeight { get; set; }
    DateTimeOffset DecayAnchor { get; set; }
    double Score { get; set; }

    DateTimeOffset LastPlayedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}

public class UserArtistAffinity : IDecayingAffinity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public int PlayCount { get; set; }
    public int SkipCount { get; set; }
    public double DecayedWeight { get; set; }
    public DateTimeOffset DecayAnchor { get; set; }
    public double Score { get; set; }
    public DateTimeOffset LastPlayedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class UserGenreAffinity : IDecayingAffinity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid GenreId { get; set; }
    public Genre? Genre { get; set; }
    public int PlayCount { get; set; }
    public int SkipCount { get; set; }
    public double DecayedWeight { get; set; }
    public DateTimeOffset DecayAnchor { get; set; }
    public double Score { get; set; }
    public DateTimeOffset LastPlayedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
