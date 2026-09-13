// What this service WRITES does not depend on the locale of the machine it runs on.
//
//
// WHAT THE EARLIER SWEEP GOT WRONG
//
// The entry was closed as swept and the production code clean, and the sweep read explicit formatting CALL SITES.
//
// String interpolation formats too, and it was not swept: a handler interpolated a decimal score into an audit
// row's state field. On a host whose locale uses a comma decimal separator, a score of seven and a half was
// recorded with a comma, in a table with a trigger that forbids updates and deletes, so the wrong value could never
// be corrected, and one that is exported verbatim to spreadsheets.
//
//
// NOT REACHABLE THROUGH A REQUEST
//
// No request-localisation middleware is registered, so a client cannot influence this: the exposure was purely the
// deployment host's locale.
//
// That makes it latent rather than live, which is why nothing caught it, and it is also why no test could catch it
// without doing what this one does, which is changing the process's culture on purpose.
//
//
// THE MUTATION IS GLOBAL, AND RESTORED IN A FINALLY
//
// The previous culture is captured, a comma-decimal one installed, and the original put back whether the assertions
// pass or throw. Without that, every later test in the run would format under the wrong culture.
//
// The seeding happens FIRST, under the ordinary culture. The first version installed the other culture around the
// whole thing and the seed itself failed, which told us nothing about the rule under test: a test that changes
// global state should change it for exactly the operation it is about.
//
// The chosen culture separates decimals with a comma so a mistake is VISIBLE rather than coincidentally identical.
// The realistic host's own culture would be the wrong choice, because the platform renders its decimals with a full
// stop and the test would pass against the defect.
//
// There is a control for the control: if the sample ever renders with a full stop, the chosen culture stopped being
// comma-decimal and the real assertion proves nothing. The score has a fractional part, which is what makes the
// separator observable, and the column's precision makes it an ordinary value rather than an edge case.
//
// The other half, the one that survives a new formatting site nobody qualifies, is read off the composition root
// rather than off a running thread, because the fixture builds the host so the process-wide pin has already run by
// the time any test observes it.

namespace MotsSupplierPortal.Tests.Integration.Contract;

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class CultureFormattingTests(PostgresApiFixture fixture)
{
    private static readonly CultureInfo CommaDecimal = new("de-DE");

    [Fact]
    public async Task A_decimal_score_is_audited_the_same_way_on_any_hosts_locale()
    {
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

            7.5m.ToString(CultureInfo.CurrentCulture).Should().Be("7,5",
                "the test culture must actually differ, or this file asserts nothing");

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
