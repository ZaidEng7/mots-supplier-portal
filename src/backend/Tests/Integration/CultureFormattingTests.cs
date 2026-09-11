using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-048: what this service WRITES does not depend on the locale of the machine it runs on.
///
/// <para><b>What the entry got wrong, and what it got right.</b> The row was closed as "swept;
/// production C# is clean" - and the sweep read <c>ToString</c> CALL SITES. String interpolation
/// formats too, and it was not swept: <c>EvaluationHandlers</c> interpolated a <c>decimal</c> score
/// into the audit row's <c>toState</c>. On a host whose locale uses a comma decimal separator, a
/// score of 7.5 was recorded as "7,5" in <c>ops.audit_log</c> - a table with a
/// <c>BEFORE UPDATE OR DELETE</c> trigger, so the wrong value could never be corrected, and one that
/// is exported verbatim to CSV.</para>
///
/// <para><b>Not through a request header.</b> No request-localization middleware is registered, so
/// a client cannot influence this - the exposure was purely the deployment host's locale. That makes
/// it latent rather than live, which is why nothing caught it, and it is also why no test could
/// catch it without doing what this one does: changing the process's culture on purpose.</para>
///
/// <para><b>The mutation is global, and restored in a finally.</b> T-073 is the backlog entry about
/// exactly this class of test, so: the previous culture is captured, a comma-decimal one is
/// installed, and the original is put back whether the assertions pass or throw. Without the
/// finally, every later test in the run would format under de-DE.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class CultureFormattingTests(PostgresApiFixture fixture)
{
    /// <summary>
    /// A culture that separates decimals with a comma, so a mistake is VISIBLE rather than
    /// coincidentally identical. ar-SY would be the realistic host and is the wrong choice here:
    /// .NET renders its decimals with a full stop, so the test would pass against the defect.
    /// </summary>
    private static readonly CultureInfo CommaDecimal = new("de-DE");

    [Fact]
    public async Task A_decimal_score_is_audited_the_same_way_on_any_hosts_locale()
    {
        // Seeded FIRST, under the ordinary culture. The first version of this test installed de-DE
        // around the whole thing and the seed itself failed, which told us nothing about the rule
        // under test: a test that changes global state should change it for exactly the operation it
        // is about.
        var seeded = await EvaluationSeed.CreateAsync(fixture, "Culture");
        await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { seeded.EvaluatorId } });

        Guid criterionId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            criterionId = await db.EvaluationCriterionSnapshots
                .Where(c => c.EvaluationId == seeded.EvaluationId).Select(c => c.Id).FirstAsync();
        }

        var previous = CultureInfo.DefaultThreadCurrentCulture;
        try
        {
            CultureInfo.DefaultThreadCurrentCulture = CommaDecimal;

            // The control for the control: if this ever renders "7.5", the chosen culture stopped
            // being comma-decimal and the assertion below proves nothing.
            7.5m.ToString(CultureInfo.CurrentCulture).Should().Be("7,5",
                "the test culture must actually differ, or this file asserts nothing");

            // A score with a fractional part, which is what makes the separator observable. The
            // column is decimal(6,2), so this is an ordinary value rather than an edge case.
            var scored = await seeded.Evaluator.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/scores",
                new { proposalCode = seeded.ProposalCode, criterionId, rawScore = 7.5m, commentAr = (string?)null, commentEn = (string?)null });
            scored.StatusCode.Should().Be(HttpStatusCode.OK, await scored.Content.ReadAsStringAsync());

            await using var readScope = fixture.Services.CreateAsyncScope();
            var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var toState = await readDb.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "evaluation.score" && a.ReferenceCode == seeded.RfqCode)
                .OrderByDescending(a => a.OccurredAt)
                .Select(a => a.ToState)
                .FirstAsync();

            toState.Should().EndWith("=7.5",
                "the audit row is append-only and exported to CSV; a comma here is a number nobody "
                + "can correct and a CSV column that shifts");
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = previous;
        }
    }

    [Fact]
    public void The_process_pins_the_invariant_culture_at_startup()
    {
        // The other half, and the one that survives a new formatting site nobody qualifies. Read off
        // the composition root rather than off a running thread: the fixture builds the host, so the
        // pin has already run by the time any test observes it.
        var source = File.ReadAllText(ProgramFile());

        source.Should().Contain("CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture",
            "an unpinned process formats by the host's locale, which is how T-048 reopened");
        source.Should().Contain("CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture");
    }

    private static string ProgramFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MotsSupplierPortal.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the check cannot find the backend solution root from the test binaries");
        var file = Path.Combine(directory!.FullName, "Api", "Program.cs");
        File.Exists(file).Should().BeTrue($"expected {file}");
        return file;
    }
}
