// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text;
using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "music-streaming";
    public string Audience { get; set; } = "music-streaming";
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 10;
    public int RefreshTokenDays { get; set; } = 30;

    public static OptionsBuilder<JwtOptions> Validated(OptionsBuilder<JwtOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "Jwt:SigningKey is required. Set JWT_SIGNING_KEY in .env, or use dotnet user-secrets for local development.")
        .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32, $"Jwt:SigningKey must be at least 32 bytes. Generate one with: openssl rand -base64 48")
        .Validate(o => o.AccessTokenMinutes > 0, "Jwt:AccessTokenMinutes must be greater than zero.")
        .Validate(o => o.RefreshTokenDays > 0, "Jwt:RefreshTokenDays must be greater than zero.");
}
