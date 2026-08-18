using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

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

public class TrackLyricsConfiguration : IEntityTypeConfiguration<TrackLyrics>
{
    public void Configure(EntityTypeBuilder<TrackLyrics> builder)
    {
        builder.ToTable("track_lyrics");
        builder.HasKey(l => l.TrackId);

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

public class ListeningStatConfiguration : IEntityTypeConfiguration<ListeningStat>
{
    public void Configure(EntityTypeBuilder<ListeningStat> builder)
    {
        builder.ToTable("listening_stats");

        builder.HasKey(s => new { s.UserId, s.Hour, s.TrackId });

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Track)
            .WithMany()
            .HasForeignKey(s => s.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.TrackId);
    }
}
