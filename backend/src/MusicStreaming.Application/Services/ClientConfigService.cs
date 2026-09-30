// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Services;

public class ClientConfigService(IOptions<JwtOptions> jwt)
{
    public ClientConfigDto Get() => new(
        UploadLimits.AudioBytes,
        UploadLimits.ImageBytes,
        jwt.Value.AccessTokenMinutes);
}
