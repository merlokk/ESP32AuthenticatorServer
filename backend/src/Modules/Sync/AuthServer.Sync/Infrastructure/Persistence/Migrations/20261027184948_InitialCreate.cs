using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthServer.Sync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sync");

            migrationBuilder.CreateTable(
                name: "stores",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    seed_id = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    head_version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "store_versions",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_ref = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    modified = table.Column<long>(type: "bigint", nullable: false),
                    cipher = table.Column<short>(type: "smallint", nullable: false),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    kdf_iterations = table.Column<long>(type: "bigint", nullable: false),
                    key_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    uploaded_by_device = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_store_versions_stores",
                        column: x => x.store_ref,
                        principalSchema: "sync",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_store_versions_store_version",
                schema: "sync",
                table: "store_versions",
                columns: new[] { "store_ref", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_stores_user_store_seed_kind",
                schema: "sync",
                table: "stores",
                columns: new[] { "user_id", "store_id", "seed_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_versions",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "stores",
                schema: "sync");
        }
    }
}
