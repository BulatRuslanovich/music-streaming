using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ListeningStatsLyricsAndIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "lastfm_accounts",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    session_key = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_scrobble_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lastfm_accounts", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_lastfm_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listening_stats",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hour = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    play_count = table.Column<int>(type: "integer", nullable: false),
                    listened_seconds = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listening_stats", x => new { x.user_id, x.hour, x.track_id });
                    table.ForeignKey(
                        name: "fk_listening_stats_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_listening_stats_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outbound_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", maxLength: 512, nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_outbound_jobs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_lyrics",
                columns: table => new
                {
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plain = table.Column<string>(type: "text", maxLength: 20000, nullable: false),
                    synced = table.Column<string>(type: "jsonb", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_lyrics", x => x.track_id);
                    table.ForeignKey(
                        name: "fk_track_lyrics_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    autoplay = table.Column<bool>(type: "boolean", nullable: false),
                    quality = table.Column<int>(type: "integer", nullable: false),
                    data_saver = table.Column<bool>(type: "boolean", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_settings", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_listening_stats_track_id",
                table: "listening_stats",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_jobs_dedupe_key",
                table: "outbound_jobs",
                column: "dedupe_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_jobs_state_next_attempt_at",
                table: "outbound_jobs",
                columns: new[] { "state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_jobs_user_id",
                table: "outbound_jobs",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lastfm_accounts");

            migrationBuilder.DropTable(
                name: "listening_stats");

            migrationBuilder.DropTable(
                name: "outbound_jobs");

            migrationBuilder.DropTable(
                name: "track_lyrics");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "users");
        }
    }
}
