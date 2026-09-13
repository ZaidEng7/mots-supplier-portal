using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Proposals;

public sealed class GetBuyerProposalHandler(AppDbContext db, IScopeContext scope) : IGetBuyerProposalHandler
{
    public async Task<BuyerProposalDetailDto?> HandleAsync(string rfqReferenceCode, Guid proposalId, CancellationToken ct)
    {
        if (await BuyerProposalVisibilityRule.ResolveAsync(db, scope, rfqReferenceCode, ct) is not var (rfq, visibility))
        {
            return null;
        }

        // Sealed means sealed: a 404 on the detail, not an empty shell. An empty shell would confirm
        // the bid exists, which is the fact the tier is protecting.
        if (visibility == BuyerProposalVisibility.Sealed) return null;

        // Split, not one join: three sibling collections in a single query multiply out, so a proposal
        // with 20 items, 15 requirement answers and 5 documents costs 1,500 rows to read 40 entities,
        // every scalar on the proposal repeated in each. Three round trips is the cheaper shape.
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
                    // Quantity and lead time are technical; price is not.
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
