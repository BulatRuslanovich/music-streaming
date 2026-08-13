using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Добавляет признак администратора и колонку фотографии исполнителя.
    ///
    /// Признак выставляется самой старой учётной записи: до сих пор строка пользователя могла
    /// появиться только через <c>DatabaseInitializer.SeedOwnerAsync</c>, поэтому на существующей
    /// установке первая строка — это владелец. Миграция не может читать конфигурацию, отчего опорой
    /// служит порядок создания, а не <c>Owner:Username</c>. На свежей базе таблица пуста и оператор
    /// ничего не делает — сидер создаёт владельца уже с выставленным признаком.
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
