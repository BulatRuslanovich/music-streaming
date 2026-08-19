// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniquePlaylistTrack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Дубликаты, накопленные до появления ограничения, уходят: позиция считалась
            // отдельным запросом, и два одновременных добавления одного трека обе строки
            // проходили. Остаётся самая ранняя — та, которой человек и добавлял трек;
            // без этого CREATE UNIQUE INDEX упал бы на первой же такой паре.
            migrationBuilder.Sql("""
                DELETE FROM playlist_tracks pt
                USING playlist_tracks keep
                WHERE pt.playlist_id = keep.playlist_id
                  AND pt.track_id = keep.track_id
                  AND (keep.position, keep.added_at, keep.id) < (pt.position, pt.added_at, pt.id);
                """);

            // Нумерация после удаления дублей могла остаться с дырами — они безвредны
            // (порядок задаётся сравнением, а не значением), но сомкнуть их дешевле сейчас,
            // чем объяснять потом, почему позиции идут 0, 1, 3.
            migrationBuilder.Sql("""
                UPDATE playlist_tracks pt
                SET position = ranked.position
                FROM (
                    SELECT id,
                           (ROW_NUMBER() OVER (PARTITION BY playlist_id ORDER BY position, added_at, id) - 1)
                               AS position
                    FROM playlist_tracks
                ) ranked
                WHERE pt.id = ranked.id AND pt.position <> ranked.position;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_playlist_tracks_playlist_id_track_id",
                table: "playlist_tracks",
                columns: new[] { "playlist_id", "track_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_playlist_tracks_playlist_id_track_id",
                table: "playlist_tracks");
        }
    }
}
