// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "audio_score",
                table: "track_similarity",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "track_audio_features",
                columns: table => new
                {
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tempo_bpm = table.Column<double>(type: "double precision", nullable: true),
                    tempo_confidence = table.Column<double>(type: "double precision", nullable: false),
                    energy = table.Column<double>(type: "double precision", nullable: false),
                    loudness_db = table.Column<double>(type: "double precision", nullable: false),
                    brightness = table.Column<double>(type: "double precision", nullable: false),
                    dynamic_range_db = table.Column<double>(type: "double precision", nullable: false),
                    analyzed_seconds = table.Column<double>(type: "double precision", nullable: false),
                    algorithm_version = table.Column<int>(type: "integer", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    analyzed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_audio_features", x => x.track_id);
                    table.ForeignKey(
                        name: "fk_track_audio_features_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_track_audio_features_analyzed_at",
                table: "track_audio_features",
                column: "analyzed_at");

            migrationBuilder.CreateIndex(
                name: "ix_track_audio_features_succeeded_algorithm_version",
                table: "track_audio_features",
                columns: new[] { "succeeded", "algorithm_version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "track_audio_features");

            migrationBuilder.DropColumn(
                name: "audio_score",
                table: "track_similarity");
        }
    }
}
