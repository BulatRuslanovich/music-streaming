// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTasteVectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_taste_vectors",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    context = table.Column<int>(type: "integer", nullable: false),
                    vector = table.Column<float[]>(type: "real[]", nullable: false),
                    dimension = table.Column<int>(type: "integer", nullable: false),
                    positive_count = table.Column<int>(type: "integer", nullable: false),
                    negative_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_taste_vectors", x => new { x.user_id, x.context });
                    table.ForeignKey(
                        name: "fk_user_taste_vectors_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_taste_vectors");
        }
    }
}
