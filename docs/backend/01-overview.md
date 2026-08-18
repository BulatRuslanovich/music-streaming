# 01. Обзор

## Что это

Caimack — self-hosted сервис потокового воспроизведения **собственной** музыкальной коллекции.
Ты разворачиваешь его на своём сервере, загружает туда свои файлы и слушает их с любого
устройства через браузер.

## Карта репозитория

```
music-streaming/
├── backend/                    ← этой документацией описан он
│   ├── src/
│   │   ├── MusicStreaming.Domain/           сущности и чистые правила
│   │   ├── MusicStreaming.Application/      сценарии, DTO, порты
│   │   ├── MusicStreaming.Infrastructure/   адаптеры, EF Core, фоновые процессы
│   │   ├── MusicStreaming.Api/              контроллеры и композиция приложения
│   │   └── MusicStreaming.Tools.ArtistImages/  отдельная утилита
│   ├── tests/
│   │   ├── MusicStreaming.UnitTests/        чистые функции
│   │   └── MusicStreaming.IntegrationTests/ реальный стек в Testcontainers
│   ├── MusicStreaming.slnx      решение (новый XML-формат, не .sln)
│   ├── Directory.Build.props    версия, TFM, nullable для всех проектов
│   ├── Dockerfile               три стадии: build / tools / runtime
│   └── .editorconfig
├── frontend/                   Next.js 15, вне этой документации
├── deploy/                     Caddy, Prometheus, Loki, Promtail, дашборды Grafana
├── docs/
│   ├── backend/                ← вы здесь
│   └── SCREENSHOTS.md
├── scripts/release.sh          проставить версию и создать тег
├── storage/                    локальная библиотека (в git не входит)
├── docker-compose.yml          боевой стенд целиком
├── docker-compose.dev.yml      override: публикует порт Postgres на localhost
├── .env.example                шаблон переменных окружения
└── Makefile                    ярлыки для локальной разработки
```

Наружу смотрит только Caddy. Порт бэкенда публикуется на `127.0.0.1`, то есть доступен на сервере, но
не из интернета — этим и защищены `/docs` и `/metrics`.

## Куда дальше

Можно почитать [`11-configuration.md`](11-configuration.md): что нужно задать, чтобы
приложение вообще стартовало.
