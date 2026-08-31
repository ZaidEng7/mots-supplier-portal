using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationOrgUnitSupplierOrgLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organization");

            migrationBuilder.CreateTable(
                name: "organization",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LegalNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrganizationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SyncStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "org_unit",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentOrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_unit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_org_unit_org_unit_ParentOrgUnitId",
                        column: x => x.ParentOrgUnitId,
                        principalSchema: "organization",
                        principalTable: "org_unit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_org_unit_organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_org_link",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_org_link", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_org_link_organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_org_link_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_org_unit_OrganizationId",
                schema: "organization",
                table: "org_unit",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_org_unit_ParentOrgUnitId",
                schema: "organization",
                table: "org_unit",
                column: "ParentOrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_org_link_OrganizationId",
                schema: "organization",
                table: "supplier_org_link",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_org_link_SupplierId_OrganizationId",
                schema: "organization",
                table: "supplier_org_link",
                columns: new[] { "SupplierId", "OrganizationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "org_unit",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "supplier_org_link",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "organization",
                schema: "organization");
        }
    }
}
