using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using MusicStreaming.Application.Abstractions;
using Scalar.AspNetCore;

namespace MusicStreaming.Api.Startup;

/// <summary>
/// Схема API и читалка к ней: страница, где видно все ручки и с которой их можно позвать руками.
/// </summary>
public static class OpenApiSetup
{
    /// <summary>
    /// Где лежит сам документ и где живёт читалка.
    ///
    /// <para>
    /// Оба пути намеренно вне <c>/api</c> — по той же причине, что и <see cref="MetricsSetup.ScrapePath"/>:
    /// снаружи Caddy отдаёт бэкенду только <c>/api/*</c> и <c>/health</c>, а всё прочее уводит на
    /// фронтенд. Значит, ни схема, ни читалка через публичный домен не открываются вовсе, и
    /// закрывать их отдельно не нужно — достаточно не выставлять наружу порт.
    /// </para>
    /// </summary>
    public const string DocumentPath = "/openapi/{documentName}.json";

    public const string DocsPath = "/docs";

    private const string DocumentName = "v1";

    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer(DescribeBearerAuth);
            options.AddOperationTransformer(DescribeAuthFailures);
        });

        return services;
    }

    public static WebApplication MapApiOpenApi(this WebApplication app)
    {
        // Анонимные, потому что политика по умолчанию требует входа (см. AuthenticationSetup), а
        // читалка загружается до того, как у неё появится токен. Наружу они не выставлены — см.
        // DocumentPath.
        app.MapOpenApi(DocumentPath).AllowAnonymous();

        app.MapScalarApiReference(DocsPath, options => options
                .WithTitle("Music Streaming API")
                .WithOpenApiRoutePattern(DocumentPath)
                // Шрифты и статистика ходят на чужие CDN. Первое ломает страницу там, где наружу
                // нет выхода, второе не нужно вовсе — по тем же соображениям, по каким выключена
                // отправка отчётов у Grafana.
                .DisableDefaultFonts()
                .DisableTelemetry())
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Объявляет, что ручки закрыты токеном, — иначе в читалке нет кнопки «Authorize» и любой
    /// вызов из неё возвращает 401.
    ///
    /// <para>
    /// Токен можно и не вставлять руками: <c>/api/auth/login</c> кладёт его в cookie, а бэкенд
    /// читает её наравне с заголовком, так что после входа прямо со страницы остальные вызовы
    /// уходят уже от вошедшего.
    /// </para>
    /// </summary>
    private static Task DescribeBearerAuth(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "Music Streaming API";
        document.Info.Version = typeof(OpenApiSetup).Assembly.GetName().Version?.ToString(3) ?? DocumentName;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Токен из ответа /api/auth/login.",
        };

        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [],
            },
        ];

        return Task.CompletedTask;
    }

    /// <summary>
    /// Дописывает каждой ручке отказы аутентификации и авторизации.
    ///
    /// <para>
    /// Выводится из метаданных эндпоинта, а не проставляется атрибутами на сотне действий: политика
    /// по умолчанию требует входа (см. <c>AuthenticationSetup</c>), поэтому 401 возможен почти
    /// везде, и повторять это руками значило бы завести сотню мест, которые разойдутся с
    /// действительностью при первой же правке политики. Здесь же ответ схемы меняется вместе с
    /// самим правилом.
    /// </para>
    /// </summary>
    private static Task DescribeAuthFailures(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any())
            return Task.CompletedTask;

        AddResponse(operation, StatusCodes.Status401Unauthorized, "Нет действующего токена доступа.");

        // Единственная политика с ролью — админская; всё, что ею закрыто, обычному пользователю
        // отвечает 403.
        var needsAdmin = metadata.OfType<IAuthorizeData>()
            .Any(data => data.Policy == AppPolicies.Admin || data.Roles?.Contains(AppRoles.Admin) == true);

        if (needsAdmin)
            AddResponse(operation, StatusCodes.Status403Forbidden, "Требуются права администратора.");

        return Task.CompletedTask;
    }

    /// <summary>Добавляет ответ, не затирая уже описанный атрибутом на самом действии.</summary>
    private static void AddResponse(OpenApiOperation operation, int statusCode, string description)
    {
        operation.Responses ??= new OpenApiResponses();

        var key = statusCode.ToString(CultureInfo.InvariantCulture);
        if (operation.Responses.ContainsKey(key))
            return;

        operation.Responses[key] = new OpenApiResponse { Description = description };
    }
}
