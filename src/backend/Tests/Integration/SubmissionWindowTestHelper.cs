// Opening and closing a tender's submission window without racing a wall clock.
//
// Six suites each opened a window a second wide and then slept, so everything in between, approving, publishing,
// the timeline job, starting a bid, pricing it, setting terms and sometimes two file uploads, had to finish inside
// two seconds.
//
// On a loaded machine it did not, the submission was refused by the closed window, and the failure surfaced later
// from an unrelated endpoint. That was the flake the backlog carried.
//
// The opening half is the same problem and outlived the first fix. A seed that opens the window a second out has
// one second to get through template creation, the tender, its line and requirement, a supplier registration and
// an invitation before it sends the tender for review, and sending for review is refused outright once the window
// has opened. The seed swallowed that refusal, so the failure surfaced later as a missing bid code.
//
//
// MOVING A STORED DEADLINE IS NOT A SHORTCUT PAST THE BEHAVIOUR UNDER TEST
//
// The real timeline job still performs the transition, and the tests still assert the tender reached the closed
// state. Only the sleeping is gone.
//
// Same technique another suite adopted after the same class of failure.

namespace MotsSupplierPortal.Tests.Integration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class SubmissionWindowTestHelper
{
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
