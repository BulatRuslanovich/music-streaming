using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations;

/// <summary>
/// Добавляет триграммные индексы PostgreSQL для глобального поиска.
///
/// Поиск ищет незаякоренным <c>LIKE '%term%'</c>, который обычный B-tree обслужить не может: без
/// помощи любой запрос вырождается в последовательный проход по всей библиотеке. Именно GIN-индексы
/// <c>pg_trgm</c> ниже держат поиск отзывчивым на целевых 10 000 треках из спецификации.
///
/// Индексы написаны сырым SQL, потому что класс операторов (<c>gin_trgm_ops</c>) никак не выражен
/// в модели EF Core, поэтому в снимке модели их нет.
/// </summary>
public partial class SearchTrigramIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS ix_artists_normalized_name_trgm
                ON artists USING gin (normalized_name gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS ix_albums_normalized_title_trgm
                ON albums USING gin (normalized_title gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS ix_tracks_normalized_title_trgm
                ON tracks USING gin (normalized_title gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS ix_genres_normalized_name_trgm
                ON genres USING gin (normalized_name gin_trgm_ops);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_genres_normalized_name_trgm;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tracks_normalized_title_trgm;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_albums_normalized_title_trgm;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_artists_normalized_name_trgm;");

        // Расширение оставляем на месте: от него могут зависеть другие объекты, а удаление общего
        // расширения куда разрушительнее, чем удаление индексов выше.
    }
}
