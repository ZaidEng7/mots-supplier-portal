using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// SCR-501: the criterion guidance an evaluator scores against, carried into the evaluation's own
    /// snapshot.
    ///
    /// <para><b>Nullable, and not backfilled.</b> A tender that bound its template before this column
    /// existed did not record the instruction, and copying the template's CURRENT text into those rows
    /// would show an evaluator an instruction the tender never carried - while looking exactly like one it
    /// did. Null means "not recorded for this tender", which the screen states rather than hides.</para>
    /// </summary>
    public partial class EvaluationCriterionGuidance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GuidanceAr",
                schema: "evaluation",
                table: "evaluation_criterion_snapshot",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuidanceEn",
                schema: "evaluation",
                table: "evaluation_criterion_snapshot",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuidanceAr",
                schema: "evaluation",
                table: "evaluation_criterion_snapshot");

            migrationBuilder.DropColumn(
                name: "GuidanceEn",
                schema: "evaluation",
                table: "evaluation_criterion_snapshot");
        }
    }
}
