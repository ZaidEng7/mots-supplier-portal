using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierProfileExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "City",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.RenameColumn(
                name: "Country",
                schema: "supplier",
                table: "supplier",
                newName: "SupplierGroup");

            migrationBuilder.RenameColumn(
                name: "AddressLine",
                schema: "supplier",
                table: "supplier",
                newName: "Website");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalId",
                schema: "supplier",
                table: "supplier",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "supplier",
                table: "supplier",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EstablishedOn",
                schema: "supplier",
                table: "supplier",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncedAt",
                schema: "supplier",
                table: "supplier",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalNameAr",
                schema: "supplier",
                table: "supplier",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalNameEn",
                schema: "supplier",
                table: "supplier",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoStorageKey",
                schema: "supplier",
                table: "supplier",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierType",
                schema: "supplier",
                table: "supplier",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncStatus",
                schema: "supplier",
                table: "supplier",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.CreateTable(
                name: "address",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Line1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Line2 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RegionCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_address", x => x.Id);
                    table.ForeignKey(
                        name: "FK_address_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_account",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountHolderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BranchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EncryptedAccountNumber = table.Column<byte[]>(type: "bytea", nullable: false),
                    MaskedAccountNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SwiftBic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_account", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bank_account_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "branch",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_branch_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "category",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "category_link",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category_link", x => x.Id);
                    table.ForeignKey(
                        name: "FK_category_link_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contact_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "region",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_region", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "reference",
                table: "category",
                columns: new[] { "Id", "Code", "IsActive", "NameAr", "NameEn" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000301"), "accommodation", true, "الإقامة والفنادق", "Accommodation & Hotels" },
                    { new Guid("00000000-0000-0000-0000-000000000302"), "catering", true, "التموين والضيافة", "Catering & Hospitality" },
                    { new Guid("00000000-0000-0000-0000-000000000303"), "transport", true, "النقل والمواصلات", "Transport" },
                    { new Guid("00000000-0000-0000-0000-000000000304"), "tour_operations", true, "تنظيم الرحلات السياحية", "Tour Operations" },
                    { new Guid("00000000-0000-0000-0000-000000000305"), "events", true, "تنظيم الفعاليات", "Events & Conferences" },
                    { new Guid("00000000-0000-0000-0000-000000000306"), "maintenance", true, "الصيانة والخدمات الفنية", "Maintenance & Technical Services" }
                });

            migrationBuilder.InsertData(
                schema: "reference",
                table: "region",
                columns: new[] { "Id", "Code", "IsActive", "NameAr", "NameEn" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000201"), "DIM", true, "دمشق", "Damascus" },
                    { new Guid("00000000-0000-0000-0000-000000000202"), "ALP", true, "حلب", "Aleppo" },
                    { new Guid("00000000-0000-0000-0000-000000000203"), "LAT", true, "اللاذقية", "Latakia" },
                    { new Guid("00000000-0000-0000-0000-000000000204"), "HOM", true, "حمص", "Homs" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_address_SupplierId",
                schema: "supplier",
                table: "address",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_bank_account_SupplierId",
                schema: "supplier",
                table: "bank_account",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_branch_SupplierId",
                schema: "supplier",
                table: "branch",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_category_Code",
                schema: "reference",
                table: "category",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_category_link_SupplierId_CategoryCode",
                schema: "supplier",
                table: "category_link",
                columns: new[] { "SupplierId", "CategoryCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contact_SupplierId",
                schema: "supplier",
                table: "contact",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_region_Code",
                schema: "reference",
                table: "region",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "address",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "bank_account",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "branch",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "category",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "category_link",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "contact",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "region",
                schema: "reference");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "EstablishedOn",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "LegalNameAr",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "LegalNameEn",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "LogoStorageKey",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "SupplierType",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "SyncStatus",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.RenameColumn(
                name: "Website",
                schema: "supplier",
                table: "supplier",
                newName: "AddressLine");

            migrationBuilder.RenameColumn(
                name: "SupplierGroup",
                schema: "supplier",
                table: "supplier",
                newName: "Country");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalId",
                schema: "supplier",
                table: "supplier",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "supplier",
                table: "supplier",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
