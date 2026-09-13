using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>FEAT-08.2/FR-INV-002: suggests suppliers whose Offerings match one of the RFQ's item
/// categories, Active-only, already-invited suppliers excluded, ranked by match count. Reuses the
/// same manual Offering-to-Supplier join as SearchBuyerOfferingsHandler (OfferingHandlers.cs's own
/// comment: Offering and Supplier are separate aggregate roots with no EF navigation between
/// them) - a suggestion, not a binding action: the officer still calls InviteSupplier per
/// candidate.</summary>
public sealed class SuggestInvitationCandidatesHandler(AppDbContext db, IScopeContext scope) : ISuggestInvitationCandidatesHandler
{
    public async Task<IReadOnlyList<InvitationCandidateDto>> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, referenceCode, ct);
        if (rfq is null) return [];

        var categoryCodes = rfq.Items.Select(i => i.CategoryCode).Distinct().ToList();
        var alreadyInvited = rfq.Invitations.Select(i => i.SupplierId).ToHashSet();
        if (categoryCodes.Count == 0) return [];

        // Two sources, deliberately, and the second one was missing.
        //
        // A supplier declares the categories they work in during onboarding - it is a REQUIRED step,
        // and the application cannot be submitted without at least one. An OFFERING is the optional
        // catalogue entry on top of that. Matching only on offerings meant an approved, active
        // supplier with the right categories and no catalogue entry appeared nowhere in the buyer's
        // suggestions, and this screen has no other way to invite anybody - so they could not be
        // invited at all. Reported by a buyer who had just approved one and could not find them.
        var fromOfferings = await (
            from o in db.Offerings
            where o.IsActive && categoryCodes.Contains(o.CategoryCode)
            select new { o.SupplierId, o.CategoryCode })
            .Distinct()
            .ToListAsync(ct);

        var fromCategories = await (
            from l in db.Set<CategoryLink>()
            where categoryCodes.Contains(l.CategoryCode)
            select new { l.SupplierId, l.CategoryCode })
            .Distinct()
            .ToListAsync(ct);

        // Distinct on the PAIR, so a supplier who both declared a category and listed an offering in
        // it counts once for it - the number beside their name is "categories matched", not "rows".
        var matches = fromOfferings.Concat(fromCategories).Distinct().ToList();

        var candidateIds = matches.Select(m => m.SupplierId).Distinct().Where(id => !alreadyInvited.Contains(id)).ToList();
        if (candidateIds.Count == 0) return [];

        var activeSuppliers = await db.Suppliers
            .Where(s => candidateIds.Contains(s.Id) && s.LifecycleState == SupplierLifecycleState.Active)
            .Select(s => new { s.Id, s.DisplayNameAr, s.DisplayNameEn })
            .ToListAsync(ct);

        var matchCounts = matches.Where(m => activeSuppliers.Select(s => s.Id).Contains(m.SupplierId))
            .GroupBy(m => m.SupplierId).ToDictionary(g => g.Key, g => g.Select(m => m.CategoryCode).Distinct().Count());

        return [.. activeSuppliers
            .Select(s => new InvitationCandidateDto(s.Id, s.DisplayNameAr, s.DisplayNameEn, matchCounts.GetValueOrDefault(s.Id, 0)))
            .OrderByDescending(c => c.MatchCount)];
    }
}
