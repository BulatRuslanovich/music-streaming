using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives a track every artist its tag named instead of only one.
    ///
    /// Until now the whole artist string became a single row, so a collaboration was filed under
    /// "BONES, Grayera" — a name that matches no search, owns no page, and hides both performers.
    /// The new <c>track_artists</c> table holds the full credit list, with
    /// <c>tracks.artist_id</c> kept as the primary credit.
    ///
    /// The backfill below splits the rows already in the library and re-points tracks and albums
    /// at the resulting artists. It mirrors <c>ArtistNames.Split</c>; the separator list and the
    /// composite-name exceptions are duplicated here on purpose, since the split has to happen in
    /// SQL. Keep the two in step.
    /// </summary>
    public partial class TrackArtists : Migration
    {
        /// <summary>Matches the separators in <c>ArtistNames.SeparatorPattern</c>.</summary>
        private const string SeparatorPattern =
            @"(?i)\s*[;/]\s*|\s*,\s*|[\s([]+(?:featuring|feat|ft|versus|vs)\.?\s+|\s+[x×]\s+";

        /// <summary>Matches <c>ArtistNames.Composite</c>: names that must survive a separator.</summary>
        private static readonly string[] CompositeNames =
        [
            "ac/dc",
            "tyler, the creator",
            "earth, wind & fire",
            "crosby, stills & nash",
            "crosby, stills, nash & young",
            "emerson, lake & palmer",
            "blood, sweat & tears",
            "peter, paul and mary",
        ];

        /// <summary>Renders <see cref="CompositeNames"/> as the rows of a SQL VALUES list.</summary>
        private static string CompositeValues =>
            string.Join(", ", CompositeNames.Select(n => $"('{n.Replace("'", "''")}')"));

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "track_artists",
                columns: table => new
                {
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_artists", x => new { x.track_id, x.artist_id });
                    table.ForeignKey(
                        name: "fk_track_artists_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_track_artists_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_track_artists_artist_id",
                table: "track_artists",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_artists_track_id_position",
                table: "track_artists",
                columns: new[] { "track_id", "position" });

            BackfillCredits(migrationBuilder);
        }

        /// <summary>
        /// Splits the artist rows that were written before the credit table existed. On an empty
        /// database every statement is a no-op, so a fresh install pays nothing for it.
        /// </summary>
        private static void BackfillCredits(MigrationBuilder migrationBuilder)
        {
            // Every performer named by an existing row, cleaned of the bracket debris a split
            // leaves behind ("Artist (feat. Other)"), de-duplicated per source row.
            migrationBuilder.Sql($"""
                CREATE TEMP TABLE _artist_parts AS
                WITH composite(normalized_name) AS (
                    VALUES {CompositeValues}
                ),
                split AS (
                    SELECT a.id AS old_id, s.part, s.ord - 1 AS position
                    FROM artists a
                    CROSS JOIN LATERAL regexp_split_to_table(a.name, '{SeparatorPattern}')
                        WITH ORDINALITY AS s(part, ord)
                    WHERE a.normalized_name NOT IN (SELECT normalized_name FROM composite)
                    UNION ALL
                    SELECT a.id, a.name, 0
                    FROM artists a
                    WHERE a.normalized_name IN (SELECT normalized_name FROM composite)
                ),
                named AS (
                    SELECT
                        old_id,
                        position,
                        btrim(part, E' \t()[]-_') AS name
                    FROM split
                ),
                keyed AS (
                    SELECT old_id, position, name, regexp_replace(lower(name), '\s+', ' ', 'g') AS norm
                    FROM named
                    WHERE name <> ''
                )
                SELECT DISTINCT ON (old_id, norm) old_id, position, name, norm
                FROM keyed
                ORDER BY old_id, norm, position;
                """);

            // Rows for the performers the library does not have yet.
            migrationBuilder.Sql("""
                INSERT INTO artists (id, name, normalized_name, created_at)
                SELECT DISTINCT ON (p.norm) gen_random_uuid(), p.name, p.norm, now()
                FROM _artist_parts p
                WHERE NOT EXISTS (SELECT 1 FROM artists a WHERE a.normalized_name = p.norm)
                ORDER BY p.norm, p.name;
                """);

            // Credit every track to each performer of the row it used to point at.
            migrationBuilder.Sql("""
                INSERT INTO track_artists (track_id, artist_id, position)
                SELECT t.id, a.id, p.position
                FROM tracks t
                JOIN _artist_parts p ON p.old_id = t.artist_id
                JOIN artists a ON a.normalized_name = p.norm
                ON CONFLICT DO NOTHING;
                """);

            // The first performer becomes the primary credit the track is filed under.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE _artist_primary AS
                SELECT DISTINCT ON (p.old_id) p.old_id, a.id AS new_id
                FROM _artist_parts p
                JOIN artists a ON a.normalized_name = p.norm
                ORDER BY p.old_id, p.position;
                """);

            migrationBuilder.Sql("""
                UPDATE tracks t
                SET artist_id = m.new_id
                FROM _artist_primary m
                WHERE t.artist_id = m.old_id AND t.artist_id <> m.new_id;
                """);

            // Albums move to the same primary artist. Two albums can collide on the unique
            // (artist_id, normalized_title) pair once they land on the same artist — the older
            // row survives and adopts the other's tracks, since one of them has to go.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE _album_target AS
                SELECT al.id, COALESCE(m.new_id, al.artist_id) AS artist_id, al.normalized_title, al.created_at
                FROM albums al
                LEFT JOIN _artist_primary m ON m.old_id = al.artist_id;
                """);

            migrationBuilder.Sql("""
                CREATE TEMP TABLE _album_map AS
                SELECT
                    id AS album_id,
                    first_value(id) OVER (
                        PARTITION BY artist_id, normalized_title ORDER BY created_at, id
                    ) AS survivor_id
                FROM _album_target;
                """);

            migrationBuilder.Sql("""
                UPDATE tracks t
                SET album_id = m.survivor_id
                FROM _album_map m
                WHERE t.album_id = m.album_id AND m.survivor_id <> m.album_id;
                """);

            // A merged-away album may leave its cover file behind; the file is unreferenced,
            // never served, and cheap to leave alone next to losing the album row itself.
            migrationBuilder.Sql("""
                DELETE FROM albums al
                USING _album_map m
                WHERE al.id = m.album_id AND m.survivor_id <> m.album_id;
                """);

            migrationBuilder.Sql("""
                UPDATE albums al
                SET artist_id = tg.artist_id
                FROM _album_target tg
                WHERE tg.id = al.id AND al.artist_id <> tg.artist_id;
                """);

            // The combined rows ("BONES, Grayera") are now unreferenced.
            migrationBuilder.Sql("""
                DELETE FROM artists a
                WHERE NOT EXISTS (SELECT 1 FROM tracks t WHERE t.artist_id = a.id)
                  AND NOT EXISTS (SELECT 1 FROM albums al WHERE al.artist_id = a.id)
                  AND NOT EXISTS (SELECT 1 FROM track_artists ta WHERE ta.artist_id = a.id);
                """);

            migrationBuilder.Sql("DROP TABLE _artist_parts, _artist_primary, _album_target, _album_map;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "track_artists");
        }
    }
}
