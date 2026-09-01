// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "tags_fetched_at",
                table: "tracks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "tags_fetched_at",
                table: "artists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "artist_tags",
                columns: table => new
                {
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    weight = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artist_tags", x => new { x.artist_id, x.name });
                    table.ForeignKey(
                        name: "fk_artist_tags_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_tags",
                columns: table => new
                {
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    weight = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_tags", x => new { x.track_id, x.name });
                    table.ForeignKey(
                        name: "fk_track_tags_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_artist_tags_name",
                table: "artist_tags",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_track_tags_name",
                table: "track_tags",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artist_tags");

            migrationBuilder.DropTable(
                name: "track_tags");

            migrationBuilder.DropColumn(
                name: "tags_fetched_at",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "tags_fetched_at",
                table: "artists");
        }
    }
}
