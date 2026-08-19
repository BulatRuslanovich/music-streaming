// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Infrastructure.Security;

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
            logger.LogError(ex, "A stored integration secret could not be decrypted");
            return null;
        }
    }
}
