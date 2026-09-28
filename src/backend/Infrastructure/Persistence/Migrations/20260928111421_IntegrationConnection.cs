using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_connection",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SecretCipher = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SecretSetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastTestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "boolean", nullable: true),
                    LastTestDetail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_connection", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "ops",
                table: "integration_connection",
                columns: new[] { "Id", "ApiKey", "BaseUrl", "DisplayName", "IsEnabled", "Key", "LastTestDetail", "LastTestSucceeded", "LastTestedAt", "SecretCipher", "SecretSetAt", "UpdatedAt", "UpdatedByUserId" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000901"), "", "", "Seven Gates ERP", false, "erp", null, null, null, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_integration_connection_Key",
                schema: "ops",
                table: "integration_connection",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_connection",
                schema: "ops");
        }
    }
}
