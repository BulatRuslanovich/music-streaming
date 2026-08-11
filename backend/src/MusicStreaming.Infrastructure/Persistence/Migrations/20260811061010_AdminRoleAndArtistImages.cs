using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the administrator flag and the artist photo column.
    ///
    /// The flag is promoted on the oldest account: until now the only way a user row could exist
    /// was <c>DatabaseInitializer.SeedOwnerAsync</c>, so on an existing install the first row is
    /// the owner. A migration cannot read configuration, which is why this keys on creation order
    /// rather than on <c>Owner:Username</c>. On a fresh database the table is empty and the
    /// statement does nothing — the seeder creates the owner with the flag already set.
    /// </summary>
    public partial class AdminRoleAndArtistImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_admin",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "image_path",
                table: "artists",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE users
                SET is_admin = TRUE
                WHERE id = (SELECT id FROM users ORDER BY created_at, id LIMIT 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_admin",
                table: "users");

            migrationBuilder.DropColumn(
                name: "image_path",
                table: "artists");
        }
    }
}
