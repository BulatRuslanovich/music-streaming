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

/// <summary>
/// Поднимает настоящее приложение поверх настоящего PostgreSQL, потому что большая часть работы
/// движка рекомендаций — это SQL. Провайдер в памяти не задействовал бы ни оконных функций, ни
/// соединений по совстречаемости, ни колонок jsonb, на которых движок построен, поэтому прошедший
/// на нём тест не сказал бы ничего о работоспособности функции.
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

    /// <summary>Docker есть не везде; без него набор пропускается, а не падает.</summary>
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

        // Заставляет хост собраться и прогнать миграции сейчас, чтобы за это не платил первый тест.
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

        // Периодические воркеры выключены, чтобы тесты запускали роллап, генерацию и обслуживание
        // явно. Приём событий продолжает работать: он и сам входит в проверяемое, и он единственный
        // писатель журнала событий.
        builder.UseSetting("Recommendations:Enabled", "false");
        builder.UseSetting("Transcode:Enabled", "false");

        // Все запросы набора приходят с одного (пустого) адреса, поэтому боевые десять попыток в
        // минуту делят на всех и тесты про сессии упирались бы в 429 вместо своих проверок.
        builder.UseSetting("Security:LoginAttemptsPerMinute", "1000");
    }

    /// <summary>
    /// Как отвечает API: перечисления строками, свойства в camelCase. Клиент тестов обязан читать
    /// ответ ровно так же, иначе он проверяет не контракт, а собственные настройки разбора.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Клиент без сессии — для тестов, которым нужно войти самим и посмотреть, что из этого выйдет.</summary>
    public HttpClient CreateAnonymousClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
    });

    private HttpClient? _signedIn;

    /// <summary>
    /// Клиент, уже вошедший как владелец: каждый эндпойнт требует сессии.
    ///
    /// <para>
    /// Адресуется по https, хотя тестовый сервер не занимается TLS. Вне разработки приложение
    /// помечает свои куки аутентификации как <c>Secure</c>, а контейнер кук не отправит такие обратно
    /// на источник по обычному http; запрос пришёл бы анонимным, и каждый тест падал бы на
    /// авторизации, а не на том, что он на самом деле проверяет.
    /// </para>
    ///
    /// <para>
    /// Общий на весь набор и входит ровно один раз. Вход ограничен десятью попытками в минуту с
    /// адреса — защита, которую стоит сохранить, — и набор, входящий на каждый тест, упирался бы в
    /// неё и падал на 429 вместо собственных проверок.
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

    private readonly Dictionary<string, HttpClient> _others = [];

    /// <summary>
    /// Клиент, вошедший как обычный (не владелец) пользователь: без второго пользователя не проверить
    /// ни того, что чужой приватный плейлист не виден, ни того, что публичный виден. Учётная запись
    /// заводится владельцем через админский эндпойнт и, как и клиент владельца, входит один раз —
    /// вход ограничен по частоте, и набор, входящий на каждый тест, упирался бы в это ограничение.
    /// </summary>
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
