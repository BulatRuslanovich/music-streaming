// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using MusicStreaming.Application.Abstractions;
using Scalar.AspNetCore;

namespace MusicStreaming.Api.Startup;

public static class OpenApiSetup
{
    public const string DocumentPath = "/openapi/{documentName}.json";

    private const string DocumentName = "v1";

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
        app.MapOpenApi(DocumentPath).AllowAnonymous();

        app.MapScalarApiReference("/docs", options => options
                .WithTitle("Music Streaming API")
                .WithOpenApiRoutePattern(DocumentPath)
                .DisableDefaultFonts()
                .DisableTelemetry())
            .AllowAnonymous();

        return app;
    }

    private static Task DescribeBearerAuth(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "Music Streaming API";
        document.Info.Version = typeof(OpenApiSetup).Assembly.GetName().Version?.ToString(3) ?? DocumentName;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
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
                [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
            },
        ];

        return Task.CompletedTask;
    }

    private static Task DescribeAuthFailures(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any())
            return Task.CompletedTask;

        AddResponse(operation, StatusCodes.Status401Unauthorized, "Нет действующего токена доступа.");

        var needsAdmin = metadata.OfType<IAuthorizeData>()
            .Any(data => data.Policy == "Admin" || data.Roles?.Contains("Admin") == true);

        if (needsAdmin)
            AddResponse(operation, StatusCodes.Status403Forbidden, "Требуются права администратора.");

        return Task.CompletedTask;
    }

    private static void AddResponse(OpenApiOperation operation, int statusCode, string description)
    {
        operation.Responses ??= [];

        var key = statusCode.ToString(CultureInfo.InvariantCulture);
        if (operation.Responses.ContainsKey(key))
            return;

        operation.Responses[key] = new OpenApiResponse { Description = description };
    }
}
