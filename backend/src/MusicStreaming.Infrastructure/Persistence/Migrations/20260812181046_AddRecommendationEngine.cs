using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Добавляет таблицы, на которых работает движок рекомендаций. Ничего существующего не
    /// затрагивается: <c>listening_history</c> по-прежнему питает «недавно прослушанное».
    ///
    /// Разделение сделано намеренно. <c>playback_events</c> — сырой сигнал только на дозапись,
    /// который чистится по расписанию; таблицы аффинити, профиля и статистики — долговечные роллапы,
    /// поэтому чистка событий никогда не стоит пользователю накопленного вкуса. Именно этот изъян
    /// делал <c>listening_history</c> (перезаписываемую в 30-минутном окне и обрезаемую до 1000
    /// свежих строк) непригодной как вход для рекомендаций.
    ///
    /// Все внешние ключи каскадные: удаление трека или учётной записи не должно оставлять висящими
    /// строки аффинити, закэшированные полки и показы. Единственное исключение —
    /// <c>recommendation_runs</c>: это журнал аудита, у него намеренно нет внешнего ключа, поэтому
    /// он переживает то, что описывает.
    /// </summary>
    public partial class AddRecommendationEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "playback_events");

            migrationBuilder.DropTable(
                name: "recommendation_cache");

            migrationBuilder.DropTable(
                name: "recommendation_impressions");

            migrationBuilder.DropTable(
                name: "recommendation_runs");

            migrationBuilder.DropTable(
                name: "track_similarity");

            migrationBuilder.DropTable(
                name: "track_stats");

            migrationBuilder.DropTable(
                name: "user_artist_affinity");

            migrationBuilder.DropTable(
                name: "user_genre_affinity");

            migrationBuilder.DropTable(
                name: "user_taste_profiles");

            migrationBuilder.DropTable(
                name: "user_track_affinity");
        }
    }
}
