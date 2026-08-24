// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    public partial class AddTrackShuffleKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "shuffle_key",
                table: "tracks",
                type: "double precision",
                nullable: false,
                defaultValueSql: "random()");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_shuffle_key",
                table: "tracks",
                column: "shuffle_key");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tracks_shuffle_key",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "shuffle_key",
                table: "tracks");
        }
    }
}
