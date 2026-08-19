// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Infrastructure.Integrations;
using MusicStreaming.Infrastructure.Persistence;

var limit = ParseLimitArg(args);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development",
});

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options => options
    .UseNpgsql(connectionString, npgsql => npgsql
        .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
    .UseSnakeCaseNamingConvention());

builder.Services.AddOptions<LrclibOptions>()
    .Bind(builder.Configuration.GetSection(LrclibOptions.SectionName));

// LRCLIB не просит ключа, но просит представляться: по User-Agent они отличают клиентов и к кому
// идти, когда один из них начинает вести себя неаккуратно.
builder.Services.AddHttpClient<LrclibClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent());
});

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var services = scope.ServiceProvider;

var logger = services.GetRequiredService<ILogger<Program>>();
var db = services.GetRequiredService<ApplicationDbContext>();
var lrclib = services.GetRequiredService<LrclibClient>();
var lrclibOptions = services.GetRequiredService<IOptions<LrclibOptions>>().Value;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
var ct = cts.Token;

// Тексты из тегов и правки руками остаются нетронутыми: инструмент заходит только туда, где текста
// нет вовсе.
IQueryable<Track> query = db.Tracks
    .Include(t => t.Artist)
    .Include(t => t.Album)
    .Where(t => t.Lyrics == null)
    .OrderBy(t => t.Artist!.Name)
    .ThenBy(t => t.Title);

if (limit is not null)
    query = query.Take(limit.Value);

var tracks = await query.ToListAsync(ct);
logger.LogInformation("Found {Count} tracks without lyrics", tracks.Count);

var synced = 0;
var plain = 0;
var instrumental = new List<string>();
var notFound = new List<string>();
var errored = new List<string>();

foreach (var track in tracks)
{
    if (ct.IsCancellationRequested)
        break;

    var label = $"{track.Artist?.Name ?? "?"} — {track.Title}";

    try
    {
        var lookup = new LyricsQuery(
            track.Title, track.Artist?.Name ?? string.Empty, track.Album?.Title, track.DurationSeconds);

        var result = await LookupWithRetryAsync(lrclib, lookup, label, logger, ct);

        switch (result.Status)
        {
            case LyricsLookupStatus.Instrumental:
                instrumental.Add(label);
                logger.LogInformation("LRCLIB marks {Track} as instrumental", label);
                break;

            case LyricsLookupStatus.NotFound:
                notFound.Add(label);
                logger.LogInformation("No LRCLIB match for {Track}", label);
                break;

            case LyricsLookupStatus.Found:
                {
                    var parsed = LyricsText.Parse(result.Text);

                    if (parsed.IsEmpty)
                    {
                        notFound.Add(label);
                        logger.LogInformation("LRCLIB returned an unusable text for {Track}", label);
                        break;
                    }

                    db.TrackLyrics.Add(new TrackLyrics
                    {
                        TrackId = track.Id,
                        Plain = parsed.Plain,
                        Synced = parsed.Lines,
                        Source = LyricsSource.Provider,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    });

                    await db.SaveChangesAsync(ct);

                    if (parsed.Lines.Count > 0)
                        synced++;
                    else
                        plain++;

                    logger.LogInformation(
                        "Saved {Kind} lyrics for {Track}", parsed.Lines.Count > 0 ? "synced" : "plain", label);
                    break;
                }
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        break;
    }
    catch (Exception ex)
    {
        errored.Add(label);
        logger.LogWarning(ex, "Failed to fetch lyrics for {Track}", label);
    }

    await Task.Delay(lrclibOptions.RequestDelayMs, ct);
}

logger.LogInformation(
    "Done. Synced {Synced}, plain {Plain}, instrumental {Instrumental}, not found {NotFound}, errored {Errored}",
    synced, plain, instrumental.Count, notFound.Count, errored.Count);

PrintNames("Instrumental on LRCLIB", instrumental);
PrintNames("Not found on LRCLIB", notFound);
PrintNames("Errored", errored);

return;

static int? ParseLimitArg(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--limit" && int.TryParse(args[i + 1], out var value))
            return value;
    }

    return null;
}

static string UserAgent()
{
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    return $"Caimack/{version} (+https://github.com/BulatRuslanovich/music-streaming)";
}

static void PrintNames(string title, List<string> names)
{
    if (names.Count == 0)
        return;

    Console.WriteLine();
    Console.WriteLine($"{title} ({names.Count}):");
    foreach (var name in names)
        Console.WriteLine($"  - {name}");
}

static async Task<LyricsLookupResult> LookupWithRetryAsync(
    LrclibClient client, LyricsQuery query, string label, ILogger logger, CancellationToken ct)
{
    try
    {
        return await client.LookupAsync(query, ct);
    }
    catch (HttpRequestException ex)
    {
        logger.LogWarning(ex, "LRCLIB lookup failed for {Track}, retrying once", label);
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        return await client.LookupAsync(query, ct);
    }
}
