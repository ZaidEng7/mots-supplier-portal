using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmailTemplateOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_template_override",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubjectAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SubjectEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    BodyEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_template_override", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_template_override_Key",
                schema: "ops",
                table: "email_template_override",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_template_override",
                schema: "ops");
        }
    }
}
