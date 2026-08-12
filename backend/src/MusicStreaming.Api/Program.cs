using System.Text.Json;
using System.Text.Json.Serialization;
using MusicStreaming.Api.Middleware;
using MusicStreaming.Api.Startup;
using MusicStreaming.Application;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Infrastructure;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Infrastructure.Security;

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

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddApiRateLimiting();
builder.Services.AddApiForwardedHeaders(builder.Configuration);
builder.Services.AddApiCors(builder.Configuration);
builder.AddApiUploadLimits();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseApiRequestLogging();

if (app.Environment.IsDevelopment())
    app.UseCors(RequestPipelineSetup.DevCorsPolicy);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

app.Run();
