using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Integrations;

namespace MusicStreaming.Application.Services.Integrations;

/// <summary>
/// Подключение и отключение Last.fm. Сама отправка сюда не заходит — ею занимается очередь
/// исходящих заданий, чтобы недоступность сервиса ничего не задерживала.
/// </summary>
public class LastfmService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ILastfmApi api,
    ISecretProtector secrets,
    TimeProvider clock,
    ILogger<LastfmService> logger)
{
    public async Task<LastfmStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        if (!api.IsConfigured)
            return LastfmStatusDto.Unavailable;

        var account = await db.LastfmAccounts.AsNoTracking()
            .Where(a => a.UserId == currentUser.Id)
            .Select(a => new { a.Username, a.ConnectedAt, a.LastScrobbleAt })
            .FirstOrDefaultAsync(ct);

        return account is null
            ? new LastfmStatusDto(true, null, null, null)
            : new LastfmStatusDto(true, account.Username, account.ConnectedAt, account.LastScrobbleAt);
    }

    /// <summary>Адрес, на который нужно отправить браузер, чтобы пользователь разрешил доступ.</summary>
    /// <param name="callbackUrl">Абсолютный адрес, куда Last.fm вернёт браузер с одноразовым токеном.</param>
    public string AuthorizeUrl(string callbackUrl)
    {
        if (!api.IsConfigured)
            throw new ValidationException("Last.fm is not configured on this server.");

        return api.AuthorizeUrl(callbackUrl);
    }

    /// <summary>Завершает подключение: меняет одноразовый токен на ключ сессии и сохраняет его зашифрованным.</summary>
    public async Task<LastfmStatusDto> CompleteAsync(
        Guid userId, string token, CancellationToken ct = default)
    {
        var session = await api.CompleteAsync(token, ct);
        var now = clock.GetUtcNow();

        var account = await db.LastfmAccounts.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (account is null)
        {
            account = new LastfmAccount { UserId = userId };
            db.LastfmAccounts.Add(account);
        }

        account.Username = session.Username;
        account.SessionKey = secrets.Protect(session.SessionKey);
        account.Enabled = true;
        account.ConnectedAt = now;
        account.LastScrobbleAt = null;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "User {UserId} connected the Last.fm account {LastfmUser}", userId, session.Username);

        return new LastfmStatusDto(true, account.Username, account.ConnectedAt, null);
    }

    /// <summary>Отключает Last.fm и убирает всё, что ещё не успело уехать.</summary>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        var userId = currentUser.Id;

        await db.LastfmAccounts.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);

        // Ждущие задания без учётной записи выполнить всё равно нечем, а их полезная нагрузка —
        // это история прослушивания, которой незачем лежать после отключения.
        await db.OutboundJobs
            .Where(job => job.UserId == userId && job.State == OutboundJobState.Pending)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("User {UserId} disconnected Last.fm", userId);
    }
}
