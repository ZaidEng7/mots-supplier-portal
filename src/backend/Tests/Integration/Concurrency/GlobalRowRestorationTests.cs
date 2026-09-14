// T-073: no test leaves a globally shared row changed.
//
// The class of defect, and the two it has already cost. One database is shared by 145 test classes,
// serialized but never reset. ManageRolesTests overwrote ministry_viewer's permissions and left them
// overwritten - the governance suite then passed alone and failed in a full run. ReportEndpointsTests
// granted report.read to procurement_officer and never took it back, and AuthorizationFuzzTests records
// that its sweep had to be rewritten because of it. Both were found by a person, after the fact, from a
// failure somewhere else.
//
// What this asserts: every row the seeder created, still as the seeder left it - each role's permission
// set, every system setting, every supplier field-config flag, every document type's flags, and the KEYS
// carrying an email, notification or UI-string override. Rows a test creates for itself are not compared; a
// new reference code or an override on a key nobody else reads changes nothing another test sees, and
// forbidding it would forbid most of the suite.
//
// The honest limit: xUnit v2 does not let a test class declare itself last, so this catches a leak from
// every class that ran BEFORE it - which is most of them in a full run, and all of them when a suspect file
// is run with this one. The fixture repeats the check when the run ends and reports at collection level,
// loud in the log, but the VSTest adapter exits 0 on it, which is why this test exists rather than that
// check alone.
//
// The message IS the finding, so it is raised directly rather than through a `because` clause:
// FluentAssertions prints a because twice, once in its own sentence and once in the subject, which buried
// the row it names.

namespace MotsSupplierPortal.Tests.Integration.Concurrency;

using FluentAssertions;
using Xunit;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class GlobalRowRestorationTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Every_seeded_global_row_is_still_what_the_seeder_made_it()
    {
        var drift = await fixture.GlobalRowDriftAsync();

        if (drift is not null) Assert.Fail(drift);
    }
}
