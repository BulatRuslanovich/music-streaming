// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileSignalDecay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "positive_signal_mass",
                table: "user_taste_profiles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "signal_decay_anchor",
                table: "user_taste_profiles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Переносим уже накопленную зрелость: иначе после развёртывания каждый существующий
            // профиль откатился бы в Cold и выбирался обратно только новыми прослушиваниями.
            migrationBuilder.Sql("""
                UPDATE user_taste_profiles
                SET positive_signal_mass = positive_signal_count,
                    signal_decay_anchor = updated_at
                WHERE positive_signal_count > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "positive_signal_mass",
                table: "user_taste_profiles");

            migrationBuilder.DropColumn(
                name: "signal_decay_anchor",
                table: "user_taste_profiles");
        }
    }
}
