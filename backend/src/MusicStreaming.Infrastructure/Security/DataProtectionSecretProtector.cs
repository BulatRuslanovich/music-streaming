using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Infrastructure.Security;

/// <summary>
/// Шифрует секреты ключами ASP.NET Data Protection — теми же, что уже настроены приложением и
/// лежат в хранилище рядом с музыкой, так что перезапуск и обновление образа их не теряют.
/// </summary>
public class DataProtectionSecretProtector(
    IDataProtectionProvider provider, ILogger<DataProtectionSecretProtector> logger) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("music-streaming.integration-secrets");

    public string Protect(string value) => _protector.Protect(value);

    public string? Unprotect(string protectedValue)
    {
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (Exception ex)
        {
            // Ключи потеряли или подменили: молча ничего не отправлять правильнее, чем ронять воркер.
            logger.LogError(ex, "A stored integration secret could not be decrypted");
            return null;
        }
    }
}
