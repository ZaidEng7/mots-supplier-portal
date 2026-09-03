using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Infrastructure.Persistence;

/// <summary>
/// Carries the caller's <c>If-Match</c> version from the request into every save, so §8.1's guard
/// applies to all forty-odd aggregate writes rather than the two that set it by hand.
///
/// <para>An interceptor rather than a <c>SaveChangesAsync</c> override, because the context is
/// constructed from options and would otherwise need the request-scoped concurrency context threaded
/// through its constructor - which makes every design-time and test construction of the context need
/// one too.</para>
///
/// <para>Does nothing when the request carried no <c>If-Match</c>. That is not a hole: the endpoints
/// §8.1 covers reject a version-less write at the edge with a 428, long before a save is attempted,
/// so "no expected version here" means "this write is not one of the guarded ones".</para>
/// </summary>
public sealed class ExpectedVersionInterceptor(IConcurrencyContext concurrency) : SaveChangesInterceptor
{
    /// <summary>
    /// The expected version describes the state the CALLER read, so it can only be asserted against
    /// the first write of a request. A handler that saves twice - and most do, because the audit
    /// logger performs its own SaveChanges - would otherwise have the second save compare the
    /// caller's now-superseded version against the row its own first save just advanced, and fail a
    /// write that nothing was contending. Scoped per request, like the context it reads from.
    /// </summary>
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
