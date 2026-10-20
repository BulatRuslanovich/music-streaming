// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Middleware;
using Api.Startup;
using App;
using App.Abstractions;
using App.Common;
using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

LocalizationSetup.UseInvariantFormats();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseApiSerilog();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser>(sp =>
    new ClaimsPrincipalCurrentUser(sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // Невалидный запрос — тот же ответ ASP.NET, но заголовок на языке клиента.
        var standard = options.InvalidModelStateResponseFactory;
        options.InvalidModelStateResponseFactory = context =>
        {
            var result = standard(context);
            if (result is ObjectResult { Value: ProblemDetails problem })
                problem.Title = ErrorText.Localize("One or more validation errors occurred.");
            return result;
        };
    });

builder.Services.AddHealthChecks();

builder.Services.AddApiOpenApi();

builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddApiForwardedHeaders(builder.Configuration);
builder.AddApiUploadLimits();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseApiRequestLogging();
app.UseApiLocalization();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapApiOpenApi();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

app.Run();
