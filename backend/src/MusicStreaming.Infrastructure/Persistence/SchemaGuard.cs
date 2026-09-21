// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Infrastructure.Persistence;

/// <summary>
/// Сверяет модель EF с тем, что реально есть в базе.
/// </summary>
/// <remarks>
/// Схему создаёт db/init, миграций в приложении больше нет — значит, никто не поправит базу
/// на старте за администратора. Единственное, что остаётся, — на запуске назвать всё,
/// чего не хватает, вместо падения на первом же запросе к недостающей колонке.
/// </remarks>
public static class SchemaGuard
{
    private const string ColumnsQuery = """
        SELECT table_name, column_name
        FROM information_schema.columns
        WHERE table_schema = current_schema();
        """;

    private static readonly string FunctionQuery =
        $"SELECT to_regprocedure('{SearchRank.FunctionName}(text, text)') IS NOT NULL;";

    /// <summary>Чего не хватает в базе, в порядке, пригодном для сообщения об ошибке.</summary>
    public static async Task<IReadOnlyList<string>> FindMissingObjectsAsync(
        ApplicationDbContext db, CancellationToken ct = default)
    {
        var present = await ReadColumnsAsync(db, ct);
        var missing = new List<string>();

        // Именно design-time-модель: в рабочей, оптимизированной для чтения, нет признака
        // ExcludeFromMigrations — обращение к нему там кидает исключение.
        var tables = db.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            // Таблицы источников для SQL-выборок без ключа в базе не существуют — это формы строк.
            .Where(table => !table.IsExcludedFromMigrations)
            .OrderBy(table => table.Name, StringComparer.Ordinal);

        foreach (var table in tables)
        {
            if (!present.TryGetValue(table.Name, out var columns))
            {
                missing.Add($"table {table.Name}");
                continue;
            }

            missing.AddRange(table.Columns
                .Select(column => column.Name)
                .Where(column => !columns.Contains(column))
                .OrderBy(column => column, StringComparer.Ordinal)
                .Select(column => $"column {table.Name}.{column}"));
        }

        if (!await HasSearchFunctionAsync(db, ct))
            missing.Add($"function {SearchRank.FunctionName}(text, text)");

        return missing;
    }

    private static async Task<Dictionary<string, HashSet<string>>> ReadColumnsAsync(
        ApplicationDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ColumnsQuery;

            var columns = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var table = reader.GetString(0);
                if (!columns.TryGetValue(table, out var names))
                    columns[table] = names = new HashSet<string>(StringComparer.Ordinal);

                names.Add(reader.GetString(1));
            }

            return columns;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> HasSearchFunctionAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = FunctionQuery;
            return await command.ExecuteScalarAsync(ct) is true;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
