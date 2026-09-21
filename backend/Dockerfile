FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY MusicStreaming.slnx ./
COPY src/MusicStreaming.Domain/MusicStreaming.Domain.csproj src/MusicStreaming.Domain/
COPY src/MusicStreaming.Application/MusicStreaming.Application.csproj src/MusicStreaming.Application/
COPY src/MusicStreaming.Infrastructure/MusicStreaming.Infrastructure.csproj src/MusicStreaming.Infrastructure/
COPY src/MusicStreaming.Api/MusicStreaming.Api.csproj src/MusicStreaming.Api/
RUN dotnet restore src/MusicStreaming.Api/MusicStreaming.Api.csproj

COPY src/ src/

ARG GIT_SHA=""
RUN dotnet publish src/MusicStreaming.Api/MusicStreaming.Api.csproj \
    -c Release \
    -o /app \
    --no-restore \
    -p:SourceRevisionId=$GIT_SHA

# glibc, не Alpine: ONNX Runtime поставляет нативы только под glibc, musl-RID в пакете нет,
# и CLAP-эмбеддер на musl падает при загрузке libonnxruntime.so. Образ тяжелее примерно на 90 МБ.
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

# --chown прямо в COPY: отдельный chown -R поверх скопированного каталога переписал бы
# все его файлы в новый слой и удвоил образ на размер приложения.
COPY --from=build --chown=music:music /app ./

USER music
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=4s --start-period=40s --retries=5 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "MusicStreaming.Api.dll"]
