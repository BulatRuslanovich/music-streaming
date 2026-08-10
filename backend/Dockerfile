# ==============================================================================================
# ASP.NET Core API — multi-stage build
# ==============================================================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore against the project files alone, so the restore layer is only invalidated when a
# dependency actually changes rather than on every source edit.
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

# TagLib# reads tags in managed code, so no native audio libraries are needed. ICU is skipped
# because the app runs in invariant globalization mode.
# The base image already sets HTTP_PORTS=8080; setting ASPNETCORE_URLS as well would only
# override it and log a warning about the conflict.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_gcServer=0

# Npgsql probes for GSSAPI/Kerberos when opening a connection; without these libraries present it
# logs a shared-library error on every start even though password authentication works fine.
RUN apk add --no-cache krb5-libs

COPY --from=build /app ./

# Run unprivileged, and make sure the storage mount is owned by that user so uploads can be
# written without granting the container root.
RUN addgroup -S -g 1001 music \
    && adduser -S -u 1001 -G music music \
    && mkdir -p /storage/music /storage/covers \
    && chown -R music:music /storage /app

USER music
EXPOSE 8080

# The API answers /health as soon as migrations have completed, which is what the compose
# dependency conditions wait on.
HEALTHCHECK --interval=15s --timeout=4s --start-period=40s --retries=5 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "MusicStreaming.Api.dll"]
