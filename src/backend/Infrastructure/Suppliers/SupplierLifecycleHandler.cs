// Suspending, reactivating and deactivating an approved supplier.
//
// The transitions themselves live on the supplier. This handler owns saving them, recording them, and the
// part the rule requires that the domain cannot reach: taking away the supplier's users' access when the
// company is deactivated.
//
// A refused transition comes back as a typed result carrying the domain's own message, so the caller learns
// why rather than being told no.
//
//
// REVOKING ACCESS NEEDS BOTH HALVES
//
// Neither is sufficient on its own. Marking the accounts inactive stops a new sign-in and stops a refresh,
// because both paths check it. But a live refresh-token family is a credential still sitting in a browser,
// so the families are ended as well, matching what disabling a single user already does.
//
// Setting the supplier's state alone would look identical in the database and leave every one of its users
// able to keep working. That is why the tests assert an actual failed sign-in and an actual failed refresh
// rather than inspecting the state column.
//
// A deactivation is one transaction, opened by SessionLock for every one of the supplier's users before anything is
// written: the transition, the inactive accounts, the ended sessions and the audit row. The framework saves each
// account through its own call, and those saves used to commit one at a time, the transition with the first of them.
// The lock makes a refresh in flight for any of those users either finish first, so its successor is ended here, or
// wait and find its session over.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SupplierLifecycleHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IScopeContext scope,
    IAuditLogger auditLogger) : ISupplierLifecycleHandler
{
    public Task<SupplierLifecycleResult> SuspendAsync(SupplierLifecycleCommand command, CancellationToken ct) =>
        TransitionAsync(command, (s, reason) => s.Suspend(reason), "supplier_suspended", ct);

    public Task<SupplierLifecycleResult> ReactivateAsync(SupplierLifecycleCommand command, CancellationToken ct) =>
        TransitionAsync(command, (s, reason) => s.Reactivate(reason), "supplier_reactivated", ct);

    public Task<SupplierLifecycleResult> DeactivateAsync(SupplierLifecycleCommand command, CancellationToken ct) =>
        TransitionAsync(command, (s, reason) => s.Deactivate(reason), "supplier_deactivated", ct,
            revokeUserAccess: true);

    private async Task<SupplierLifecycleResult> TransitionAsync(
        SupplierLifecycleCommand command,
        Action<Domain.Suppliers.Supplier, string> transition,
        string auditAction,
        CancellationToken ct,
        bool revokeUserAccess = false)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.ReferenceCode == command.ReferenceCode, ct);
        if (supplier is null)
        {
            return new SupplierLifecycleResult.NotFound();
        }

        var stateBefore = supplier.LifecycleState;

        try
        {
            transition(supplier, command.Reason);
        }
        catch (DomainException ex)
        {
            return new SupplierLifecycleResult.Invalid(ex.Message);
        }

        List<AppUser> supplierUsers = revokeUserAccess
            ? await userManager.Users.Where(u => u.SupplierId == supplier.Id).ToListAsync(ct)
            : [];

        await using var transaction = revokeUserAccess
            ? await SessionLock.BeginAsync(db, supplierUsers.Select(u => u.Id), ct)
            : null;

        if (revokeUserAccess)
        {
            await RevokeAccessAsync(supplierUsers, ct);
        }

        await auditLogger.LogAsync(
            "Supplier", supplier.Id, KeptSuspended(auditAction, stateBefore, supplier) ? "supplier_kept_suspended" : auditAction,
            scope.UserId,
            fromState: stateBefore.ToString(),
            toState: supplier.LifecycleState.ToString(),
            reason: command.Reason,
            referenceCode: supplier.ReferenceCode,
            ct: ct);

        await db.SaveChangesAsync(ct);

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return new SupplierLifecycleResult.Success(supplier.LifecycleState.ToString());
    }

    // A suspension of a supplier that was already suspended - the sync's hold while the ERP approves it, taken over by a
    // person - is recorded as that, not as a suspension that changed nothing: it is why the ERP's approval will no
    // longer bring the supplier back, and the trail is where somebody later looks for that. It still ends in Suspended,
    // so a person's suspension is what the rest of the product sees as the latest.
    private static bool KeptSuspended(string auditAction, SupplierLifecycleState stateBefore, Domain.Suppliers.Supplier supplier) =>
        auditAction == "supplier_suspended"
        && stateBefore == SupplierLifecycleState.Suspended
        && supplier.LifecycleState == SupplierLifecycleState.Suspended;

    private async Task RevokeAccessAsync(List<AppUser> users, CancellationToken ct)
    {
        foreach (var user in users)
        {
            user.IsActive = false;
            await userManager.UpdateAsync(user);
        }

        var userIds = users.Select(u => u.Id).ToList();
        await SessionLock.RevokeAsync(db, t => userIds.Contains(t.UserId), DateTimeOffset.UtcNow, ct);
    }
}
