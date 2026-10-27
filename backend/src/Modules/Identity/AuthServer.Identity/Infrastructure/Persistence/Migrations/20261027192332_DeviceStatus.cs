using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthServer.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "firmware",
                schema: "identity",
                table: "devices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "identity",
                table: "devices",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "firmware",
                schema: "identity",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "identity",
                table: "devices");
        }
    }
}
