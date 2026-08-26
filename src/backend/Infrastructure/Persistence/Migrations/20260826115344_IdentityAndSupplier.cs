using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAndSupplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.EnsureSchema(
                name: "reference");

            migrationBuilder.EnsureSchema(
                name: "supplier");

            migrationBuilder.RenameTable(
                name: "currencies",
                newName: "currencies",
                newSchema: "reference");

            migrationBuilder.CreateTable(
                name: "app_user",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorLabel = table.Column<string>(type: "text", nullable: true),
                    AggregateType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceCode = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FromState = table.Column<string>(type: "text", nullable: true),
                    ToState = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "role",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "supplier",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TaxId = table.Column<string>(type: "text", nullable: true),
                    OnboardingState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_session",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Ip = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_session", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_claim",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_claim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_claim_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_login",
                schema: "identity",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_login", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_user_login_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_token",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_token", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_user_token_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_claim",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_claim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_claim_role_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_role",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_role", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_user_role_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_role_role_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "representative",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Position = table.Column<string>(type: "text", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_representative", x => x.Id);
                    table.ForeignKey(
                        name: "FK_representative_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "app_user",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_OrganizationId",
                schema: "identity",
                table: "app_user",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_SupplierId",
                schema: "identity",
                table: "app_user",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identity",
                table: "app_user",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_ActorUserId_OccurredAt",
                schema: "ops",
                table: "audit_log",
                columns: new[] { "ActorUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_AggregateType_AggregateId_OccurredAt",
                schema: "ops",
                table: "audit_log",
                columns: new[] { "AggregateType", "AggregateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_CorrelationId",
                schema: "ops",
                table: "audit_log",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_representative_SupplierId",
                schema: "supplier",
                table: "representative",
                column: "SupplierId",
                unique: true,
                filter: "\"IsPrimary\" = true");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "identity",
                table: "role",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_claim_RoleId",
                schema: "identity",
                table: "role_claim",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_OnboardingState",
                schema: "supplier",
                table: "supplier",
                column: "OnboardingState");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_ReferenceCode",
                schema: "supplier",
                table: "supplier",
                column: "ReferenceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_claim_UserId",
                schema: "identity",
                table: "user_claim",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_login_UserId",
                schema: "identity",
                table: "user_login",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_RoleId",
                schema: "identity",
                table: "user_role",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_user_session_FamilyId",
                schema: "identity",
                table: "user_session",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_user_session_UserId",
                schema: "identity",
                table: "user_session",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "representative",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "role_claim",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_claim",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_login",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_role",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_session",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_token",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "supplier",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "role",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_user",
                schema: "identity");

            migrationBuilder.RenameTable(
                name: "currencies",
                schema: "reference",
                newName: "currencies",
                newSchema: "public");
        }
    }
}
