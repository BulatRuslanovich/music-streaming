using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MusicStreaming.Api.Auth;
using MusicStreaming.Api.Middleware;
using MusicStreaming.Application;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Infrastructure;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Infrastructure.Security;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Structured logging
// ---------------------------------------------------------------------------------------------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

// ---------------------------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------------------------
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

builder.Services.AddHttpContextAccessor();

// The current user is derived from the request's claims, so every service that needs the caller's
// id gets it without threading it through every method signature.
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

// ---------------------------------------------------------------------------------------------
// Authentication
//
// Bearer tokens are the primary scheme; when no Authorization header is present the token is read
// from the HttpOnly cookie instead. That fallback is what lets an <audio> element stream a
// protected track, since a media request cannot carry a custom header.
// ---------------------------------------------------------------------------------------------
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = JwtTokenService.BuildSigningKey(jwtOptions),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = AppClaims.Username,
            RoleClaimType = ClaimTypes.Role,
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token) &&
                    context.Request.Cookies.TryGetValue(AuthCookies.AccessTokenCookie, out var cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            },
        };
    });

// Nothing in this application is public, so authentication is the default and the few open
// endpoints (login, refresh, health) opt out with [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// ---------------------------------------------------------------------------------------------
// Rate limiting: slows down password guessing without getting in the way of normal use.
// ---------------------------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// ---------------------------------------------------------------------------------------------
// Uploads: the request body limit is aligned with the configured per-file ceiling, with headroom
// for multipart overhead and a handful of files in one request.
// ---------------------------------------------------------------------------------------------
var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
                     ?? new StorageOptions();

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = storageOptions.MaxUploadBytes * 20;
    options.ValueLengthLimit = int.MaxValue;
});

// Data Protection is not used for the auth tokens (those are signed with the configured JWT key),
// but keys are persisted anyway so that anything relying on it later survives a container rebuild
// instead of silently invalidating on every restart.
builder.Services
    .AddDataProtection()
    .SetApplicationName("music-streaming")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storageOptions.RootPath, ".dataprotection")));

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = storageOptions.MaxUploadBytes * 20;
    // A large library upload over a slow link must not be cut off by the default rate minimum.
    options.Limits.MinRequestBodyDataRate = null;
});

// Behind Caddy and Cloudflare, the real scheme and client address arrive in forwarded headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// In development the Next.js dev server runs on its own origin, so it needs explicit CORS.
// In production everything is same-origin behind the reverse proxy and no policy is applied.
const string DevCorsPolicy = "dev-frontend";
builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                 ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, elapsed, ex) =>
        ex is not null ? LogEventLevel.Error
        : httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error
        // Range requests during playback are high-volume and uninteresting individually.
        : httpContext.Request.Path.StartsWithSegments("/api/tracks") &&
          httpContext.Request.Headers.ContainsKey("Range") ? LogEventLevel.Debug
        : LogEventLevel.Information;
});

if (app.Environment.IsDevelopment())
    app.UseCors(DevCorsPolicy);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

// ---------------------------------------------------------------------------------------------
// Startup: migrate the schema and make sure the personal account exists.
// ---------------------------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

app.Run();
