# 13. Сборка и эксплуатация

## Образ

[`backend/Dockerfile`](../../backend/Dockerfile) — три стадии.

### `build` — `sdk:10.0-alpine`

Сначала копируются **только** `.csproj`, `Directory.Build.props` и `.slnx`, затем идёт `restore`, и
лишь потом копируется `src/`. Это классический и очень хитрый приём кэширования слоёв: правка кода не приводит к
повторному восстановлению пакетов. 

### `tools` — `runtime:10.0-alpine`

Утилита изображений исполнителей, **отдельной целью**. Она запускается вручную и раз в жизни, а образ
API работает круглосуточно: класть в него то, что почти никогда не исполняется, значит расширять
поверхность без выгоды. Собирается только по явному `--target tools`.

### `runtime` — `aspnet:10.0-alpine` (по умолчанию)

```dockerfile
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_gcServer=0
RUN apk add --no-cache krb5-libs ffmpeg
```

| Решение | Почему |
|---|---|
| `krb5-libs` | Npgsql тянет krb5 даже там, где Kerberos не используется |
| `ffmpeg` | Перекодирование. Единственная внешняя программа, которая нужна приложению |
| `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` | Образ без ICU меньше. **Следствие:** приложение не знает часовых поясов — их знает только Postgres. Отсюда `AT TIME ZONE` в статистике |
| `DOTNET_gcServer=0` | Рабочий GC вместо серверного: на маленькой машине он экономнее по памяти |
| `USER music` (uid/gid 1001) | Приложение не работает от root |
| `HEALTHCHECK` | `/health`, интервал 15 с, стартовый период 40 с, 5 попыток |

## Развёртывание

```bash
git clone <repo> && cd music-streaming
cp .env.example .env
$EDITOR .env
GIT_SHA=$(git rev-parse HEAD) docker compose up -d --build
```

Compose откажется стартовать без `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY`, `OWNER_PASSWORD`,
`GRAFANA_PASSWORD`, `PUBLIC_DOMAIN` — синтаксис `${VAR:?…}`.

Обновление:

```bash
git pull
GIT_SHA=$(git rev-parse HEAD) docker compose up -d --build
```

## Релиз

```bash
make release VERSION=1.2.0
```

## Куда дальше

[`14-conventions.md`](14-conventions.md) — как писать код, который примут.
