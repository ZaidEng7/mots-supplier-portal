// Carrying the caller's expected version from the request into every save.
//
// So the stale-version guard applies to all forty-odd aggregate writes rather than the two that set it by hand.
//
// An interceptor rather than an override on the context, because the context is constructed from options and would
// otherwise need the request-scoped value threaded through its constructor, which makes every design-time and test
// construction of the context need one too.
//
// It does nothing when the request carried no expected version. That is not a hole: the guarded endpoints reject a
// version-less write at the edge, long before a save is attempted, so no expected version here means this write is
// not one of the guarded ones.
//
//
// IT APPLIES ONLY TO THE FIRST SAVE OF A REQUEST
//
// The expected version describes the state the CALLER read, so it can only be asserted against that first write.
//
// A handler that saves twice, and most do because the audit logger performs its own save, would otherwise have the
// second save compare the caller's now-superseded version against the row its own first save just advanced, and
// fail a write nothing was contending.

namespace MotsSupplierPortal.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MotsSupplierPortal.Application.Common;

public sealed class ExpectedVersionInterceptor(IConcurrencyContext concurrency) : SaveChangesInterceptor
{
    private bool _applied;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext? context)
    {
        if (_applied || context is not AppDbContext db) return;
        if (concurrency.ExpectedRowVersion is not { } expected) return;

        db.ApplyExpectedVersion(expected);
        _applied = true;
    }
}
