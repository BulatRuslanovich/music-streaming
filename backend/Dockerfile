# ==============================================================================================
# ASP.NET Core API — multi-stage build
# ==============================================================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY MusicStreaming.slnx ./
COPY src/MusicStreaming.Domain/MusicStreaming.Domain.csproj src/MusicStreaming.Domain/
COPY src/MusicStreaming.Application/MusicStreaming.Application.csproj src/MusicStreaming.Application/
COPY src/MusicStreaming.Infrastructure/MusicStreaming.Infrastructure.csproj src/MusicStreaming.Infrastructure/
COPY src/MusicStreaming.Api/MusicStreaming.Api.csproj src/MusicStreaming.Api/
RUN dotnet restore src/MusicStreaming.Api/MusicStreaming.Api.csproj

COPY src/ src/
RUN dotnet publish src/MusicStreaming.Api/MusicStreaming.Api.csproj \
    -c Release \
    -o /app \
    --no-restore

# ----------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_gcServer=0

RUN apk add --no-cache krb5-libs ffmpeg

COPY --from=build /app ./

RUN addgroup -S -g 1001 music \
    && adduser -S -u 1001 -G music music \
    && mkdir -p /storage/music /storage/covers \
    && chown -R music:music /storage /app

USER music
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=4s --start-period=40s --retries=5 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "MusicStreaming.Api.dll"]
