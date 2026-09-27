// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities.Integrations;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

public class LastfmAccountConfiguration : IEntityTypeConfiguration<LastfmAccount>
{
    public void Configure(EntityTypeBuilder<LastfmAccount> builder)
    {
        // Ключ он же внешний: без явного HasForeignKey EF не узнаёт в нём ссылку на пользователя
        // и заводит теневую колонку user_id1, которой в базе нет.
        builder.HasKey(a => a.UserId);
        builder.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId);
    }
}

public class OutboundJobConfiguration : IEntityTypeConfiguration<OutboundJob>
{
    public void Configure(EntityTypeBuilder<OutboundJob> builder) =>
        builder.Property(j => j.Payload).HasColumnType("jsonb");
}
