using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthServer.Sync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreVersionNonce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "nonce",
                schema: "sync",
                table: "store_versions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "nonce",
                schema: "sync",
                table: "store_versions");
        }
    }
}
