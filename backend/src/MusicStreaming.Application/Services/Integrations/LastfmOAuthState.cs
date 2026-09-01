// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Application.Services.Integrations;

/// <summary>
/// Одноразовая метка, связывающая начало подключения Last.fm с возвратом из браузера.
/// </summary>
/// <remarks>
/// Возврат приходит анонимным запросом с чужого сайта, поэтому кто именно подключался, известно
/// только из этой метки. Она защищена и живёт минуты: просроченная равнозначна отсутствующей.
/// Раньше её выпуск и разбор были в контроллере, и вместе с ними туда приходили протектор, часы
/// и время жизни.
/// </remarks>
public class LastfmOAuthState(ISecretProtector secrets, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public string Issue(Guid userId) =>
        secrets.Protect($"{userId}|{clock.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds()}");

    public Guid? Resolve(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return null;

        if (secrets.Unprotect(state)?.Split('|') is not [var user, var expiry])
            return null;

        return Guid.TryParse(user, out var userId)
               && long.TryParse(expiry, out var unix)
               && DateTimeOffset.FromUnixTimeSeconds(unix) > clock.GetUtcNow()
            ? userId
            : null;
    }
}
