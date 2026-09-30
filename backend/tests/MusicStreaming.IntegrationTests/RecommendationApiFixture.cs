// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Services;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Audio;
using MusicStreaming.Infrastructure.Integrations;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Infrastructure.Recommendations;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicStreaming.IntegrationTests;

public sealed class RecommendationApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = BuildDatabase();

    private static PostgreSqlContainer BuildDatabase()
    {
        var builder = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("music")
            .WithUsername("music")
            .WithPassword("integration-tests");

        var scripts = Directory
            .EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "db-init"), "*.sql")
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (var script in scripts)
            builder = builder.WithResourceMapping(new FileInfo(script), "/docker-entrypoint-initdb.d/");

        return builder.Build();
    }

    private string _storagePath = string.Empty;

    public string StoragePath => _storagePath;

    public bool DockerAvailable { get; private set; }

    public string SkipReason { get; private set; } =
        "Docker is not available, so the integration database cannot start.";

    public const string OwnerUsername = "owner";
    public const string OwnerPassword = "integration-password";

    public FixtureClock Clock { get; } = new();

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _postgres.StartAsync();
            DockerAvailable = true;
        }
        catch (Exception ex)
        {
            DockerAvailable = false;
            SkipReason = $"The integration database could not start: {ex.Message}";
            return;
        }

        _storagePath = Path.Combine(Path.GetTempPath(), $"caimack-tests-{Guid.CreateVersion7()}");
        Directory.CreateDirectory(_storagePath);

        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);

        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-that-is-long-enough-32");
        builder.UseSetting("Owner:Username", OwnerUsername);
        builder.UseSetting("Owner:Password", OwnerPassword);
        builder.UseSetting("Storage:RootPath", _storagePath);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);

            Type[] backgroundWorkers =
            [
                typeof(TranscodeWorker), typeof(TranscodeBackfillService), typeof(AudioEmbeddingWorker),
                typeof(RecommendationWorker), typeof(LibraryMaintenanceWorker), typeof(EmbeddingIndexLoader),
                typeof(LibraryEnrichmentWorker),
            ];

            var removed = services
                .Where(descriptor => backgroundWorkers.Contains(descriptor.ImplementationType))
                .ToList();
            foreach (var worker in removed)
                services.Remove(worker);

            services.AddSingleton(new LoginAttemptTracker(Clock, lockoutAttempts: 0));
        });
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public HttpClient CreateAnonymousClient() => CreateCookieClient();

    private HttpClient? _signedIn;

    public async Task<HttpClient> CreateSignedInClientAsync()
    {
        if (_signedIn is not null)
            return _signedIn;

        var client = CreateCookieClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { username = OwnerUsername, password = OwnerPassword });

        response.EnsureSuccessStatusCode();

        return _signedIn = client;
    }

    private readonly Dictionary<string, HttpClient> _others = [];

    public async Task<HttpClient> CreateSignedInClientAsync(string username, string password)
    {
        if (_others.TryGetValue(username, out var existing))
            return existing;

        var owner = await CreateSignedInClientAsync();
        var created = await owner.PostAsJsonAsync(
            "/api/admin/users",
            new { username, password, isAdmin = false });

        created.EnsureSuccessStatusCode();

        var client = CreateCookieClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();

        return _others[username] = client;
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    public async Task<(SeededLibrary Library, HttpClient Client)> SeedAndSignInAsync(
        int artistCount = 4,
        int tracksPerArtist = 5)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var library = await LibrarySeeder.SeedAsync(db, artistCount, tracksPerArtist);

        await ReloadEmbeddingIndexAsync();

        return (library, await CreateSignedInClientAsync());
    }

    public Task ReloadEmbeddingIndexAsync() =>
        new IndexLoader(Services.GetRequiredService<IServiceScopeFactory>(), Services.GetRequiredService<EmbeddingIndex>(), Clock)
            .LoadAsync();

    private sealed class IndexLoader(IServiceScopeFactory scopes, EmbeddingIndex index, TimeProvider clock)
        : EmbeddingIndexLoader(scopes, index, clock, NullLogger<EmbeddingIndexLoader>.Instance)
    {
        public Task LoadAsync() => RunPassAsync(Cancel.Token);
    }

    public async Task EmbedLibraryAsync()
    {
        const int Dimension = 32;

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trackIds = await db.Tracks.Where(track => track.Embedding == null).Select(track => track.Id)
                .ToListAsync(Cancel.Token);

            var random = new Random(20260929);

            foreach (var trackId in trackIds)
            {
                var vector = Enumerable.Range(0, Dimension).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();

                VectorMath.NormalizeInPlace(vector);

                db.TrackEmbeddings.Add(new TrackEmbedding
                {
                    TrackId = trackId,
                    Vector = vector,
                    Dimension = Dimension,
                    ModelId = "test",
                    Strategy = "test",
                    Succeeded = true,
                    AnalyzedAt = Clock.GetUtcNow(),
                });
            }

            await db.SaveChangesAsync(Cancel.Token);
        }

        await ReloadEmbeddingIndexAsync();
    }

    public async Task BuildRecommendationsAsync(Guid userId)
    {
        using var scope = CreateScope();
        var provider = scope.ServiceProvider;

        await provider.GetRequiredService<ProfileRollupService>().RollupAsync(userId);
        await provider.GetRequiredService<LibraryMaintenance>().RefreshTrackStatsAsync();
        await provider.GetRequiredService<ShelfGenerationService>()
            .GenerateAsync(userId);
    }

    public async Task<T> AsListenerAsync<T>(Guid userId, Func<RecommendationService, Task<T>> read)
    {
        using var scope = CreateScope();

        var recommendations = ActivatorUtilities.CreateInstance<RecommendationService>(
            scope.ServiceProvider, new FixtureListener(userId));

        return await read(recommendations);
    }

    public Task<RecommendationHomeDto> HomeAsync(
        Guid userId, int sectionSize = 12, bool includeScores = false) =>
        AsListenerAsync(userId, rec => rec.GetHomeAsync(sectionSize, includeScores, ct: Cancel.Token));

    private sealed record FixtureListener(Guid Id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }

    private HttpClient CreateCookieClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
    });

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (DockerAvailable)
            await _postgres.DisposeAsync();

        if (Directory.Exists(_storagePath))
            Directory.Delete(_storagePath, recursive: true);
    }
}

[CollectionDefinition(nameof(RecommendationApiCollection))]
public class RecommendationApiCollection : ICollectionFixture<RecommendationApiFixture>;
