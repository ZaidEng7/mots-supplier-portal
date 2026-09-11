using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-087: closes an RFQ's submission window without racing a wall clock.
///
/// <para>Six suites each opened a window from <c>now+1s</c> to <c>now+3s</c> and then slept, so
/// everything in between - approve, publish, the timeline job, starting a proposal, pricing it, setting
/// terms, sometimes two file uploads - had to finish inside two seconds. On a loaded machine it did not,
/// the submit was refused by the closed window, and the failure surfaced later from an unrelated
/// endpoint. That was the flake carried in the backlog through batch 9.</para>
///
/// <para>Moving the stored deadline is not a shortcut past the behaviour under test: the real
/// <c>RfqTimelineJob</c> still performs the transition, and the tests still assert the RFQ reached
/// SubmissionClosed. Only the sleeping is gone. Same technique CrossOrganizationScopeTests adopted after
/// the same class of failure.</para>
/// </summary>
public static class SubmissionWindowTestHelper
{
    /// <summary>
    /// Moves an RFQ's OPENING time into the past, so the timeline job can open the window without a
    /// test having waited for a wall clock.
    ///
    /// <para>The other half of the same problem, and the cause of a flake that outlived T-087's fix.
    /// A seed that sets <c>submissionOpensAt = now + 1s</c> has one second to get through template
    /// creation, the RFQ, its item and requirement, a supplier registration and an invitation before
    /// it calls submit-review - and submit-review is refused outright once the window has opened
    /// ("Cannot submit for review: the submission window has already opened"). The seed swallowed
    /// that refusal, so the failure surfaced later as a missing <c>proposalCode</c>.</para>
    ///
    /// <para>Same discipline as <see cref="CloseAsync"/>: the real job still performs the transition,
    /// and only the sleeping is gone.</para>
    /// </summary>
    public static async Task OpenAsync(PostgresApiFixture fixture, string rfqReferenceCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Rfqs.Where(r => r.ReferenceCode == rfqReferenceCode)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.SubmissionOpensAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }

    public static async Task CloseAsync(PostgresApiFixture fixture, string rfqReferenceCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Rfqs.Where(r => r.ReferenceCode == rfqReferenceCode)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.SubmissionClosesAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }
}
