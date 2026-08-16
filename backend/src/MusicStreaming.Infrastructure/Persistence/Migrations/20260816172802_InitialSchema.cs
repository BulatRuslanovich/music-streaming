using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "artists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    image_path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "genres",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_genres", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trigger = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    candidate_count = table.Column<int>(type: "integer", nullable: false),
                    shelf_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_admin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "albums",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    normalized_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true),
                    cover_path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_albums", x => x.id);
                    table.ForeignKey(
                        name: "fk_albums_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "playlists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cover_path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlists", x => x.id);
                    table.ForeignKey(
                        name: "fk_playlists_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_cache",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shelf_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation_cache", x => new { x.user_id, x.shelf_key });
                    table.ForeignKey(
                        name: "fk_recommendation_cache_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_artist_affinity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    play_count = table.Column<int>(type: "integer", nullable: false),
                    skip_count = table.Column<int>(type: "integer", nullable: false),
                    decayed_weight = table.Column<double>(type: "double precision", nullable: false),
                    decay_anchor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    last_played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_artist_affinity", x => new { x.user_id, x.artist_id });
                    table.ForeignKey(
                        name: "fk_user_artist_affinity_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_artist_affinity_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_genre_affinity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    genre_id = table.Column<Guid>(type: "uuid", nullable: false),
                    play_count = table.Column<int>(type: "integer", nullable: false),
                    skip_count = table.Column<int>(type: "integer", nullable: false),
                    decayed_weight = table.Column<double>(type: "double precision", nullable: false),
                    decay_anchor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    last_played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_genre_affinity", x => new { x.user_id, x.genre_id });
                    table.ForeignKey(
                        name: "fk_user_genre_affinity_genres_genre_id",
                        column: x => x.genre_id,
                        principalTable: "genres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_genre_affinity_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
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

            migrationBuilder.CreateTable(
                name: "user_taste_profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    positive_signal_count = table.Column<int>(type: "integer", nullable: false),
                    total_event_count = table.Column<int>(type: "integer", nullable: false),
                    total_listening_seconds = table.Column<long>(type: "bigint", nullable: false),
                    average_completion = table.Column<double>(type: "double precision", nullable: false),
                    skip_rate = table.Column<double>(type: "double precision", nullable: false),
                    distinct_tracks = table.Column<int>(type: "integer", nullable: false),
                    distinct_artists = table.Column<int>(type: "integer", nullable: false),
                    year_center = table.Column<double>(type: "double precision", nullable: true),
                    year_spread = table.Column<double>(type: "double precision", nullable: false),
                    top_artists = table.Column<string>(type: "jsonb", nullable: false),
                    top_genres = table.Column<string>(type: "jsonb", nullable: false),
                    maturity = table.Column<int>(type: "integer", nullable: false),
                    events_watermark = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_taste_profiles", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_taste_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tracks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    normalized_title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    album_id = table.Column<Guid>(type: "uuid", nullable: true),
                    genre_id = table.Column<Guid>(type: "uuid", nullable: true),
                    track_number = table.Column<int>(type: "integer", nullable: true),
                    disc_number = table.Column<int>(type: "integer", nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    file_path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    codec = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    bitrate_kbps = table.Column<int>(type: "integer", nullable: true),
                    sample_rate_hz = table.Column<int>(type: "integer", nullable: true),
                    bits_per_sample = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracks", x => x.id);
                    table.ForeignKey(
                        name: "fk_tracks_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "albums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tracks_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tracks_genres_genre_id",
                        column: x => x.genre_id,
                        principalTable: "genres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "favorites",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favorites", x => new { x.user_id, x.track_id });
                    table.ForeignKey(
                        name: "fk_favorites_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_favorites_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listening_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    playback_position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listening_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_listening_history_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_listening_history_users_user_id",
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
                name: "playback_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    position_seconds = table.Column<int>(type: "integer", nullable: false),
                    listened_seconds = table.Column<int>(type: "integer", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playback_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_playback_events_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_playback_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlist_tracks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    playlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlist_tracks", x => x.id);
                    table.ForeignKey(
                        name: "fk_playlist_tracks_playlists_playlist_id",
                        column: x => x.playlist_id,
                        principalTable: "playlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_playlist_tracks_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_impressions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shelf_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    shown_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clicked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation_impressions", x => x.id);
                    table.ForeignKey(
                        name: "fk_recommendation_impressions_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recommendation_impressions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_artists",
                columns: table => new
                {
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_artists", x => new { x.track_id, x.artist_id });
                    table.ForeignKey(
                        name: "fk_track_artists_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_track_artists_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
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
                name: "track_similarity",
                columns: table => new
                {
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    similar_track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    content_score = table.Column<double>(type: "double precision", nullable: false),
                    collab_score = table.Column<double>(type: "double precision", nullable: false),
                    support = table.Column<int>(type: "integer", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_similarity", x => new { x.track_id, x.similar_track_id });
                    table.ForeignKey(
                        name: "fk_track_similarity_tracks_similar_track_id",
                        column: x => x.similar_track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_track_similarity_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_stats",
                columns: table => new
                {
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    play_count = table.Column<int>(type: "integer", nullable: false),
                    play_count30d = table.Column<int>(type: "integer", nullable: false),
                    skip_count = table.Column<int>(type: "integer", nullable: false),
                    distinct_listeners = table.Column<int>(type: "integer", nullable: false),
                    completion_rate = table.Column<double>(type: "double precision", nullable: false),
                    skip_rate = table.Column<double>(type: "double precision", nullable: false),
                    popularity_score = table.Column<double>(type: "double precision", nullable: false),
                    last_played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_stats", x => x.track_id);
                    table.ForeignKey(
                        name: "fk_track_stats_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_track_affinity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    play_count = table.Column<int>(type: "integer", nullable: false),
                    completed_count = table.Column<int>(type: "integer", nullable: false),
                    skip_count = table.Column<int>(type: "integer", nullable: false),
                    replay_count = table.Column<int>(type: "integer", nullable: false),
                    queue_adds = table.Column<int>(type: "integer", nullable: false),
                    playlist_adds = table.Column<int>(type: "integer", nullable: false),
                    total_listened_seconds = table.Column<long>(type: "bigint", nullable: false),
                    completion_sum = table.Column<double>(type: "double precision", nullable: false),
                    completion_samples = table.Column<int>(type: "integer", nullable: false),
                    decayed_weight = table.Column<double>(type: "double precision", nullable: false),
                    decay_anchor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    first_played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_track_affinity", x => new { x.user_id, x.track_id });
                    table.ForeignKey(
                        name: "fk_user_track_affinity_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_track_affinity_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_albums_artist_id_normalized_title",
                table: "albums",
                columns: new[] { "artist_id", "normalized_title" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_albums_created_at",
                table: "albums",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_albums_title",
                table: "albums",
                column: "title");

            migrationBuilder.CreateIndex(
                name: "ix_artists_name",
                table: "artists",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_artists_normalized_name",
                table: "artists",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_favorites_track_id",
                table: "favorites",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_favorites_user_id_created_at",
                table: "favorites",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_genres_normalized_name",
                table: "genres",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listening_history_played_at",
                table: "listening_history",
                column: "played_at");

            migrationBuilder.CreateIndex(
                name: "ix_listening_history_track_id",
                table: "listening_history",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_listening_history_user_id_played_at",
                table: "listening_history",
                columns: new[] { "user_id", "played_at" });

            migrationBuilder.CreateIndex(
                name: "ix_listening_history_user_id_track_id_played_at",
                table: "listening_history",
                columns: new[] { "user_id", "track_id", "played_at" });

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

            migrationBuilder.CreateIndex(
                name: "ix_playback_events_occurred_at",
                table: "playback_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_playback_events_session_id_occurred_at",
                table: "playback_events",
                columns: new[] { "session_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_playback_events_track_id",
                table: "playback_events",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_playback_events_user_id_occurred_at",
                table: "playback_events",
                columns: new[] { "user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_playback_events_user_id_sequence",
                table: "playback_events",
                columns: new[] { "user_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_playback_events_user_id_track_id_occurred_at",
                table: "playback_events",
                columns: new[] { "user_id", "track_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_playlist_tracks_playlist_id_position",
                table: "playlist_tracks",
                columns: new[] { "playlist_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_playlist_tracks_track_id",
                table: "playlist_tracks",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_playlists_created_at",
                table: "playlists",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_playlists_is_public_updated_at",
                table: "playlists",
                columns: new[] { "is_public", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_playlists_user_id_name",
                table: "playlists",
                columns: new[] { "user_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_cache_expires_at",
                table: "recommendation_cache",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_cache_user_id_position",
                table: "recommendation_cache",
                columns: new[] { "user_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_impressions_shown_at",
                table: "recommendation_impressions",
                column: "shown_at");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_impressions_track_id",
                table: "recommendation_impressions",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_impressions_user_id_shelf_key_shown_at",
                table: "recommendation_impressions",
                columns: new[] { "user_id", "shelf_key", "shown_at" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_impressions_user_id_track_id_shown_at",
                table: "recommendation_impressions",
                columns: new[] { "user_id", "track_id", "shown_at" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_runs_started_at",
                table: "recommendation_runs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_runs_user_id_started_at",
                table: "recommendation_runs",
                columns: new[] { "user_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_artists_artist_id",
                table: "track_artists",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_artists_track_id_position",
                table: "track_artists",
                columns: new[] { "track_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_track_similarity_similar_track_id",
                table: "track_similarity",
                column: "similar_track_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_similarity_track_id_score",
                table: "track_similarity",
                columns: new[] { "track_id", "score" });

            migrationBuilder.CreateIndex(
                name: "ix_track_stats_popularity_score",
                table: "track_stats",
                column: "popularity_score");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_album_id",
                table: "tracks",
                column: "album_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_artist_id",
                table: "tracks",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_content_hash",
                table: "tracks",
                column: "content_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracks_created_at",
                table: "tracks",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_file_path",
                table: "tracks",
                column: "file_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracks_genre_id",
                table: "tracks",
                column: "genre_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_title",
                table: "tracks",
                column: "title");

            migrationBuilder.CreateIndex(
                name: "ix_user_artist_affinity_artist_id",
                table: "user_artist_affinity",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_artist_affinity_user_id_score",
                table: "user_artist_affinity",
                columns: new[] { "user_id", "score" });

            migrationBuilder.CreateIndex(
                name: "ix_user_genre_affinity_genre_id",
                table: "user_genre_affinity",
                column: "genre_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_genre_affinity_user_id_score",
                table: "user_genre_affinity",
                columns: new[] { "user_id", "score" });

            migrationBuilder.CreateIndex(
                name: "ix_user_track_affinity_track_id",
                table: "user_track_affinity",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_track_affinity_user_id_last_played_at",
                table: "user_track_affinity",
                columns: new[] { "user_id", "last_played_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_track_affinity_user_id_score",
                table: "user_track_affinity",
                columns: new[] { "user_id", "score" });

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);

            CreateSearchObjects(migrationBuilder);
        }

        /// <summary>
        /// То, чего в модели EF Core нет и потому не будет ни в одной сгенерированной миграции.
        ///
        /// <para>
        /// Поиск ищет незаякоренным <c>LIKE '%term%'</c>, который обычный B-tree обслужить не может:
        /// без помощи любой запрос вырождается в последовательный проход по всей библиотеке.
        /// Держат его отзывчивым GIN-индексы <c>pg_trgm</c>, а класс операторов
        /// (<c>gin_trgm_ops</c>) в модели никак не выражается.
        /// </para>
        ///
        /// <para>
        /// Функция <c>search_rank</c> — правило ранжирования, одно на исполнителей, альбомы, треки и
        /// жанры. Записанное на C# в каждом из четырёх запросов, оно рано или поздно разошлось бы; в
        /// базе же оно попадает прямо в <c>ORDER BY</c>, и сортировать в памяти не приходится.
        /// Сравнивается с уже нормализованными колонками, поэтому ни приведения регистра, ни
        /// <c>LIKE</c> здесь нет: <c>starts_with</c> и <c>position</c> работают с текстом буквально,
        /// а значит запрос со знаком процента ищется как обычный текст и экранировать в нём нечего.
        /// Ранг 4 достаётся найденному по смежному полю — треку, совпавшему именем исполнителя, а не
        /// названием.
        /// </para>
        /// </summary>
        private static void CreateSearchObjects(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_artists_normalized_name_trgm
                    ON artists USING gin (normalized_name gin_trgm_ops);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_albums_normalized_title_trgm
                    ON albums USING gin (normalized_title gin_trgm_ops);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_tracks_normalized_title_trgm
                    ON tracks USING gin (normalized_title gin_trgm_ops);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_genres_normalized_name_trgm
                    ON genres USING gin (normalized_name gin_trgm_ops);
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION search_rank(value text, term text) RETURNS integer AS $$
                    SELECT CASE
                        WHEN value = term                                    THEN 0
                        WHEN starts_with(value, term)                        THEN 1
                        WHEN position(' ' || term in ' ' || value) > 0       THEN 2
                        WHEN position(term in value) > 0                     THEN 3
                        ELSE 4
                    END;
                $$ LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS search_rank(text, text);");

            // Триграммные индексы уходят вместе со своими таблицами, а расширение остаётся: от него
            // могут зависеть другие объекты, и удаление общего расширения куда разрушительнее.

            migrationBuilder.DropTable(
                name: "favorites");

            migrationBuilder.DropTable(
                name: "lastfm_accounts");

            migrationBuilder.DropTable(
                name: "listening_history");

            migrationBuilder.DropTable(
                name: "listening_stats");

            migrationBuilder.DropTable(
                name: "outbound_jobs");

            migrationBuilder.DropTable(
                name: "playback_events");

            migrationBuilder.DropTable(
                name: "playlist_tracks");

            migrationBuilder.DropTable(
                name: "recommendation_cache");

            migrationBuilder.DropTable(
                name: "recommendation_impressions");

            migrationBuilder.DropTable(
                name: "recommendation_runs");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "track_artists");

            migrationBuilder.DropTable(
                name: "track_lyrics");

            migrationBuilder.DropTable(
                name: "track_similarity");

            migrationBuilder.DropTable(
                name: "track_stats");

            migrationBuilder.DropTable(
                name: "user_artist_affinity");

            migrationBuilder.DropTable(
                name: "user_genre_affinity");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropTable(
                name: "user_taste_profiles");

            migrationBuilder.DropTable(
                name: "user_track_affinity");

            migrationBuilder.DropTable(
                name: "playlists");

            migrationBuilder.DropTable(
                name: "tracks");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "albums");

            migrationBuilder.DropTable(
                name: "genres");

            migrationBuilder.DropTable(
                name: "artists");
        }
    }
}
