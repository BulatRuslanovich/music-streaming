using MusicStreaming.Domain.Common;

namespace MusicStreaming.Domain.Entities;

public class UserSettings
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public bool Autoplay { get; set; } = true;
    public AudioQuality Quality { get; set; } = AudioQuality.Normal;
    public bool DataSaver { get; set; }
    public string TimeZone { get; set; } = "UTC";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public AudioQuality EffectiveQuality => DataSaver ? AudioQuality.Low : Quality;
}
