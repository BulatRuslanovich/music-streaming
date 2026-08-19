// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MusicStreaming.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicStreaming.IntegrationTests;

public sealed class RecommendationApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("music")
        .WithUsername("music")
        .WithPassword("integration-tests")
        .Build();

    private string _storagePath = string.Empty;

    public bool DockerAvailable { get; private set; }

    // Причина пропуска несёт саму ошибку. Раньше здесь была общая фраза про недоступный Docker, и
    // когда контейнер переставал стартовать по любой другой причине — скажем, реестр отвергал
    // просроченные учётные данные из ~/.docker/config.json, — полторы сотни тестов молча
    // превращались в пропущенные, а разбираться приходилось вслепую.
    public string SkipReason { get; private set; } =
        "Docker is not available, so the integration database cannot start.";

    public const string OwnerUsername = "owner";
    public const string OwnerPassword = "integration-password";

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

        builder.UseSetting("Recommendations:Enabled", "false");
        builder.UseSetting("Transcode:Enabled", "false");

        builder.UseSetting("Security:LoginAttemptsPerMinute", "1000");
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public HttpClient CreateAnonymousClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
    });

    private HttpClient? _signedIn;

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

    private readonly Dictionary<string, HttpClient> _others = [];

    public async Task<HttpClient> CreateSignedInClientAsync(string username, string password)
    {
        if (_others.TryGetValue(username, out var existing))
            return existing;

        var owner = await CreateSignedInClientAsync();
        var created = await owner.PostAsJsonAsync(
            "/api/admin/users",
            new { username, password, displayName = username, isAdmin = false });

        created.EnsureSuccessStatusCode();

        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();

        return _others[username] = client;
    }

    public IServiceScope CreateScope() => Services.CreateScope();

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
