using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

/// <summary>Настройки пользователя — отдельная таблица 1:1, чтобы не грузить их вместе с самим пользователем на каждом входе.</summary>
public class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("user_settings");
        builder.HasKey(s => s.UserId);

        builder.Property(s => s.TimeZone).HasMaxLength(64).IsRequired();

        builder.HasOne(s => s.User)
            .WithOne(u => u.Settings)
            .HasForeignKey<UserSettings>(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Текст трека: отдельная таблица, потому что это килобайты, не нужные ни одному списку.</summary>
public class TrackLyricsConfiguration : IEntityTypeConfiguration<TrackLyrics>
{
    public void Configure(EntityTypeBuilder<TrackLyrics> builder)
    {
        builder.ToTable("track_lyrics");
        builder.HasKey(l => l.TrackId);

        // Текст песни не влезает в общее ограничение строк в 512 символов; предел разбора и здесь
        // тот же, что применяет LyricsText при чтении тега.
        builder.Property(l => l.Plain)
            .HasColumnType("text")
            .HasMaxLength(LyricsText.MaxLength)
            .IsRequired();

        builder.Property(l => l.Synced)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<LyricLine>(), JsonColumn.Comparer<LyricLine>());

        builder.HasOne(l => l.Track)
            .WithOne(t => t.Lyrics)
            .HasForeignKey<TrackLyrics>(l => l.TrackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Почасовая сводка прослушиваний — источник личной статистики за любой срок.</summary>
public class ListeningStatConfiguration : IEntityTypeConfiguration<ListeningStat>
{
    public void Configure(EntityTypeBuilder<ListeningStat> builder)
    {
        builder.ToTable("listening_stats");

        // Ключ и есть единица сводки: один пользователь, один час, один трек.
        builder.HasKey(s => new { s.UserId, s.Hour, s.TrackId });

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Удалённый трек уносит свою статистику: показать его в топе всё равно было бы нечем.
        builder.HasOne(s => s.Track)
            .WithMany()
            .HasForeignKey(s => s.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        // Каждый запрос статистики — это диапазон часов одного пользователя; первичный ключ уже
        // начинается с этой пары, поэтому отдельного индекса под чтение не нужно. Индекс по треку
        // нужен обратному направлению — очистке при удалении трека.
        builder.HasIndex(s => s.TrackId);
    }
}
