// One bid as buyer staff may see it, which depends on what the tender's state permits.
//
// Sealed means sealed: a not-found on the detail rather than an empty shell. An empty shell would confirm the
// bid exists, which is the fact the seal is protecting.
//
// Prices, totals, the currency and the commercial terms appear only once the commercial envelope is open.
// Quantities and lead times are technical, so they are always there.
//
// The three child collections are fetched as separate statements rather than one join. Three siblings in a
// single join multiply out, so a bid with twenty lines, fifteen answers and five documents costs fifteen
// hundred rows to read forty entities, with every scalar on the bid repeated in each. Three round trips is the
// cheaper shape.

namespace MotsSupplierPortal.Infrastructure.Proposals;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetBuyerProposalHandler(AppDbContext db, IScopeContext scope) : IGetBuyerProposalHandler
{
    public async Task<BuyerProposalDetailDto?> HandleAsync(string rfqReferenceCode, Guid proposalId, CancellationToken ct)
    {
        if (await BuyerProposalVisibilityRule.ResolveAsync(db, scope, rfqReferenceCode, ct) is not var (rfq, visibility))
        {
            return null;
        }

        if (visibility == BuyerProposalVisibility.Sealed) return null;

        var proposal = await db.Proposals.AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Items)
            .Include(p => p.RequirementAnswers)
            .Include(p => p.Documents)
            .FirstOrDefaultAsync(p => p.Id == proposalId && p.RfqId == rfq.Id, ct);

        if (proposal is null || !BuyerProposalVisibilityRule.IsDisclosable(proposal.State)) return null;

        var supplier = await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == proposal.SupplierId)
            .Select(s => new { s.DisplayNameAr, s.DisplayNameEn })
            .FirstOrDefaultAsync(ct);

        var rfqItems = await db.RfqItems.AsNoTracking()
            .Where(i => i.RfqId == rfq.Id)
            .Select(i => new { i.Id, i.TitleAr, i.TitleEn })
            .ToDictionaryAsync(i => i.Id, ct);

        var requirements = await db.Requirements.AsNoTracking()
            .Where(r => r.RfqId == rfq.Id)
            .Select(r => new { r.Id, r.TextAr, r.TextEn })
            .ToDictionaryAsync(r => r.Id, ct);

        var commercial = visibility == BuyerProposalVisibility.Commercial;

        var items = proposal.Items
            .Select(i =>
            {
                var titles = rfqItems.GetValueOrDefault(i.RfqItemId);
                return new BuyerProposalItemDto(
                    i.RfqItemId, titles?.TitleAr ?? "", titles?.TitleEn ?? "", i.Quantity,
                    commercial ? i.UnitPrice : null,
                    commercial ? i.LineTotal : null,
                    i.LeadTimeDays, i.NotesAr, i.NotesEn);
            })
            .ToList();

        var answers = proposal.RequirementAnswers
            .Select(a =>
            {
                var text = requirements.GetValueOrDefault(a.RequirementId);
                return new BuyerProposalAnswerDto(a.RequirementId, text?.TextAr ?? "", text?.TextEn ?? "", a.AnswerAr, a.AnswerEn);
            })
            .ToList();

        return new BuyerProposalDetailDto(
            visibility, proposal.Id, proposal.ReferenceCode,
            supplier?.DisplayNameAr ?? "", supplier?.DisplayNameEn ?? "",
            proposal.State, proposal.SubmittedAt,
            proposal.NarrativeAr, proposal.NarrativeEn,
            answers, items, proposal.Documents.Count,
            commercial ? proposal.Items.Sum(i => i.LineTotal) : null,
            commercial ? proposal.CurrencyCode : null,
            commercial ? proposal.PaymentTerms : null,
            commercial ? proposal.IncotermCode : null);
    }
}
