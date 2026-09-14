// Turning a bid into the read model its own supplier receives.
//
// This is the only place the priced-line shape, which is the financial envelope, is ever produced. Every
// handler that reaches it has already resolved the bid by the caller's own company.
//
// That is the two-envelope seal on this side: not a filter applied to a shared read, but the fact that the
// buyer's reads build a different shape entirely, one whose prices are absent below the commercial tier.

namespace MotsSupplierPortal.Infrastructure.Proposals;

using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Infrastructure.Rfqs;

internal static class ProposalDtoMapper
{
    public static ProposalDto ToDto(Proposal proposal, string rfqReferenceCode) => new(
        proposal.ReferenceCode, rfqReferenceCode, proposal.State,
        proposal.CurrencyCode, proposal.PaymentTerms, proposal.IncotermCode, proposal.DeliveryTermsAr, proposal.DeliveryTermsEn,
        proposal.Warranty, proposal.ValidityStart, proposal.ValidityEnd,
        proposal.NarrativeAr, proposal.NarrativeEn,
        proposal.SubmittedAt, proposal.WithdrawnAt, proposal.WithdrawReason,
        proposal.ClarificationReason, proposal.ClarificationRequestedAt, proposal.RevisionNumber,
        [.. proposal.Items.Select(i => new ProposalItemDto(i.Id, i.RfqItemId, i.Quantity, i.UnitPrice, i.Discount, i.LineTotal, i.LeadTimeDays, i.NotesAr, i.NotesEn))],
        [.. proposal.Documents.Select(d => new ProposalDocumentDto(d.Id, d.OriginalFileName, d.ContentType, d.Caption, d.UploadedAt, d.Envelope))],
        [.. proposal.RequirementAnswers.Select(a => new RequirementAnswerDto(a.Id, a.RequirementId, a.AnswerAr, a.AnswerEn))],
        proposal.CreatedAt,
        new ProposalTotalsDto(proposal.CurrencyCode, proposal.Items.Sum(i => i.LineTotal)),
        proposal.ValidityStart is { } from && proposal.ValidityEnd is { } to
            ? to.DayNumber - from.DayNumber
            : null,
        proposal.RowVersion);
}
