using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<AuthService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<LibraryService>();
        services.AddScoped<TrackUploadService>();
        services.AddScoped<PlaylistService>();
        services.AddScoped<FavoriteService>();
        services.AddScoped<HistoryService>();
        services.AddScoped<SearchService>();
        services.AddScoped<StreamingService>();

        return services;
    }
}
