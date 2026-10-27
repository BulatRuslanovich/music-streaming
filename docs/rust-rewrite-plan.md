# План: бэкенд на Rust

Сейчас: ASP.NET Core / .NET 10, ~17 тыс. строк C# (`backend/src`) и ~6,8 тыс. строк тестов.
Цель — тот же продукт на Rust: фронт и Android-клиент не меняются, база и `storage/` остаются как есть.

## 0. Рамки

**Не меняется**

- HTTP-контракт: пути, коды ответов, JSON, куки, заголовки кэша (раздел 1).
- Схема БД: `db/init/*.sql` остаётся единственным источником, миграций по-прежнему нет.
- Раскладка `storage/`: `music/`, `covers/`, `artists/`, `playlists/`, `hls/{hash}/{quality}/`, `models/clap/`.
- Caddy, Postgres, фронт — всё, кроме сервиса `backend` в `docker-compose.yml`.

**Можно менять**

- Поведение рекомендаций (полки, радио, миксы, настроения): формы ответов те же, алгоритмы — новые и проще.
- Внутреннее устройство: очереди, кэши, воркеры, способ декодирования аудио.
- Переменные окружения бэкенда — плоские `SCREAMING_SNAKE` вместо .NET-секций (приложение C).

**Каталоги:** текущий C#-бэкенд переезжает в `backend_old/`, новый пишется в `backend/`.
Прода нет, поэтому откат и совместимость данных между версиями не нужны: `backend_old/` — справочник и эталон для сверки,
после переключения он удаляется.

## 1. Контракт — что нельзя сломать

### JSON

- [ ] Имена полей в camelCase.
- [ ] Поля со значением `null` **не выводятся** (`WhenWritingNull`) → `#[serde(skip_serializing_if = "Option::is_none")]` / `serde_with::skip_serializing_none` на всех DTO.
- [ ] Enum'ы строками, как в C# (`JsonStringEnumConverter` без политики → `"Normal"`, `"Admin"`) → `#[serde(rename_all = "PascalCase")]`. Проверить каждый enum по факту, не по памяти.
- [ ] Даты — ISO-8601 со смещением, GUID — строчными с дефисами.
- [ ] Принимаемый JSON: ASP.NET регистронезависим к именам полей, serde — нет. Проверить, что фронт и Android шлют ровно camelCase.

### Ошибки

- [ ] `application/problem+json`: `{ status, title, detail, instance }`.
- [ ] Невалидный запрос → 400 в формате `ValidationProblemDetails` (`errors: { field: [..] }`); `title` локализован.
- [ ] Язык из `Accept-Language` (en/ru), заголовок `Content-Language` в ответе; таблица переводов — `App/Common/ErrorText.cs`.
- [ ] 500 без деталей наружу, исключение — в строку лога запроса.
- [ ] Отменённый клиентом запрос — без ответа и без ошибки в логе.

### Авторизация

- [ ] Всё требует входа, кроме `POST /api/auth/{login,refresh,logout}` и `/health`. Политика `Admin` — claim `role=Admin`.
- [ ] Access-токен берётся из `Authorization: Bearer` **или** из куки `ms_access`.
- [ ] JWT HS256, ключ — UTF-8 байты `JWT_SIGNING_KEY` (≥ 32 байт), `iss`/`aud` = `music-streaming`, допуск по часам 30 с.
      Claims: `sub`, `username`, `jti`, `sid` (id refresh-токена), `role` (только у админа).
      При одинаковом ключе токены старого и нового бэкенда взаимозаменяемы, и после переключения никого не разлогинит.
- [ ] На каждом запросе: сессия `sid` ещё жива (строка в `refresh_tokens` и `users.is_active`). Результат кэшируется на 30 с,
      отзыв сессии или всех сессий пользователя сбрасывает кэш сразу.
- [ ] Refresh: ротация, повторное использование отозванного токена в течение 20 с — это гонка параллельных запросов, а не атака;
      позже — отзыв **всех** сессий пользователя. При выдаче нового токена удаляются протухшие (старше суток).
- [ ] Refresh-токен: 32 случайных байта в base64url, в базе — SHA-256 в hex строчными.
- [ ] Пароли: bcrypt от BCrypt.Net-Next (`$2a$`/`$2b$`) должны проверяться крейтом `bcrypt` — проверить на реальном хеше из базы.
- [ ] Куки: `ms_access` и `ms_refresh` (HttpOnly, `Path=/`), `ms_session` (не HttpOnly, base64url JSON пользователя),
      `SameSite=Lax`, `Secure` везде, кроме dev по http; удалять legacy `ms_refresh` с `Path=/api/auth`.
      Android-клиент работает через куки (`PersistentCookieJar`) — значит, это контракт и для него.
- [ ] Старт: дождаться Postgres (12 попыток × 5 с), создать владельца из `Owner:*`, вернуть ему права админа,
      `ResetPasswordOnStartup` (`DatabaseInitializer`).

### Медиа

- [ ] `stream`, `download`, HLS-ассеты: Range-запросы (206, `Content-Range`, `Accept-Ranges`), `ETag` из `content_hash`, `If-None-Match` → 304.
- [ ] `download`: `Content-Disposition` с безопасным именем «Исполнитель - Название.ext» (обрезка до 120 символов, `filename*` в UTF-8).
- [ ] `Cache-Control` ровно как сейчас на каждом эндпоинте (stream — 7 дней, HLS-ассет — immutable на год, master — час + swr, обложки — 30 дней + swr, waveform — сутки, download — no-store).
- [ ] HLS master: если вариантов ещё нет — `202` + `Retry-After: 2` + `no-store` и срочная задача на транскод; файлы — только `index.m3u8` и `media.m4s`.
- [ ] Обложки: `?size=Full|Thumb|Large` (640/256/1024), WebP.
- [ ] SSE `GET /api/playback/session`: события `claimed`, `ping` каждые 20 с, `displaced` (JSON с `deviceId`/`deviceName`) — и закрытие.
- [ ] Загрузка: multipart, аудио ≤ 200 МБ, картинки ≤ 8 МБ, длинные загрузки без минимальной скорости.

### Прочее

- [ ] `/health` — 200 без авторизации (по нему ждут Caddy и compose).
- [ ] Проксирующие заголовки (`X-Real-IP` от Caddy) — в лог запроса.
- OpenAPI / Scalar не переносятся.

## 2. Стек

| Задача                  | Сейчас                        | Rust                                                                 |
| ----------------------- | ----------------------------- | -------------------------------------------------------------------- |
| HTTP                    | ASP.NET Core, Kestrel         | `axum` + `tower-http` (trace, limits, timeout), `tokio`              |
| База                    | EF Core + Npgsql              | `sqlx` (Postgres, `query!` с проверкой на этапе компиляции, offline-режим `.sqlx/`) |
| JWT / пароли            | JwtBearer, BCrypt.Net-Next    | `jsonwebtoken`, `bcrypt`, `sha2`, `rand`                             |
| Кэши                    | `IMemoryCache`                | `moka` (TTL)                                                         |
| Логи                    | Serilog                       | `tracing` + `tracing-subscriber`                                     |
| Конфиг                  | `IOptions` + валидация        | `envy` (env → struct) + `dotenvy` (локальный `.env`) + проверка при старте |
| Ошибки                  | исключения + middleware       | `thiserror` → `IntoResponse` в ProblemDetails                        |
| Теги MP3/FLAC           | TagLibSharp                   | `lofty` (чтение и запись, длительность, битрейт, частота)            |
| Картинки                | ImageSharp                    | `image` (decode JPEG/PNG/WebP), `fast_image_resize` (Lanczos3, SIMD), `webp` (libwebp: lossy q=82 — у `image` WebP только lossless) |
| Декодирование аудио     | ffmpeg (процесс)              | `symphonia` (MP3 + FLAC) + `rubato` (ресемплинг); ffmpeg — только для AAC/HLS и ремукса FLAC из MP4 |
| FFT                     | свой `Fft.cs`                 | `realfft` (FFT вещественного сигнала, вдвое меньше работы)           |
| ONNX                    | Microsoft.ML.OnnxRuntime      | `ort` (тот же ONNX Runtime)                                          |
| Вычисления по векторам  | `System.Numerics.Tensors`     | плоский `Vec<f32>` + `rayon`; при необходимости `simsimd`            |
| HTTP-клиенты            | `HttpClient`                  | `reqwest` (rustls)                                                   |
| Фоновые задачи          | `BackgroundService`, `Channel` | `tokio::spawn`, `mpsc`, `Semaphore`, `tokio-util::CancellationToken` |
| Range / ETag            | `File(..., enableRangeProcessing)` | свой небольшой файловый ответчик на `http-range-header` (`ServeFile` из tower-http не ставит свой ETag) |
| Тесты                   | xUnit + Testcontainers        | `cargo test`, `#[sqlx::test]`, `insta` (снимки JSON)                 |

## 3. Устройство проекта

Cargo workspace из нескольких крейтов — ради времени сборки: правка в хендлере не пересобирает DSP и рекомендации.

```
backend_old/            # C#, только для справки и эталонов; удаляется после переключения
backend/
  Cargo.toml            # workspace
  crates/
    api/        # бинарник: main, роутер, хендлеры, экстракторы (CurrentUser, Admin), ProblemDetails, куки
    app/        # сервисы и запросы к БД (sqlx), DTO, ошибки, локализация, воркеры (`core` занято стандартной библиотекой)
    media/      # хранилище, ffmpeg, декодирование, cue, CLAP + mel, картинки, теги — без БД
    reco/       # векторы, снимок эмбеддингов, рекомендации, карта, дубликаты — чистые функции без IO
  .sqlx/        # offline-данные sqlx: CI собирает без базы
  Dockerfile
```

- **Без DI-контейнера.** `AppState` собирается в `main`: `PgPool`, `Storage`, кэши, очереди, `ArcSwap<EmbeddingSnapshot>`, реестр воспроизведения.
- **CPU-тяжёлое — не на async-потоках.** mel, FFT, ONNX, cue, раскладка карты, пересчёт полок — в `spawn_blocking` или `rayon`.
- **Дочерние процессы ffmpeg** — `tokio::process` с `kill_on_drop(true)`: остановка сервера сама гасит ffmpeg.
- **Плавная остановка:** один `CancellationToken` на все воркеры, `axum::serve(...).with_graceful_shutdown`.

## 4. Что сделать лучше, чем сейчас

1. **Один проход декодирования на трек.** Сейчас CLAP запускает ffmpeg до 8 раз (по разу на окно), и cue-анализ — ещё раз.
   Принимаются только MP3 и FLAC, а их `symphonia` декодирует сама. Поток PCM один: он ресемплится в 11 025 Гц для cue-детектора
   и в 48 кГц для CLAP, причём для CLAP сохраняются только сэмплы внутри запланированных окон. Часовой микс не держится в памяти целиком,
   как и сейчас. Cue и эмбеддинг объединяются в один воркер «анализ трека».
2. **`realfft` вместо комплексного FFT** — в mel-спектрограмме и в cue-детекторе.
3. **Проверка живой сессии проще:** вместо связки `CancellationTokenSource` + `IMemoryCache` — `moka` с TTL 30 с
   и счётчик-«поколение» на пользователя. Отзыв всех сессий — это просто инкремент счётчика.
4. **SQL проверяется при компиляции** (`sqlx::query!`): расхождение кода и `db/init` ломает сборку, а не прод.
5. **Rate limit на `/api/auth/login`** (`tower_governor`) — сейчас его нет.
6. **Встроенная культура не нужна:** в Rust нет `CurrentCulture`, поэтому проблема «0,5 вместо 0.5 в аргументах ffmpeg» исчезает сама.
7. **Общие типы с фронтом (после переключения):** `ts-rs` генерирует TS-типы из Rust-DTO, и `frontend/src/lib/api.ts` больше не расходится с бэкендом.
8. **Образ:** `cargo-chef` для кэша слоёв, рантайм `debian:bookworm-slim` + ffmpeg. Лимит памяти `BACKEND_MEMORY_LIMIT` пересмотреть
   по замерам. Честно: модель CLAP в ONNX Runtime весит столько же, сколько и сейчас, так что основной выигрыш по памяти — без неё.

Сознательно **не** делаем в первой версии:

- **pgvector.** Снимок эмбеддингов в памяти для личной библиотеки быстрый и простой; pgvector — это смена образа Postgres и схемы.
- **Хранение импортов Яндекса в базе.** Сейчас они живут в памяти и теряются при перезапуске; это устраивает.
- **Квантизация CLAP в int8.** Можно попробовать потом, сверив пороги дубликатов.

## 5. Порядок работ

Оценки — для плотной работы; в сумме ~3–5 недель.

### Фаза 0. Страховка (2–3 дня)

- [x] **Контрактные тесты по HTTP** — крейт `backend/crates/contract`, запуск `make contract-old` / `make contract-new`
      (`backend/contract/contract.sh`). Одноразовый Postgres в контейнере (127.0.0.1:55432), бэкенд на 127.0.0.1:5299.
      - Сид (`backend/contract/seed`: дамп данных + файлы) собран через API старого бэкенда из 6 синтетических треков
        (`contract/fixtures/generate.sh`, ffmpeg): MP3 и FLAC, альбом с номерами, обложки в тегах, два артиста в одном теге,
        трек без тегов, тишина по краям, ровный бит. Плюс второй пользователь, настройки, избранное, плейлисты, история,
        сигналы, текст песни; воркеры успевают посчитать HLS, cue и эмбеддинги. Пересобрать: `make contract-seed`.
      - 67 тестов, снимки сняты со старого бэкенда (`make contract-accept`) и дважды подряд воспроизводятся на нём же.
        Рекомендации, shuffle и recap проверяются по форме ответа, остальное — по значениям.
- [x] **Эталоны для аудио** (`make golden` → `backend/contract/golden`): на каждый файл сида — окна и вектор CLAP, cue-точки,
      темп, волна; для одного трека — сырой PCM окна 48 кГц и его mel 1001×64, PCM 11 025 Гц и cue из него. Векторы и cue
      побитово совпадают с тем, что старый бэкенд записал в базу, — расчёт воспроизводим.
- [x] **Сверка тегов** (`cargo run -p contract --bin tag_audit -- <DATABASE_URL> <STORAGE_ROOT>`) на 1351 треке библиотеки:
      битрейт, частота и разрядность совпадают у всех; длительность — у 1341. У 10 MP3 `lofty` даёт x.48–x.50 с, а TagLib
      округлил вверх (+1 с) — в пределах допуска «той же записи» (±3 с). Округление «к чётному» (.NET) не задело ни одного трека.

**Что контракт показал о старом бэкенде** (Rust должен повторить):

- 401 от проверки токена — пустое тело и `WWW-Authenticate: Bearer`; 403 от политики `Admin` — пустое тело.
- ProblemDetails уходят с `Content-Type: application/json; charset=utf-8`, а не `application/problem+json`; у 401 из
  `refresh` есть `type` (`…rfc9110#section-15.5.2`).
- `PagedResult` содержит вычисляемое `totalPages`.
- Неверный GUID в пути — 404 (ограничение маршрута `{id:guid}`), а не 400.
- HLS master для трека, где Normal не ниже исходника (MP3 ≤ 128 кбит/с), — всегда 202 с ETag `…-original`: клиент играет файл.
- ETag обложек строится из времени изменения файла (в контракт входит только его наличие).

### Фаза 1. Каркас (2–4 дня)

- [x] `git mv backend backend_old`; поправить пути в `Makefile`, `docker-compose*.yml`, `backend-ci.yml`, чтобы старый бэкенд
      по-прежнему собирался и запускался (он нужен для контрактных тестов и эталонов). Скрипты экспорта CLAP остались в `backend/scripts`.
- [x] Workspace в `backend/`, конфиг из окружения с проверкой при старте, `tracing`, пул `sqlx`, ожидание базы, создание владельца.
- [x] Новые переменные окружения: `backend/.env.example` для локального запуска. Сервис `backend` в compose до фазы 8 собирается
      из `backend_old` со старыми именами; корневой `.env.example` и `README.md` меняются вместе с ним в фазе 8.
- [x] Тип ошибки → ProblemDetails, локализация (`ErrorText` целиком), невалидный JSON → 400 с `errors`.
- [x] Авторизация полностью: login / refresh / logout / me, куки, экстракторы `CurrentUser` и `Admin`, кэш живых сессий.
      Параллельные refresh одного токена сериализуются `SELECT … FOR UPDATE`.
- [x] `/health`, `Dockerfile` (cargo-chef, `SQLX_OFFLINE`), CI (`fmt`, `clippy -D warnings`, тесты на Postgres из `db/init`), цели в `Makefile`.

### Фаза 2. Каталог и данные пользователя (4–6 дней)

- [x] `config`, `me/settings`, `me/password`, `admin/users`.
- [x] Треки, альбомы, артисты, жанры: списки с пагинацией, карточки, правка, удаление, `bulk-delete`, `lookup`, `shuffle` (`shuffle_key`), `top-tracks`.
- [x] Поиск: `pg_trgm` + `search_rank()` из `001_extensions.sql`, нормализация запроса как в `SearchTerm`/`Normalize`.
- [x] Избранное, история, плейлисты (порядок, публичные), тексты песен (разбор LRC), `library/overview`.
      Загрузка и удаление обложек альбомов, артистов и плейлистов — в фазе 3 вместе с картинками.
- [x] Контракт: добавлен набор `writes.rs` (8 тестов на изменения: правка и откат трека, удаление с уборкой опустевших
      артистов, альбомов и жанров, конфликты имён, тексты, избранное, история, настройки, жизненный цикл учётной записи).
      На Rust проходят все 53 теста авторизации, каталога, данных пользователя и изменений.
- Заметки:
  - База для `cargo build` (sqlx проверяет запросы) и `make backend` — `make db-rust`: отдельный контейнер со схемой
    из `db/init` на 127.0.0.1:55433. Postgres на 5432 — чужой (`C:\main\work\Caimack`), его схема неполная.
  - Списки с переменными условием и сортировкой собираются `QueryBuilder` (без проверки при компиляции), остальное —
    `query!`. Покрытие списков держат контрактные тесты.
  - Форматы как у System.Text.Json: даты `…T21:42:48.33446+00:00`, целые double без `.0`, `null`-поля не пишутся.

### Фаза 3. Медиа (5–7 дней)

- [x] Хранилище: проверка, что путь не выходит за корень; трек пишется потоком с SHA-256 и потолком 200 МБ (413).
- [x] `stream` и `download` (Range → 206/416, ETag и If-None-Match → 304, `Content-Disposition` как у ASP.NET), `waveform`.
- [x] Обложки треков, альбомов, артистов, плейлистов: три размера WebP (libwebp, q=82), загрузка и удаление, лимит 12 Мп,
      только JPEG/PNG/WebP, поворот по EXIF. Новый крейт `media` — картинки, теги (`lofty`), ffmpeg, форматы; без базы.
- [x] HLS: master / ассеты, очередь (срочная + прогрев, без дублей), воркеры (1 срочный + N прогревочных), догрев при старте.
      ffmpeg гасится при остановке сервера; без ffmpeg сервер не стартует.
- [x] Загрузка: `upload/check`, `upload` — сниффинг контейнера, теги, разбор имени файла «Артист - Название», дубль по хешу,
      «та же запись» ±3 с, приоритет lossless, замена файла, обложка и слова из тегов, прогрев HLS после загрузки.
      Синхронные слова из SYLT не читаются (`lofty` их не отдаёт); постановка в обогащение — в фазе 6.
- [x] Контракт: `writes_media.rs` — обложки (загрузка, размеры, отказы, удаление, чужой плейлист) и загрузка треков
      (проверка, повтор, MP3 поверх FLAC, FLAC поверх MP3, без тегов, неподходящие файлы; файлы перекодирует сам тест через ffmpeg).
      Порядок по популярности в поиске и топе сравнивается без учёта порядка: её пересчитывают фоновые воркеры старого бэкенда.
      На Rust проходят 64 из 77 тестов; остались воспроизведение (фаза 4) и рекомендации (фаза 7).

### Фаза 4. Воспроизведение (2–3 дня)

- [x] `playback/state`, `now`, `handoff`, SSE `session` — реестр в памяти: держатель сессии, вытеснение другим
      устройством (`displaced` и конец потока), отчёты только от держателя, позиция с поправкой на прошедшее время.
      Закрытый клиентом поток отпускает устройство (Drop у владельца сессии).
- [x] `playback/signals` → `playback_events`: типы по имени и номеру, время прижимается к последней неделе, источник —
      только ключ полки, события про удалённые треки отбрасываются. Признаки событий для рекомендаций и отметка
      «пересчитать» появятся в фазе 7.
- [x] Контракт: тест `takeover_between_devices` — два SSE-потока, вытеснение, 409 на отчёт вытесненного, перехват очереди.
      На Rust проходят 70 из 78 тестов; остались только рекомендации.

### Фаза 5. Анализ аудио (4–6 дней)

- [x] Однопроходный декодер (`media::analysis`): symphonia (MP3 и FLAC, задержка энкодера обрезается так же, как в ffmpeg),
      моно как у `ffmpeg -ac 1` во float — стерео (L+R)/√2 без нормировки, свой полифазный ресемплер (sinc, окно Кайзера) в
      11 025 Гц для cue и 48 кГц для окон CLAP. ffmpeg для анализа больше не запускается.
- [x] Порт `CueDetector` (realfft): на тех же сэмплах совпадает с C# точно — начало, конец, затухание, темп, все 240 столбиков.
      Через свой декодер и ресемплер на всех 6 файлах сида cue совпадают до миллисекунды, темп — точно.
- [x] CLAP: планировщик окон, mel 64×1001 (совпадает с C# до 0,001 дБ), `ort` 2.0, батчи по 4, среднее + нормализация.
      На окнах от ffmpeg вектор совпадает с C# точно (косинус 1,00000); FLAC целиком — 0,9997–1,0000.
- [x] Стратегия сменена на `clap_body_v3`: у MP3 вектор отличается от C# (косинус 0,88–0,999), потому что C# резал каждое окно
      отдельным запуском ffmpeg с `-ss`. Сам ffmpeg без `-ss` расходится с C# так же, а с нашим совпадает на 0,9999 — то есть
      непрерывное декодирование и есть «правильное». Библиотека пересчитает векторы сама.
- [x] Воркер анализа (`app::analysis`): один вместо двух прежних — cue и вектор за одно декодирование; раз в 30 с, новые треки
      первыми, неудача повторяется через неделю. Модель грузится при старте; без неё сервер не стартует.
- [x] Сверка: `cargo test -p media --test golden` (mel и cue на тех же сэмплах) и `-- --ignored` (весь путь с моделью).
- [ ] Снимок эмбеддингов в памяти — переносится в фазу 7: его потребители (карта, дубликаты, рекомендации, радио) там же.

### Фаза 6. Интеграции (3–4 дня)

- [x] Deezer (фото артистов), LRCLIB (тексты), воркер обогащения с догоном при старте и паузами между запросами.
      Загрузка ставит в очередь трек (слова) и только что созданных артистов (фото); адрес Deezer — `DEEZER_BASE_URL`.
- [x] Яндекс Музыка: поиск, импорт лайков, альбомов и треков (очередь в памяти, до 20 задач), подписанная ссылка на файл,
      ремукс FLAC из MP4 через ffmpeg, запись тегов через `lofty`. Токен не хранится — приходит с каждым запросом.
      Временная папка импорта удаляется и при остановке сервера посреди трека.
- [x] Сверка: контрактные `integrations` (токен, проверка источника, жизненный цикл импорта без сети),
      юнит-тесты подписи, очереди и разбора ответов; `cargo test -p app --lib integrations -- --ignored` — живые Deezer и LRCLIB.

### Фаза 7. Рекомендации v2 (5–8 дней)

Алгоритмы — заново и проще. Расчёты — в крейте `reco` (чистые функции без IO, свои тесты), база и ответы —
в `app::recommendations`. Таблиц-свёрток и кеша полок больше нет: всё считается на лету по снимку в памяти.

- [x] **Снимок эмбеддингов в памяти** (`index`): строится при старте, после прохода анализа с новыми векторами,
      после пересчёта популярности и при смене подписи векторов (сверка раз в минуту). Настроения по трекам
      считаются вместе со снимком. 1351 трек × 512 — 0,3 с.
- [x] **История слушателя** — один SQL-агрегат по событиям за 180 дней: у каждого типа события свой вес,
      полураспад 45 дней, скип в начале — минус (в своих списках — слабее), лайк из избранного не затухает.
      Кеш на 30 с. `user_*_affinity`, `user_taste_profiles`, `recommendation_cache` не используются.
- [x] **Вкус:** до четырёх центров звучания (взвешенный сферический k-means), отношение к артистам и жанрам,
      ранг близости каждого трека. Счёт трека — вкус, артисты/жанр, популярность, новизна, немного шума дня;
      поправки за недавно слышанное и нелюбимого артиста; отвергнутое не предлагается.
- [x] **`home/feed`:** «Для вас», «Потому что вы слушали X» (X сменяется по дням), «Открытия», «Артисты для вас»;
      отбор с разнообразием (≤2 трека артиста и альбома, штраф за похожесть на уже выбранное, без копий одной
      записи), доля мест на разведку, знакомого — не больше трети. Зерно — слушатель и местный день. 5 мс.
- [x] **`home/mixes/{kind}`, дневной микс** — снимок в `daily_mixes` на местный день: взвешенная выборка
      из пула рекомендаций с добором из избранного и нового.
- [x] **`recommendations/radio`** — якорь (трек, центр вкуса или настроение), ближайшие с поправкой на вкус,
      без недавно слушанного, очереди и отвергнутого; лимит на артиста, разведка; застрявшее на одном артисте
      радио уходит к другому центру вкуса. <1 мс.
- [x] **`recommendations/moods`** — `moods.json` встроен в `reco`, порядок файла сохранён.
- [x] **`recap`** — порт; границы месяца и часы — в часовом поясе слушателя средствами Postgres.
- [x] **`library/duplicates`** — порт плюс правка ложных срабатываний: пары без общего артиста и названия
      требуют сходства 0.94 и длительности ±1 с (на живой библиотеке было 18 ложных групп, стало 0).
      Dismiss, merge.
- [x] **`library/map`** — порт упрощённого UMAP (kNN по ядрам), подписи настроений в самой плотной кучке;
      пока новая карта строится, отдаётся прошлая. 0,75 с на 1351 треке.
- [x] **`recommendations/stats`**; обслуживание раз в 6 часов: популярность в `track_stats`, чистка событий
      старше 180 дней. Выученных весов нет: блок «Чему движок научился» на фронте всегда в ожидании.
- [x] Сверка: контрактные тесты 80/80 на Rust. Живая библиотека — только чтением:
      `cargo run --release -p contract --bin reco_probe -- <DATABASE_URL> <пользователь>`
      (транзакции read-only; печатает полки, радио, настроения, дубликаты и время).

### Фаза 8. Переключение (1–2 дня)

Прода нет, поэтому без обратной совместимости: старый бэкенд удалён, контракт дальше задаёт Rust.

- [x] Контрактные тесты 80/80 на Rust. После переключения они, сид, эталоны звука и сверка с C# удалены:
      сравнивать больше не с чем. Тест тегов делает аудио через ffmpeg (в CI он ставится).
- [x] Сервис `backend` в `docker-compose.yml` собирается из `./backend`; переменные — из приложения C
      (`DATABASE_URL` собирается из `POSTGRES_*`, поэтому пароль базы — без спецсимволов: `openssl rand -hex 32`).
      `POSTGRES_MAX_POOL_SIZE` → `DATABASE_MAX_CONNECTIONS`, лимит памяти бэкенда 2g → 1g.
      Образ — Debian 13 (trixie): готовая статическая ONNX Runtime из `ort` требует glibc 2.38+, в bookworm 2.36.
      В контейнере с моделью и после анализа трека — ~0,6 ГБ.
- [x] Удалены `backend_old/`, `global.json`, `TestResults/`, .NET-задача CI, .NET-цели `Makefile`,
      `contract/golden.sh` (эталоны в `contract/golden` остаются фикстурами `media`), `tag_audit`.
      Из `.gitignore` убраны .NET-шаблоны — `bin/` прятал `crates/contract/src/bin`.
- [x] `db/init`: удалены `user_track_affinity`, `user_artist_affinity`, `user_genre_affinity`,
      `user_taste_profiles`, `recommendation_cache`, у `playback_events` — `sequence` и `features`;
      файлы `060_playback_events.sql`, `070_daily_mixes.sql`. Сид контрактных тестов поправлен тем же образом.
      Живую базу переводить руками:
      `DROP TABLE user_track_affinity, user_artist_affinity, user_genre_affinity, user_taste_profiles, recommendation_cache;`
      `ALTER TABLE playback_events DROP COLUMN sequence, DROP COLUMN features;`
- [x] API без совместимостей: нет стирания refresh-куки со старого пути `/api/auth`, нет `weights`
      в статистике рекомендаций; фронт без блока выученных весов.
- [x] Фронт по умолчанию ходит в `http://127.0.0.1:5199` (Node мог разрешить `localhost` в `::1`).
- [x] `README.md`, `db/README.md` — под Rust.
- [ ] Ручной прогон с веба и Android на своей библиотеке (контейнер проверен на чистой базе: старт с моделью, загрузка, анализ, HLS, обогащение; фронт → прокси → API): вход, стрим, HLS, загрузка, импорт, рекомендации.

## 6. Как переключаться

**Целиком, а не по маршрутам.** Перенос «по кусочку через Caddy» мешает состояние в памяти процесса, которое связывает эндпоинты с воркерами.
Эти группы обязаны жить в одном процессе:

| Группа                                         | Что связывает                                                       |
| ---------------------------------------------- | ------------------------------------------------------------------- |
| `playback/session` + `state` + `now` + `handoff` | реестр устройств в памяти                                           |
| `stream`/HLS + загрузка + транскод-воркеры     | очередь транскода в памяти                                          |
| `playback/signals` + рекомендации + home/radio/moods/map/duplicates | очередь «пересчитать», снимок эмбеддингов в памяти |
| загрузка + обогащение                           | очередь обогащения в памяти                                         |
| импорт Яндекса                                  | список задач в памяти                                               |

Плюс оба процесса не должны одновременно гонять одни и те же воркеры. Чтобы жить вдвоём, пришлось бы дописывать флаги в .NET и всё равно
переносить большими группами. Поэтому — контрактные тесты + переключение целиком. Прода нет, так что откатывать нечего.

## 7. Риски

| Риск                                            | Что делаем                                                              |
| ----------------------------------------------- | ----------------------------------------------------------------------- |
| Незаметно поменялся контракт                    | контрактные тесты из фазы 0, прогон на обоих бэкендах                   |
| mel-спектрограмма не та, что ждёт CLAP          | эталоны из C#; в крайнем случае — новая стратегия и полный пересчёт     |
| `lofty` читает длительность MP3 иначе, чем TagLib | сверка на всей библиотеке в фазе 0                                    |
| bcrypt-хеши не проверяются                       | тест на реальном хеше из базы в фазе 1                                 |
| WebP lossy тянет C-библиотеку (libwebp)          | собирается в Docker без проблем; на Windows — проверить в фазе 1        |
| `ort` и ONNX Runtime на Windows и в Docker       | проверить загрузку модели в фазе 1, не дожидаясь фазы 5                 |
| Долгая сборка                                    | workspace, `lld`/`mold` в Linux, offline-режим sqlx                    |
| `sqlx::query!` требует базу при сборке           | `cargo sqlx prepare`, `.sqlx/` в репозитории, `SQLX_OFFLINE=true` в CI  |

## 8. Принятые решения

- OpenAPI / Scalar не нужны.
- Новый бэкенд — `backend/`, старый C# — `backend_old/` до конца переезда.
- Переменные окружения бэкенда — новые, в стиле остального `.env` (приложение C).
- Прода нет: переключение без отката и без совместимости данных между версиями.

## Приложение A. Эндпоинты

`🔒` — только админ. Остальные — любой вошедший пользователь, кроме отмеченных «аноним».

**Auth** — `POST /api/auth/login` (аноним) · `POST /api/auth/refresh` (аноним) · `POST /api/auth/logout` (аноним) · `GET /api/auth/me`

**Me / config** — `GET /api/config` · `GET|PUT /api/me/settings` · `POST /api/me/password`

**Admin users 🔒** — `GET|POST /api/admin/users` · `PUT …/{id}/active` · `PUT …/{id}/role` · `POST …/{id}/password` · `POST …/{id}/sessions/revoke`

**Tracks** — `GET /api/tracks` · `GET /api/tracks/shuffle` · `POST /api/tracks/lookup` · `PUT /api/tracks/{id}` 🔒 · `DELETE /api/tracks/{id}` 🔒 ·
`POST /api/tracks/bulk-delete` 🔒 · `POST|DELETE /api/tracks/{id}/favorite` · `GET /api/tracks/{id}/lyrics` · `PUT /api/tracks/{id}/lyrics` 🔒

**Track media** — `GET /api/tracks/{id}/waveform` · `…/stream` · `…/hls/master.m3u8?maxQuality=` · `…/hls/{quality}/{fileName}` · `…/download` · `…/cover?size=`

**Upload 🔒** — `POST /api/tracks/upload/check` · `POST /api/tracks/upload`

**Albums** — `GET /api/albums` · `GET /api/albums/{id}` · `GET /api/albums/{id}/cover` · `PUT /api/albums/{id}` 🔒 · `POST|DELETE /api/albums/{id}/cover` 🔒

**Artists** — `GET /api/artists` · `GET /api/artists/{id}` · `GET …/{id}/top-tracks` · `GET …/{id}/image` · `PUT /api/artists/{id}` 🔒 · `POST|DELETE …/{id}/image` 🔒

**Genres** — `GET /api/genres` · `GET /api/genres/{id}/tracks`

**Search** — `GET /api/search` · `…/tracks` · `…/albums` · `…/artists` · `…/genres`

**Playlists** — `GET /api/playlists` · `GET /api/playlists/public` · `GET|PUT|DELETE /api/playlists/{id}` · `POST /api/playlists` ·
`GET|POST|DELETE …/{id}/cover` · `POST …/{id}/tracks` · `DELETE …/{id}/tracks/{trackId}` · `PUT …/{id}/tracks/order`

**Favorites / history** — `GET /api/favorites` · `GET /api/history` · `GET /api/history/recent` · `POST /api/history` · `DELETE /api/history`

**Playback** — `PUT /api/playback/state` · `GET /api/playback/now?deviceId=` · `POST /api/playback/handoff` · `GET /api/playback/session` (SSE) · `POST /api/playback/signals`

**Home / recommendations** — `GET /api/home/feed` · `GET /api/home/mixes/{kind}` · `GET /api/recommendations/stats` · `GET /api/recommendations/moods` · `POST /api/recommendations/radio` · `GET /api/recap`

**Library** — `GET /api/library/overview` · `GET /api/library/map` · `GET /api/library/duplicates` 🔒 · `POST …/duplicates/dismiss` 🔒 · `POST …/duplicates/merge` 🔒

**Yandex Music 🔒** — `GET /api/integrations/yandex-music/search` · `GET|POST …/imports` · `DELETE …/imports/{id}`

**Служебное** — `GET /health` (аноним)

## Приложение B. Фоновые задачи

| Сейчас                        | Когда                                      | В Rust                                   |
| ----------------------------- | ------------------------------------------ | ---------------------------------------- |
| `TranscodeWorker`             | очередь: 1 срочный + (ядра/2 − 1) прогревочных | то же, `Semaphore`                     |
| `TranscodeBackfillService`    | раз, через 30 с после старта                | то же                                    |
| `TrackCueWorker`              | каждые 30 с                                 | объединяется с эмбеддингами в «анализ трека» |
| `AudioEmbeddingWorker`        | очередь + догон                             | объединяется с cue                       |
| `EmbeddingIndexLoader`        | каждые 15 мин + по запросу                  | то же, `ArcSwap`                         |
| `RecommendationWorker`        | дебаунс после событий                       | то же, логика v2                         |
| `LibraryMaintenanceWorker`    | раз в N часов                               | то же                                    |
| `LibraryEnrichmentWorker`     | очередь + догон через минуту                | то же                                    |
| `YandexImportWorker`          | очередь задач импорта                       | то же                                    |

## Приложение C. Конфигурация

Плоские имена в `SCREAMING_SNAKE`, как в корневом `.env`. Где имя в `.env` уже есть (`JWT_SIGNING_KEY`, `OWNER_PASSWORD`, …),
бэкенд читает ровно его, и compose передаёт значения без переименования. Локально — файл `backend/.env` (`dotenvy`), `appsettings.json` больше нет.

| Сейчас (.NET)                                  | Rust                          | По умолчанию                       |
| ---------------------------------------------- | ----------------------------- | ---------------------------------- |
| `ConnectionStrings__Default` (строка ADO.NET)  | `DATABASE_URL`                | — обязательна; compose собирает `postgres://…` из `POSTGRES_*` |
| `Maximum Pool Size` внутри строки, `POSTGRES_MAX_POOL_SIZE` | `DATABASE_MAX_CONNECTIONS` | 40                         |
| `Jwt__SigningKey`                              | `JWT_SIGNING_KEY`             | — обязательна, ≥ 32 байт           |
| `Jwt__AccessTokenMinutes`                      | `JWT_ACCESS_TOKEN_MINUTES`    | 10                                 |
| `Jwt__RefreshTokenDays`                        | `JWT_REFRESH_TOKEN_DAYS`      | 30                                 |
| `Jwt__Issuer`, `Jwt__Audience`                 | убираются, константа в коде   | `music-streaming`                  |
| `Storage__RootPath`                            | `STORAGE_ROOT`                | `/storage`                         |
| `Owner__Username`                              | `OWNER_USERNAME`              | `admin`                            |
| `Owner__Password`                              | `OWNER_PASSWORD`              | — обязательна, пока нет ни одного пользователя |
| `Owner__ResetPasswordOnStartup`                | `OWNER_RESET_PASSWORD`        | `false`                            |
| `Lrclib__BaseUrl`                              | `LRCLIB_BASE_URL`             | `https://lrclib.net`               |
| — (адрес был зашит в `DeezerClient`)           | `DEEZER_BASE_URL`             | `https://api.deezer.com`           |
| `YandexMusic__BaseUrl`                         | `YANDEX_MUSIC_BASE_URL`       | `https://api.music.yandex.net`     |
| `YandexMusic__Client`                          | `YANDEX_MUSIC_CLIENT`         | `YandexMusicWebNext/1.0.0`         |
| `YandexMusic__SignKey`                         | `YANDEX_MUSIC_SIGN_KEY`       | текущий ключ из `YandexMusicOptions` |
| `ForwardedHeaders__KnownNetworks`              | `TRUSTED_PROXIES` (CIDR через запятую) | loopback + частные сети    |
| `ASPNETCORE_ENVIRONMENT`                       | `APP_ENV` (`development` / `production`) | `production` (влияет на `Secure` у кук) |
| `ASPNETCORE_URLS`, `launchSettings.json` (5199) | `LISTEN_ADDR`                | `0.0.0.0:8080`; в `backend/.env` — `127.0.0.1:5199`, куда смотрит фронт |
| уровни Serilog                                 | `RUST_LOG`                    | `info`                             |
| `DOTNET_gcServer` (Dockerfile)                 | убирается                     | —                                  |

`POSTGRES_MAX_POOL_SIZE` в корневом `.env` переименовывается в `DATABASE_MAX_CONNECTIONS`.
