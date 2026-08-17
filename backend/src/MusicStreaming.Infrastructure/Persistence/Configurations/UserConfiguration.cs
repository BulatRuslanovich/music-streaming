using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

/// <summary>
/// Пользователь. Физического удаления нет — есть флаг <c>IsActive</c>: удаление каскадом унесло бы
/// плейлисты, избранное, историю и накопленный профиль вкуса, и отменить это было бы нечем.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();

        // Значения по умолчанию делают ALTER TABLE, добавляющий эти колонки, безопасным на уже
        // заполненной таблице: существующие записи остаются обычными и действующими.
        builder.Property(u => u.IsAdmin).HasDefaultValue(false);
        builder.Property(u => u.IsActive).HasDefaultValue(true);

        builder.HasIndex(u => u.Username).IsUnique();
    }
}

/// <summary>
/// Refresh-токен. В базе лежит только хеш, поэтому утечка дампа не даёт войти. Отозванные строки не
/// удаляются сразу: именно по ним обнаруживается повторное использование украденного токена
/// (см. <c>AuthService.RefreshAsync</c>).
/// </summary>
public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
