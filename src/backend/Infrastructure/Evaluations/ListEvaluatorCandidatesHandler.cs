using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluations;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Evaluations;

/// <summary>
/// Staff who could score this RFQ's evaluation.
///
/// <para><b>Derived from the ROLE CLAIMS, not from Roles.DefaultPermissions.</b> The catalogue is the shipped
/// default; what a role actually grants is its claim rows, which an administrator can edit on SCR-703. Reading
/// the catalogue here would list a candidate who no longer holds evaluation.score - or omit one who was
/// granted it - and the assignment would then fail at the point of scoring, which is the worst place to find
/// out. The login path builds its token from the same claims, so this and authorisation agree by construction.</para>
///
/// <para>Scoped to the RFQ's own organisation (BRULE-029): an evaluator from another ministry body is not a
/// candidate, and offering one would be a scoping hole in a picker rather than in a query.</para>
/// </summary>
public sealed class ListEvaluatorCandidatesHandler(AppDbContext db, IScopeContext scope) : IListEvaluatorCandidatesHandler
{
    public async Task<IReadOnlyList<EvaluatorCandidateDto>?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var rfq = await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == rfqReferenceCode)
            .Select(r => new { r.OrganizationId })
            .FirstOrDefaultAsync(ct);

        // §9.2: the scope predicate is in the query, and an RFQ outside the caller's organisation is absent
        // rather than forbidden.
        if (rfq is null || scope.OrganizationId is null || rfq.OrganizationId != scope.OrganizationId) return null;

        var scoringRoleIds = await db.Set<IdentityRoleClaim<Guid>>().AsNoTracking()
            .Where(c => c.ClaimType == "perms" && c.ClaimValue == Permissions.EvaluationScore)
            .Select(c => c.RoleId)
            .ToListAsync(ct);

        if (scoringRoleIds.Count == 0) return [];

        var userIds = await db.Set<IdentityUserRole<Guid>>().AsNoTracking()
            .Where(ur => scoringRoleIds.Contains(ur.RoleId))
            .Select(ur => ur.UserId)
            .ToListAsync(ct);

        return await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id)
                        && u.OrganizationId == rfq.OrganizationId
                        // An inactive account cannot sign in, so assigning one would park the evaluation on
                        // somebody who can never submit it.
                        && u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new EvaluatorCandidateDto(u.Id, u.FullName, u.Email!))
            .ToListAsync(ct);
    }
}
