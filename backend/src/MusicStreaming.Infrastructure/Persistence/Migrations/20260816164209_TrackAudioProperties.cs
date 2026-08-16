using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicStreaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackAudioProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "bitrate_kbps",
                table: "tracks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bits_per_sample",
                table: "tracks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codec",
                table: "tracks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sample_rate_hz",
                table: "tracks",
                type: "integer",
                nullable: true);

            // До этой миграции принимался ровно один формат, поэтому кодек прошлых загрузок известен
            // точно. Остальные три колонки остаются пустыми: чтобы их заполнить, пришлось бы
            // перечитать каждый файл на диске ради подписи в интерфейсе, а она и так скрывается,
            // когда данных нет.
            migrationBuilder.Sql("UPDATE tracks SET codec = 'mp3';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bitrate_kbps",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "bits_per_sample",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "codec",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "sample_rate_hz",
                table: "tracks");
        }
    }
}
