namespace MusicStreaming.Domain.Entities.Integrations;

public class LastfmAccount
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Username { get; set; } = string.Empty;
    public string SessionKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastScrobbleAt { get; set; }
}
