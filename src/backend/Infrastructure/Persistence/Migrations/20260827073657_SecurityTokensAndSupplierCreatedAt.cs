using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecurityTokensAndSupplierCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "supplier",
                table: "supplier",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Existing rows get "now", not the 0001-01-01 default - DraftCleanupJob treats an
            // ancient CreatedAt as an abandoned draft eligible for deletion, and pre-existing dev
            // data (or any Draft row from before this migration) shouldn't be nuked on first run.
            migrationBuilder.Sql("UPDATE supplier.supplier SET \"CreatedAt\" = now() WHERE \"CreatedAt\" = '-infinity';");

            migrationBuilder.CreateTable(
                name: "security_token",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_token", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_security_token_TokenHash",
                schema: "identity",
                table: "security_token",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_security_token_UserId",
                schema: "identity",
                table: "security_token",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "security_token",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "supplier",
                table: "supplier");
        }
    }
}
