// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Http.Features;
using App.Common;

namespace Api.Startup;

public static class LimitsSetup
{
    private const long MultipartOverhead = 1024 * 1024;

    private const int MaxFormValueLength = 64 * 1024;

    public static WebApplicationBuilder AddApiUploadLimits(this WebApplicationBuilder builder)
    {
        const long ceiling = UploadLimits.AudioBytes + MultipartOverhead;

        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = ceiling;
            options.ValueLengthLimit = MaxFormValueLength;
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = ceiling;
            options.Limits.MinRequestBodyDataRate = null;

            options.Limits.MinResponseDataRate = null;
        });

        return builder;
    }
}
