using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// SCR-901/D-60: the notification types a user has switched off.
    ///
    /// <para><b>Muted rows, not preference rows.</b> A row means "do not deliver"; no row means deliver. So
    /// this table starts empty and every existing user keeps receiving exactly what they receive today - no
    /// backfill, and no window in which somebody's inbox goes quiet because a migration ran.</para>
    /// </summary>
    public partial class NotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_preference",
                schema: "shared",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_preference_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_preference_UserId_NotificationType",
                schema: "shared",
                table: "notification_preference",
                columns: new[] { "UserId", "NotificationType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_preference",
                schema: "shared");
        }
    }
}
