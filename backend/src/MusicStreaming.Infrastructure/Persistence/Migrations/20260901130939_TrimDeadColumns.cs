// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrimDeadColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recommendation_runs_user_id_started_at",
                table: "recommendation_runs");

            migrationBuilder.DropColumn(
                name: "queue_adds",
                table: "user_track_affinity");

            migrationBuilder.DropColumn(
                name: "distinct_artists",
                table: "user_taste_profiles");

            migrationBuilder.DropColumn(
                name: "total_listening_seconds",
                table: "user_taste_profiles");

            migrationBuilder.DropColumn(
                name: "completion_rate",
                table: "track_stats");

            migrationBuilder.DropColumn(
                name: "distinct_listeners",
                table: "track_stats");

            migrationBuilder.DropColumn(
                name: "play_count30d",
                table: "track_stats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "queue_adds",
                table: "user_track_affinity",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "distinct_artists",
                table: "user_taste_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "total_listening_seconds",
                table: "user_taste_profiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<double>(
                name: "completion_rate",
                table: "track_stats",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "distinct_listeners",
                table: "track_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "play_count30d",
                table: "track_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_runs_user_id_started_at",
                table: "recommendation_runs",
                columns: new[] { "user_id", "started_at" });
        }
    }
}
