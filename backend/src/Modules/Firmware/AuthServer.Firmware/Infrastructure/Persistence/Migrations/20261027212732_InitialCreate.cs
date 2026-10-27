using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthServer.Firmware.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "firmware");

            migrationBuilder.CreateTable(
                name: "releases",
                schema: "firmware",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    board = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    built = table.Column<string>(type: "character varying(33)", maxLength: 33, nullable: false),
                    idf = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_releases", x => x.id);
                });

            // Uncompressed TOAST: an OTA Range reads its slice, not the whole image (substring).
            migrationBuilder.Sql("alter table firmware.releases alter column content set storage external;");

            migrationBuilder.CreateIndex(
                name: "ux_releases_board_active",
                schema: "firmware",
                table: "releases",
                column: "board",
                unique: true,
                filter: "active");

            migrationBuilder.CreateIndex(
                name: "ux_releases_board_version",
                schema: "firmware",
                table: "releases",
                columns: new[] { "board", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "releases",
                schema: "firmware");
        }
    }
}
