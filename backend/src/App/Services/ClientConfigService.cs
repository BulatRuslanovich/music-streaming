// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Common;
using App.Dtos;
using App.Options;
using Microsoft.Extensions.Options;

namespace App.Services;

public class ClientConfigService(IOptions<JwtOptions> jwt)
{
    public ClientConfigDto Get() => new(
        UploadLimits.AudioBytes,
        UploadLimits.ImageBytes,
        jwt.Value.AccessTokenMinutes);
}
