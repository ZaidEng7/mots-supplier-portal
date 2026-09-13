// The staff who could score this tender's evaluation.
//
//
// DERIVED FROM THE LIVE ROLE GRANTS, NOT THE SHIPPED DEFAULTS
//
// The default map is what the product ships with. What a role actually grants is its claim rows, which an
// administrator can edit.
//
// Reading the defaults here would list a candidate who no longer holds the scoring permission, or omit one who
// was granted it, and the assignment would then fail at the point of scoring, which is the worst place to find
// out.
//
// The sign-in path builds its token from the same claims, so this list and authorisation agree by
// construction.
//
//
// SCOPED TO THE TENDER'S OWN ORGANIZATION
//
// An evaluator from another ministry body is not a candidate, and offering one would be a scoping hole in a
// picker rather than in a query.
//
// The scope test is inside the query, and a tender outside the caller's organization is absent rather than
// forbidden.
//
// Inactive accounts are excluded, because an account that cannot sign in would park the evaluation on somebody
// who can never submit it.

namespace MotsSupplierPortal.Infrastructure.Evaluation;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ListEvaluatorCandidatesHandler(AppDbContext db, IScopeContext scope) : IListEvaluatorCandidatesHandler
{
    public async Task<IReadOnlyList<EvaluatorCandidateDto>?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var rfq = await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == rfqReferenceCode)
            .Select(r => new { r.OrganizationId })
            .FirstOrDefaultAsync(ct);

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
                        && u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new EvaluatorCandidateDto(u.Id, u.FullName, u.Email!))
            .ToListAsync(ct);
    }
}
