namespace MusicStreaming.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Деактивированная запись не может войти, а её действующие сессии отзываются в тот же момент.
    /// Мягкое отключение вместо удаления: идентификатор пользователя стоит в плейлистах, избранном,
    /// истории, событиях и аффинити, поэтому удаление уносит вместе с записью всё, что человек
    /// когда-либо слушал, и отменить это нечем.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public UserSettings? Settings { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<Playlist> Playlists { get; set; } = [];
    public ICollection<Favorite> Favorites { get; set; } = [];
    public ICollection<ListeningHistoryEntry> History { get; set; } = [];
}
