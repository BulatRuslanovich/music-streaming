// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Api.Startup;

/// <summary>
/// Знак в логе старта. Видит его только тот, у кого этот сервер свой, — то есть ровно тот, для
/// кого проект вообще писался. Волна из семи штрихов, как в <c>BrandMark</c>.
/// </summary>
public static class StartupBanner
{
    private const string Mark = """

          ___
         /   ‾\   . : | : . : | : .
        |  C          A I M A C K
         \___ /   . : | : . : | : .

        """;

    public static void LogStartupBanner(this WebApplication app)
    {
        var build = BuildInfo.Current;

        var stamp = build.BuiltAt is { } builtAt
            ? builtAt.ToString("yyyy-MM-dd HH:mm 'UTC'")
            : "—";

        // Через Serilog, а не Console.WriteLine: иначе баннер обошёл бы сконфигурированный сток.
        // {0} с уже собранной строкой — шаблон здесь один на все запуски, разворачивать его в
        // структурные поля нечего.
        app.Logger.LogInformation(
            "{Banner}",
            $"{Mark}  {build.Version} · {build.Commit ?? "local"} · {stamp}{Environment.NewLine}");
    }
}
