using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities.Integrations;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

/// <summary>Привязка Last.fm. Ключ сессии бессрочен, поэтому хранится зашифрованным.</summary>
public class LastfmAccountConfiguration : IEntityTypeConfiguration<LastfmAccount>
{
    public void Configure(EntityTypeBuilder<LastfmAccount> builder)
    {
        builder.ToTable("lastfm_accounts");
        builder.HasKey(a => a.UserId);

        builder.Property(a => a.Username).HasMaxLength(100).IsRequired();

        // Шифротекст ключа сессии заметно длиннее самого ключа, поэтому общее ограничение в 512
        // символов ему мало.
        builder.Property(a => a.SessionKey).HasMaxLength(2000).IsRequired();

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Задание на исходящую доставку. Таблица намеренно ничего не знает про Last.fm: вид задания плюс
/// непрозрачный JSON, чтобы следующая интеграция добавляла обработчик, а не вторую таблицу со своей
/// копией логики повторов.
/// </summary>
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

        // Защита от дублей на уровне базы: повторная постановка того же прослушивания просто не
        // вставляется, и коду не приходится проверять это гонящейся с самой собой выборкой.
        builder.HasIndex(j => j.DedupeKey).IsUnique();

        // Рабочий запрос воркера: «что уже пора выполнять», в порядке очереди.
        builder.HasIndex(j => new { j.State, j.NextAttemptAt });
    }
}
