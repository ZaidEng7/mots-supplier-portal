// An award whose purchase order failed to reach the ERP, for the suites about what happens next.
//
// The tender comes from EvaluationSeed, and the award is recommended on its one bid and forced into a failed send in
// storage, rather than driven through approval, issue and a failing adapter, which AwardEndpointsTests already does.
// The retry and the status banner care about the send's state, not about how the award got there.
//
// IT IS REMOVED AT THE END OF EVERY TEST THAT MAKES ONE. The database is shared by every integration class: an award
// left failed would turn another class's status read degraded, and any award left behind, failed or retried, would
// move counts read across the whole registry, such as the ERP sync monitor's. Remove deletes the row, so a test calls
// it in a finally whatever state it left the award in.

namespace MotsSupplierPortal.Tests.Integration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed record FailedAward(string RfqCode, Guid AwardId, Guid OrgId);

public static class FailedAwardSeed
{
    public static async Task<FailedAward> CreateAsync(PostgresApiFixture fixture, string label)
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, label);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rfqId = await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == seeded.RfqCode).Select(r => r.Id).SingleAsync();

        var award = Award.Recommend(rfqId, seeded.ProposalId, "ترسية تعثّر إرسالها", "An award whose send failed", seeded.ManagerId);
        db.Awards.Add(award);
        await db.SaveChangesAsync();

        await db.Awards.Where(a => a.Id == award.Id).ExecuteUpdateAsync(set => set
            .SetProperty(a => a.ErpSyncStatus, ErpSyncStatus.Failed)
            .SetProperty(a => a.ErpRetryCount, 1));

        return new FailedAward(seeded.RfqCode, award.Id, seeded.OrgId);
    }

    public static async Task RemoveAsync(PostgresApiFixture fixture, Guid awardId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Awards.Where(a => a.Id == awardId).ExecuteDeleteAsync();
    }
}
