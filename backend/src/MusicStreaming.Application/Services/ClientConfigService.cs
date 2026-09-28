// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Настройки установки, которые клиенту нужны до первого запроса данных: лимиты загрузки и срок
/// жизни токена доступа.
/// </summary>
public class ClientConfigService(IOptions<StorageOptions> storage, IOptions<JwtOptions> jwt)
{
    public ClientConfigDto Get() => new(
        storage.Value.MaxUploadBytes,
        storage.Value.MaxImageUploadBytes,
        jwt.Value.AccessTokenMinutes);
}
