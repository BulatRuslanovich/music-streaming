// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendAudioFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_minor",
                table: "track_audio_features",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "key",
                table: "track_audio_features",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "key_strength",
                table: "track_audio_features",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "spectral_rolloff",
                table: "track_audio_features",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double[]>(
                name: "timbre",
                table: "track_audio_features",
                type: "double precision[]",
                nullable: false,
                defaultValue: new double[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_minor",
                table: "track_audio_features");

            migrationBuilder.DropColumn(
                name: "key",
                table: "track_audio_features");

            migrationBuilder.DropColumn(
                name: "key_strength",
                table: "track_audio_features");

            migrationBuilder.DropColumn(
                name: "spectral_rolloff",
                table: "track_audio_features");

            migrationBuilder.DropColumn(
                name: "timbre",
                table: "track_audio_features");
        }
    }
}
