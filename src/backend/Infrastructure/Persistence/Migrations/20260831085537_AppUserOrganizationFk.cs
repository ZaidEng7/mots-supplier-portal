using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppUserOrganizationFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_app_user_OrgUnitId",
                schema: "identity",
                table: "app_user",
                column: "OrgUnitId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_app_user_supplier_xor_organization",
                schema: "identity",
                table: "app_user",
                sql: "\"SupplierId\" IS NULL OR \"OrganizationId\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_app_user_org_unit_OrgUnitId",
                schema: "identity",
                table: "app_user",
                column: "OrgUnitId",
                principalSchema: "organization",
                principalTable: "org_unit",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_app_user_organization_OrganizationId",
                schema: "identity",
                table: "app_user",
                column: "OrganizationId",
                principalSchema: "organization",
                principalTable: "organization",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_app_user_org_unit_OrgUnitId",
                schema: "identity",
                table: "app_user");

            migrationBuilder.DropForeignKey(
                name: "FK_app_user_organization_OrganizationId",
                schema: "identity",
                table: "app_user");

            migrationBuilder.DropIndex(
                name: "IX_app_user_OrgUnitId",
                schema: "identity",
                table: "app_user");

            migrationBuilder.DropCheckConstraint(
                name: "CK_app_user_supplier_xor_organization",
                schema: "identity",
                table: "app_user");
        }
    }
}
