using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations;

/// <summary>
/// Заводит <c>search_rank</c> — функцию, по которой поиск ранжирует выдачу.
///
/// <para>
/// Правило одно на исполнителей, альбомы, треки и жанры, а запросов четыре, поэтому оно и живёт в
/// одном месте. Записанное на C# в каждом запросе, оно рано или поздно разошлось бы; функция базы
/// же попадает прямо в <c>ORDER BY</c>, и сортировать в памяти не приходится.
/// </para>
///
/// <para>
/// Сравнивается с уже нормализованными колонками (нижний регистр, схлопнутые пробелы), поэтому ни
/// приведения регистра, ни <c>LIKE</c> здесь нет: <c>starts_with</c> и <c>position</c> работают с
/// текстом буквально, а значит запрос со знаком процента или подчёркиванием ищется как обычный
/// текст и экранировать в нём нечего.
/// </para>
///
/// <para>
/// Ранг 4 достаётся тому, что нашлось по смежному полю: трек, совпавший именем исполнителя, а не
/// названием. Такие идут после всех, чьё название действительно совпало.
/// </para>
/// </summary>
public partial class SearchRankFunction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION search_rank(value text, term text) RETURNS integer AS $$
                SELECT CASE
                    WHEN value = term                                    THEN 0
                    WHEN starts_with(value, term)                        THEN 1
                    WHEN position(' ' || term in ' ' || value) > 0       THEN 2
                    WHEN position(term in value) > 0                     THEN 3
                    ELSE 4
                END;
            $$ LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS search_rank(text, text);");
    }
}
