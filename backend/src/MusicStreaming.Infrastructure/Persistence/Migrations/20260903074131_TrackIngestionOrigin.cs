// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackIngestionOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "added_by_user_id",
                table: "tracks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ingestion_source",
                table: "tracks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_tracks_added_by_user_id_created_at",
                table: "tracks",
                columns: new[] { "added_by_user_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_tracks_users_added_by_user_id",
                table: "tracks",
                column: "added_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tracks_users_added_by_user_id",
                table: "tracks");

            migrationBuilder.DropIndex(
                name: "ix_tracks_added_by_user_id_created_at",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "added_by_user_id",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "ingestion_source",
                table: "tracks");
        }
    }
}
