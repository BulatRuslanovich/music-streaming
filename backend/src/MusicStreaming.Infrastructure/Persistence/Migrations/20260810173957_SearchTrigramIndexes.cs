using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds PostgreSQL trigram indexes for global search.
///
/// Search matches with an unanchored <c>LIKE '%term%'</c>, which a normal B-tree index cannot
/// serve — without help every query degrades into a sequential scan over the whole library. The
/// <c>pg_trgm</c> GIN indexes below are what keep search responsive at the 10,000-track target
/// from the specification.
///
/// The indexes are written as raw SQL because the operator class (<c>gin_trgm_ops</c>) has no
/// representation in the EF Core model, so they do not appear in the model snapshot.
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

        // The extension is left in place: other objects may depend on it, and dropping a shared
        // extension is a far more destructive act than dropping the indexes above.
    }
}
