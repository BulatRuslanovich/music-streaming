// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace Api.Startup;

// Язык ответов — из Accept-Language (клиенты присылают язык, выбранный в приложении): он становится
// CurrentUICulture запроса. Форматы чисел и дат не локализуются никогда: CurrentCulture остаётся
// инвариантной, иначе на машине с ru-RU «0.5» превратится в «0,5» в аргументах ffmpeg и плейлистах HLS.
public static class LocalizationSetup
{
    private static readonly CultureInfo[] Languages = [new("en"), new("ru")];

    public static void UseInvariantFormats()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = Languages[0];
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = Languages[0];
    }

    public static IApplicationBuilder UseApiLocalization(this IApplicationBuilder app) =>
        app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture, Languages[0]),
            SupportedCultures = [CultureInfo.InvariantCulture],
            SupportedUICultures = Languages,
            RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()],
            ApplyCurrentCultureToResponseHeaders = true,
        });
}
