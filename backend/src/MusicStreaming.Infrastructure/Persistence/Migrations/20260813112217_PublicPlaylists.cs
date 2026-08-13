using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Делает плейлист публикуемым. Существующие плейлисты остаются приватными: у столбца значение
    /// по умолчанию <c>false</c>, поэтому миграция ничего никому не открывает. Индекс по
    /// <c>(is_public, updated_at)</c> обслуживает витрину публичных плейлистов, которая читает их
    /// без привязки к владельцу и сортирует по времени изменения.
    /// </summary>
    public partial class PublicPlaylists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_public",
                table: "playlists",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_playlists_is_public_updated_at",
                table: "playlists",
                columns: new[] { "is_public", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_playlists_is_public_updated_at",
                table: "playlists");

            migrationBuilder.DropColumn(
                name: "is_public",
                table: "playlists");
        }
    }
}
