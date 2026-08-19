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

# .git в контекст сборки не попадает, поэтому хеш коммита приходит снаружи — без него SDK просто
# не допишет "+<sha>" к InformationalVersion, и интерфейс покажет версию без коммита.
ARG GIT_SHA=""
RUN dotnet publish src/MusicStreaming.Api/MusicStreaming.Api.csproj \
    -c Release \
    -o /app \
    --no-restore \
    -p:SourceRevisionId=$GIT_SHA

# ----------------------------------------------------------------------------------------------
# Разовые инструменты обслуживания — отдельной целью, а не внутри образа API.
#
# Запускаются они вручную и раз в жизни, а образ API работает круглосуточно: класть в него то, что
# почти никогда не исполняется, значит расширять поверхность без выгоды. Цель собирается только по
# явному --target tools, поэтому обычный build API её не касается.
#
# Инструменты живут в одном образе: различаются они парой сотен килобайт своего кода на общей базе
# из Infrastructure и рантайма, так что отдельный образ на каждый — это второй мегабайтный слепок
# ради разного имени. Кого запускать, решает entrypoint конкретного сервиса в compose.
FROM build AS tools-build
RUN dotnet publish src/MusicStreaming.Tools.ArtistImages/MusicStreaming.Tools.ArtistImages.csproj \
    -c Release \
    -o /tools
RUN dotnet publish src/MusicStreaming.Tools.Lyrics/MusicStreaming.Tools.Lyrics.csproj \
    -c Release \
    -o /tools

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS tools
WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

# Npgsql тянет krb5 даже там, где Kerberos не используется.
RUN apk add --no-cache krb5-libs

COPY --from=tools-build /tools ./

# Тот же непривилегированный пользователь, что и в образе API: compose всё равно переопределит его
# на PUID/PGID, но запуск без -u не должен оставлять в хранилище файлы, принадлежащие root.
RUN addgroup -S -g 1001 music \
    && adduser -S -u 1001 -G music music

USER music
ENTRYPOINT ["dotnet"]

# ----------------------------------------------------------------------------------------------
# Последней целью остаётся API: без --target собирается именно она.
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
