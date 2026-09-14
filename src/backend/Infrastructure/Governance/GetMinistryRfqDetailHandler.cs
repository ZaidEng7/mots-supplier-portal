// One tender as the ministry reads it, with every bid on it.
//
//
// THIS IS WHERE THE DISCLOSURE DECISION ACTUALLY BITES
//
// The bids are listed with the bidding supplier NAMED and its total shown, on a tender in any state including one
// still open.
//
// Under the narrower reading that was offered, this list would have been empty until the award. Under the widest,
// which is what was chosen, it is populated from the first submitted bid.
//
// Everything except a draft. A draft bid has not been offered to anybody, and showing it would disclose a
// supplier's unfinished thinking, which no reading of that decision covers.
//
// A bid whose supplier row cannot be resolved renders a dash rather than a blank, so a reader can tell a missing
// join from a missing value.

namespace MotsSupplierPortal.Infrastructure.Governance;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Governance;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class GetMinistryRfqDetailHandler(AppDbContext db) : IGetMinistryRfqDetailHandler
{
    public async Task<MinistryRfqDetailDto?> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var commercialVisible = await MinistryCommercialVisibility.IsOnAsync(db, ct);

        var rfq = await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == referenceCode)
            .Select(r => new
            {
                r.Id, r.ReferenceCode, r.TitleAr, r.TitleEn, r.DescriptionAr, r.DescriptionEn,
                r.State, r.PublishedAt, r.SubmissionClosesAt, r.CurrencyCode, r.OrganizationId,
            })
            .FirstOrDefaultAsync(ct);

        if (rfq is null) return null;

        var organisation = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == rfq.OrganizationId)
            .Select(o => new { o.LegalNameAr, o.LegalNameEn })
            .FirstOrDefaultAsync(ct);

        var items = await db.RfqItems.AsNoTracking()
            .Where(i => i.RfqId == rfq.Id)
            .OrderBy(i => i.LineNo)
            .Select(i => new MinistryRfqItemDto(i.TitleAr, i.TitleEn, i.CategoryCode, i.Quantity, i.UnitOfMeasureCode))
            .ToListAsync(ct);

        var proposals = await db.Proposals.AsNoTracking()
            .Where(p => p.RfqId == rfq.Id && p.State != ProposalState.Draft)
            .Select(p => new { p.Id, p.ReferenceCode, p.SupplierId, p.State, p.SubmittedAt })
            .ToListAsync(ct);

        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(s => proposals.Select(p => p.SupplierId).Contains(s.Id))
            .Select(s => new { s.Id, s.ReferenceCode, s.DisplayNameAr, s.DisplayNameEn })
            .ToDictionaryAsync(s => s.Id, s => s, ct);

        var totals = commercialVisible
            ? await MinistryCommercialVisibility.TotalsByProposalAsync(db, proposals.Select(p => p.Id).ToList(), ct)
            : [];

        var winningProposalIds = await db.Awards.AsNoTracking()
            .Where(a => a.RfqId == rfq.Id && a.State == AwardState.Awarded)
            .Select(a => a.WinningProposalId)
            .ToListAsync(ct);

        var bids = proposals
            .OrderBy(p => p.SubmittedAt ?? DateTimeOffset.MaxValue)
            .Select(p =>
            {
                suppliers.TryGetValue(p.SupplierId, out var supplier);
                return new MinistryBidDto(
                    p.ReferenceCode,
                    supplier?.ReferenceCode ?? "—",
                    supplier?.DisplayNameAr ?? "—",
                    supplier?.DisplayNameEn ?? "—",
                    p.State.ToString(),
                    p.SubmittedAt,
                    totals.TryGetValue(p.Id, out var value) ? value : null,
                    winningProposalIds.Contains(p.Id));
            })
            .ToList();

        var awardedValues = commercialVisible
            ? await ListMinistryRfqsHandler.AwardedValuesAsync(db, [rfq.Id], ct)
            : [];

        var summary = new MinistryRfqRowDto(
            rfq.ReferenceCode, rfq.TitleAr, rfq.TitleEn, rfq.State.ToString(),
            organisation?.LegalNameAr ?? "—", organisation?.LegalNameEn ?? "—",
            rfq.PublishedAt, rfq.SubmissionClosesAt,
            await db.Invitations.CountAsync(i => i.RfqId == rfq.Id, ct),
            bids.Count,
            awardedValues.TryGetValue(rfq.Id, out var awarded) ? awarded : null,
            rfq.CurrencyCode);

        return new MinistryRfqDetailDto(
            summary, rfq.DescriptionAr, rfq.DescriptionEn, items, bids, commercialVisible);
    }
}
