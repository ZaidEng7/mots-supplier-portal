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

namespace MotsSupplierPortal.Infrastructure.Governance;

/// <summary>
/// SCR-606: one tender, read-only, with every bid on it.
///
/// <para><b>This is where D-66's scope actually bites.</b> The bids are listed with the bidding supplier
/// NAMED and its total shown, on a tender in any state including one still open. Under the narrower scope
/// D-57 offered, this list would have been empty until the award; under the widest, which is what was
/// chosen, it is populated from the first submitted bid.</para>
/// </summary>
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

        // Everything except a Draft: a draft bid has not been offered to anybody, and showing it would
        // disclose a supplier's unfinished thinking - which no reading of D-57 covers.
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
