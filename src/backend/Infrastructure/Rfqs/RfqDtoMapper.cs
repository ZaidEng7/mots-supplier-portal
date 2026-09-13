// Turning a tender into the two read models it has: the buyer's and the bidder's.
//
// The buyer's form is built asynchronously because an invitation carries the invited supplier's display
// names, which is a small extra query per call. That is acceptable on the mutation and detail endpoints and
// is not acceptable on a list, which is why the list path selects its names in one statement instead.
//
// An identifier a name lookup cannot resolve comes back absent rather than as an empty string. A deactivated
// user's row still exists, so a missing name means the identifier points at nothing, which is a different
// fact from "this tender has no owner" and must not render as the same thing.
//
//
// THE BIDDER'S FORM IS THE ANONYMISATION BOUNDARY
//
// It includes the asking supplier's own questions whatever their visibility, plus every other supplier's
// questions that were published to all.
//
// Another supplier's private question is not merely anonymised, it is entirely absent, which is what the
// written rule means by private to the asker.
//
// Whether a question is the reader's own is computed here on the server from the real asker, and never taken
// from a flag the caller sent.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

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

internal static class RfqDtoMapper
{
    public static async Task<RfqDto> ToDtoAsync(AppDbContext db, Rfq rfq, CancellationToken ct)
    {
        var supplierIds = rfq.Invitations.Select(i => i.SupplierId)
            .Concat(rfq.Clarifications.Select(c => c.AskedBySupplierId)).Distinct().ToList();
        var names = await SupplierNamesAsync(db, supplierIds, ct);
        return ToDto(rfq, names, await StaffNamesAsync(db, rfq, ct));
    }

    public static async Task<Dictionary<Guid, string>> StaffNamesAsync(AppDbContext db, Rfq rfq, CancellationToken ct)
    {
        var ids = new List<Guid>();
        if (rfq.OwnerUserId is { } owner) ids.Add(owner);
        if (rfq.Approvals.LastOrDefault(a => a.Decision is null)?.AssignedApproverUserId is { } approver) ids.Add(approver);
        return await StaffNamesAsync(db, ids, ct);
    }

    public static async Task<Dictionary<Guid, string>> StaffNamesAsync(
        AppDbContext db, IReadOnlyList<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return [];
        return await db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    public static async Task<Dictionary<Guid, (string Ar, string En)>> SupplierNamesAsync(
        AppDbContext db, IReadOnlyList<Guid> supplierIds, CancellationToken ct)
    {
        if (supplierIds.Count == 0) return [];
        return await db.Suppliers.Where(s => supplierIds.Contains(s.Id))
            .Select(s => new { s.Id, s.DisplayNameAr, s.DisplayNameEn })
            .ToDictionaryAsync(s => s.Id, s => (s.DisplayNameAr, s.DisplayNameEn), ct);
    }

    public static RfqDto ToDto(
        Rfq rfq,
        IReadOnlyDictionary<Guid, (string Ar, string En)> supplierNames,
        IReadOnlyDictionary<Guid, string> staffNames) => new(
        rfq.ReferenceCode, rfq.OrganizationId, rfq.TitleAr, rfq.TitleEn, rfq.DescriptionAr, rfq.DescriptionEn,
        rfq.CurrencyCode, rfq.State, rfq.PublishAt, rfq.SubmissionOpensAt, rfq.SubmissionClosesAt,
        rfq.ClarificationDeadlineAt, rfq.EvaluationTargetDate, rfq.EvaluationTemplateId, rfq.EvaluationTemplateVersion,
        rfq.CancelReason,
        [.. rfq.Items.OrderBy(i => i.LineNo).Select(i => new RfqItemDto(
            i.Id, i.LineNo, i.TitleAr, i.TitleEn, i.SpecificationAr, i.SpecificationEn, i.CategoryCode,
            i.Quantity, i.UnitOfMeasureCode, i.IsUnitPrice, i.IsOptional))],
        [.. rfq.Requirements.Select(r => new RequirementDto(r.Id, r.TextAr, r.TextEn, r.IsMandatory, r.DocumentTypeCode, r.ExpectedEnvelope))],
        [.. rfq.Attachments.Select(a => new RfqAttachmentDto(a.Id, a.OriginalFileName, a.ContentType, a.Caption, a.UploadedAt))],
        [.. rfq.Approvals.OrderBy(a => a.StepNo).Select(a => new RfqApprovalDto(a.StepNo, a.ApproverUserId, a.Decision, a.Comment, a.DecidedAt))],
        [.. rfq.Invitations.OrderBy(i => i.InvitedAt).Select(i =>
        {
            (string Ar, string En) name = supplierNames.TryGetValue(i.SupplierId, out var n) ? n : ("", "");
            return new InvitationDto(i.Id, i.SupplierId, name.Ar, name.En, i.Status, i.InvitedAt, i.ViewedAt, i.RespondedAt, i.DeclineReason);
        })],
        [.. rfq.Clarifications.OrderBy(c => c.AskedAt).Select(c =>
        {
            (string Ar, string En) name = supplierNames.TryGetValue(c.AskedBySupplierId, out var n) ? n : ("", "");
            return new ClarificationDto(c.Id, c.AskedBySupplierId, name.Ar, name.En, c.Question, c.Answer, c.Visibility, c.AskedAt, c.AnsweredAt);
        })],
        [.. rfq.Addenda.OrderBy(a => a.IssuedAt).Select(a => new AddendumDto(a.Id, a.TitleAr, a.TitleEn, a.DescriptionAr, a.DescriptionEn, a.IssuedAt))],
        rfq.RowVersion,
        rfq.SubmissionDeadlineChangeReason, rfq.SubmissionDeadlineChangedAt,
        rfq.OwnerUserId, NameOf(staffNames, rfq.OwnerUserId),
        PendingApproverId(rfq), NameOf(staffNames, PendingApproverId(rfq)));

    private static Guid? PendingApproverId(Rfq rfq) =>
        rfq.Approvals.LastOrDefault(a => a.Decision is null)?.AssignedApproverUserId;

    private static string? NameOf(IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id is { } value && names.TryGetValue(value, out var name) ? name : null;

    public static SupplierRfqDto ToSupplierDto(Rfq rfq, Invitation myInvitation, Guid supplierId) => new(
        rfq.ReferenceCode, rfq.TitleAr, rfq.TitleEn, rfq.DescriptionAr, rfq.DescriptionEn, rfq.CurrencyCode, rfq.State,
        rfq.SubmissionOpensAt, rfq.SubmissionClosesAt, rfq.ClarificationDeadlineAt,
        [.. rfq.Items.OrderBy(i => i.LineNo).Select(i => new RfqItemDto(
            i.Id, i.LineNo, i.TitleAr, i.TitleEn, i.SpecificationAr, i.SpecificationEn, i.CategoryCode,
            i.Quantity, i.UnitOfMeasureCode, i.IsUnitPrice, i.IsOptional))],
        [.. rfq.Requirements.Select(r => new RequirementDto(r.Id, r.TextAr, r.TextEn, r.IsMandatory, r.DocumentTypeCode, r.ExpectedEnvelope))],
        [.. rfq.Attachments.Select(a => new RfqAttachmentDto(a.Id, a.OriginalFileName, a.ContentType, a.Caption, a.UploadedAt))],
        myInvitation.Status,
        [.. rfq.Clarifications
            .Where(c => c.AskedBySupplierId == supplierId || c.Visibility == ClarificationVisibility.PublishedToAll)
            .OrderBy(c => c.AskedAt)
            .Select(c => new SupplierClarificationDto(c.Id, c.Question, c.Answer, c.Visibility, c.AskedAt, c.AnsweredAt, c.AskedBySupplierId == supplierId))],
        [.. rfq.Addenda.OrderBy(a => a.IssuedAt).Select(a => new AddendumDto(a.Id, a.TitleAr, a.TitleEn, a.DescriptionAr, a.DescriptionEn, a.IssuedAt))],
        rfq.SubmissionDeadlineChangeReason, rfq.SubmissionDeadlineChangedAt);
}
