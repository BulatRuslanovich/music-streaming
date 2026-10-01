// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Middleware;
using Api.Startup;
using App;
using App.Abstractions;
using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Security;

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
    });

builder.Services.AddHealthChecks();

builder.Services.AddApiOpenApi();

builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddApiForwardedHeaders(builder.Configuration);
builder.AddApiUploadLimits();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseApiRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<JsonETagMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapApiOpenApi();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

app.Run();
