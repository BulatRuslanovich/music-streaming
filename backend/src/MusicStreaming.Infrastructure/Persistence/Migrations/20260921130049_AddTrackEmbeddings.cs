// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "shown_count",
                table: "track_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "skipped_early_count",
                table: "track_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "track_embeddings",
                columns: table => new
                {
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vector = table.Column<float[]>(type: "real[]", nullable: false),
                    dimension = table.Column<int>(type: "integer", nullable: false),
                    model_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    strategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cluster_id = table.Column<int>(type: "integer", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    analyzed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_embeddings", x => x.track_id);
                    table.ForeignKey(
                        name: "fk_track_embeddings_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_track_embeddings_analyzed_at",
                table: "track_embeddings",
                column: "analyzed_at");

            migrationBuilder.CreateIndex(
                name: "ix_track_embeddings_cluster_id",
                table: "track_embeddings",
                column: "cluster_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_embeddings_succeeded_model_id_strategy",
                table: "track_embeddings",
                columns: new[] { "succeeded", "model_id", "strategy" });

            // 512 float это шум: LZ его не сжимает, а циклы на попытку тратятся на каждой записи
            // и каждом чтении. EXTERNAL отправляет значение в TOAST без компрессии.
            migrationBuilder.Sql("ALTER TABLE track_embeddings ALTER COLUMN vector SET STORAGE EXTERNAL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "track_embeddings");

            migrationBuilder.DropColumn(
                name: "shown_count",
                table: "track_stats");

            migrationBuilder.DropColumn(
                name: "skipped_early_count",
                table: "track_stats");
        }
    }
}
