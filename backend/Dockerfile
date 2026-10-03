FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY MusicStreaming.slnx ./
COPY src/Domain/Domain.csproj src/Domain/
COPY src/App/App.csproj src/App/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj

COPY src/ src/

RUN dotnet publish src/Api/Api.csproj \
    -c Release \
    -o /app \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_gcServer=0

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg tzdata libkrb5-3 wget \
    && rm -rf /var/lib/apt/lists/* \
    && test -f /usr/share/zoneinfo/Europe/Moscow

RUN groupadd -r -g 1001 music \
    && useradd -r -u 1001 -g music music \
    && mkdir -p /storage/music /storage/covers \
    && chown -R music:music /storage

COPY --from=build --chown=music:music /app ./

USER music
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=4s --start-period=40s --retries=5 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "Api.dll"]
