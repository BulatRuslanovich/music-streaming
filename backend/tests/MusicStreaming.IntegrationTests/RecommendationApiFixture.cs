using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MusicStreaming.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Hosts the real application against a real PostgreSQL, because most of what the recommendation
/// engine does is SQL. An in-memory provider would exercise none of the window functions,
/// co-occurrence joins or jsonb columns the engine is built on, so a test that passed against one
/// would say nothing about whether the feature works.
/// </summary>
public sealed class RecommendationApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("music")
        .WithUsername("music")
        .WithPassword("integration-tests")
        .Build();

    private string _storagePath = string.Empty;

    /// <summary>Docker is not available everywhere; the suite skips rather than fails without it.</summary>
    public bool DockerAvailable { get; private set; }

    public string SkipReason => "Docker is not available, so the integration database cannot start.";

    public const string OwnerUsername = "owner";
    public const string OwnerPassword = "integration-password";

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _postgres.StartAsync();
            DockerAvailable = true;
        }
        catch (Exception)
        {
            DockerAvailable = false;
            return;
        }

        _storagePath = Path.Combine(Path.GetTempPath(), $"caimack-tests-{Guid.CreateVersion7()}");
        Directory.CreateDirectory(_storagePath);

        // Forces the host to build and run migrations now, so the first test does not pay for it.
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

        // The periodic workers are switched off so that tests drive rollup, generation and
        // maintenance explicitly. Event ingestion keeps running — it is part of what is under
        // test, and it is the only writer of the event log.
        builder.UseSetting("Recommendations:Enabled", "false");
        builder.UseSetting("Transcode:Enabled", "false");
    }

    private HttpClient? _signedIn;

    /// <summary>
    /// A client already signed in as the owner — every endpoint requires a session.
    ///
    /// <para>
    /// Addressed over https even though the test server does no TLS. The application marks its
    /// auth cookies <c>Secure</c> outside development, and a cookie container will not send those
    /// back over a plain-http origin; the request would arrive anonymous and every test would fail
    /// on authorisation rather than on what it is actually checking.
    /// </para>
    ///
    /// <para>
    /// Shared across the suite, and signed in exactly once. Sign-in is rate limited to ten
    /// attempts a minute per address — protection worth keeping — and a suite that logged in per
    /// test would trip it and fail on 429 instead of on its own assertions.
    /// </para>
    /// </summary>
    public async Task<HttpClient> CreateSignedInClientAsync()
    {
        if (_signedIn is not null)
            return _signedIn;

        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { username = OwnerUsername, password = OwnerPassword });

        response.EnsureSuccessStatusCode();

        return _signedIn = client;
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    public async ValueTask DisposeAsync()
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
