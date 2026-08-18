using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities.Integrations;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

public class LastfmAccountConfiguration : IEntityTypeConfiguration<LastfmAccount>
{
    public void Configure(EntityTypeBuilder<LastfmAccount> builder)
    {
        builder.ToTable("lastfm_accounts");
        builder.HasKey(a => a.UserId);

        builder.Property(a => a.Username).HasMaxLength(100).IsRequired();

        builder.Property(a => a.SessionKey).HasMaxLength(2000).IsRequired();

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class OutboundJobConfiguration : IEntityTypeConfiguration<OutboundJob>
{
    public void Configure(EntityTypeBuilder<OutboundJob> builder)
    {
        builder.ToTable("outbound_jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(j => j.DedupeKey).HasMaxLength(200).IsRequired();
        builder.Property(j => j.LastError).HasMaxLength(500);

        builder.HasOne(j => j.User)
            .WithMany()
            .HasForeignKey(j => j.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(j => j.DedupeKey).IsUnique();

        builder.HasIndex(j => new { j.State, j.NextAttemptAt });
    }
}
