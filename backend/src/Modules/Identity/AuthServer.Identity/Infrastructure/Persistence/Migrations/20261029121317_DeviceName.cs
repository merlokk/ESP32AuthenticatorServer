using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthServer.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name",
                schema: "identity",
                table: "devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "name",
                schema: "identity",
                table: "devices");
        }
    }
}
