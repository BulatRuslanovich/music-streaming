using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Infrastructure.Imaging;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Infrastructure.Storage;
using MusicStreaming.Tools.ArtistImages;

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

builder.Services.AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "Storage:RootPath is required.");

builder.Services.AddOptions<TheAudioDbOptions>()
    .Bind(builder.Configuration.GetSection(TheAudioDbOptions.SectionName));

builder.Services.AddSingleton<IMusicStorage, FileSystemMusicStorage>();
builder.Services.AddSingleton<IImageProcessor, ImageSharpImageProcessor>();
builder.Services.AddHttpClient<TheAudioDbClient>();
builder.Services.AddHttpClient("images");

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var services = scope.ServiceProvider;

var logger = services.GetRequiredService<ILogger<Program>>();
var db = services.GetRequiredService<ApplicationDbContext>();
var storage = services.GetRequiredService<IMusicStorage>();
var imageProcessor = services.GetRequiredService<IImageProcessor>();
var audioDb = services.GetRequiredService<TheAudioDbClient>();
var audioDbOptions = services.GetRequiredService<IOptions<TheAudioDbOptions>>().Value;
var downloadClient = services.GetRequiredService<IHttpClientFactory>().CreateClient("images");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
var ct = cts.Token;

IQueryable<Artist> query = db.Artists.Where(a => a.ImagePath == null).OrderBy(a => a.Name);
if (limit is not null)
    query = query.Take(limit.Value);

var artists = await query.ToListAsync(ct);
logger.LogInformation("Found {Count} artists without a photo", artists.Count);

var saved = 0;
var notFound = new List<string>();
var ambiguous = new List<string>();
var errored = new List<string>();

foreach (var artist in artists)
{
    if (ct.IsCancellationRequested)
        break;

    try
    {
        var result = await LookupWithRetryAsync(audioDb, artist.Name, logger, ct);

        switch (result.Status)
        {
            case ArtistLookupStatus.NotFound:
                notFound.Add(artist.Name);
                logger.LogInformation("No TheAudioDB match for {Artist}", artist.Name);
                break;

            case ArtistLookupStatus.Ambiguous:
                ambiguous.Add(artist.Name);
                logger.LogInformation("Ambiguous TheAudioDB matches for {Artist}", artist.Name);
                break;

            case ArtistLookupStatus.Found:
            {
                var bytes = await downloadClient.GetByteArrayAsync(result.ImageUrl, ct);
                await using var content = new MemoryStream(bytes);
                var webp = await imageProcessor.ToSquareWebpAsync(content, ImageUpload.Edge, ct);

                artist.ImagePath = await storage.SaveArtistImageAsync(artist.Id, webp, ct);
                await db.SaveChangesAsync(ct);

                saved++;
                logger.LogInformation("Saved photo for {Artist}", artist.Name);
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
        errored.Add(artist.Name);
        logger.LogWarning(ex, "Failed to fetch a photo for {Artist}", artist.Name);
    }

    await Task.Delay(audioDbOptions.RequestDelayMs, ct);
}

logger.LogInformation(
    "Done. Saved {Saved}, not found {NotFound}, ambiguous {Ambiguous}, errored {Errored}",
    saved, notFound.Count, ambiguous.Count, errored.Count);

PrintNames("Not found on TheAudioDB", notFound);
PrintNames("Ambiguous matches", ambiguous);
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

static void PrintNames(string title, List<string> names)
{
    if (names.Count == 0)
        return;

    Console.WriteLine();
    Console.WriteLine($"{title} ({names.Count}):");
    foreach (var name in names)
        Console.WriteLine($"  - {name}");
}

static async Task<ArtistLookupResult> LookupWithRetryAsync(
    TheAudioDbClient client, string artistName, ILogger logger, CancellationToken ct)
{
    try
    {
        return await client.LookupAsync(artistName, ct);
    }
    catch (HttpRequestException ex)
    {
        logger.LogWarning(ex, "TheAudioDB lookup failed for {Artist}, retrying once", artistName);
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        return await client.LookupAsync(artistName, ct);
    }
}
