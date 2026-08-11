using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
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

string[] DefaultTrustedNetworks = ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .WriteTo.Console(
        outputTemplate:
            "[{Timestamp:HH:mm:ss}] " +
            "{Level:u3} " +
            "{Message:lj} " +
            "{NewLine}" +
            "{Exception}"
    ));


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
            RoleClaimType = AppClaims.Role,
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


builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy(AppPolicies.Admin, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(AppRoles.Admin));


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


var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
                     ?? new StorageOptions();

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = storageOptions.MaxUploadBytes * 20;
    options.ValueLengthLimit = int.MaxValue;
});


builder.Services
    .AddDataProtection()
    .SetApplicationName("music-streaming")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storageOptions.RootPath, ".dataprotection")));

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = storageOptions.MaxUploadBytes * 20;
    options.Limits.MinRequestBodyDataRate = null;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks")
                 .Get<string[]>() ?? DefaultTrustedNetworks)
    {
        var parts = network.Split('/', 2);
        if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix)
                              && int.TryParse(parts[1], out var length))
        {
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, length));
        }
    }
});


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
        ex is not null
            ? LogEventLevel.Error
            : httpContext.Response.StatusCode >= 500
                ? LogEventLevel.Error
                : httpContext.Request.Path.StartsWithSegments("/api/tracks") &&
                  httpContext.Request.Headers.ContainsKey("Range")
                    ? LogEventLevel.Debug
                    : LogEventLevel.Information;

    options.MessageTemplate =
        "{RequestMethod} {RequestPath} → {StatusCode} ({Elapsed:0.0} ms)";
});

if (app.Environment.IsDevelopment())
    app.UseCors(DevCorsPolicy);

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
