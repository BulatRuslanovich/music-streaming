# 12. Тесты

| Проект | Что проверяет | Нужен Docker |
|---|---|---|
| `MusicStreaming.UnitTests` | Чистые функции: без базы, без HTTP, без побочных эффектов | нет |
| `MusicStreaming.IntegrationTests` | Приложение целиком поверх настоящего PostgreSQL | **да** |

## Запуск

```bash
make test
# = cd backend && dotnet test MusicStreaming.slnx --configuration Release

# только юнит-тесты, без Docker
dotnet test backend/tests/MusicStreaming.UnitTests/MusicStreaming.UnitTests.csproj

# один класс
dotnet test backend/MusicStreaming.slnx --filter "FullyQualifiedName~TrackDeleteTests"
```

> **Без Docker интеграционные тесты пропускаются, а не падают.** Прогон будет зелёным, но проверит
> примерно треть поведения. Перед отправкой PR убедитесь, что Docker запущен.

**Имена-предложения:**

```csharp
A_batch_takes_its_files_albums_artists_and_genres_with_it
Ids_that_were_already_gone_come_back_as_missing_and_do_not_stop_the_rest
Bulk_delete_is_closed_to_everyone_but_administrators
```

## Куда дальше

[`13-operations.md`](13-operations.md) — сборка и эксплуатация.
