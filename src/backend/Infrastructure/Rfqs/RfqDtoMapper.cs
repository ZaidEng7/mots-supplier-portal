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

internal static class RfqDtoMapper
{
    /// <summary>Async because InvitationDto carries the invited supplier's display names
    /// (FEAT-08.7) - a small extra query per call, acceptable at this call volume (buyer-side RFQ
    /// mutation/detail endpoints, not a hot list path); ListRfqsHandler batches it once for the
    /// whole page instead of N+1 (see its own comment).</summary>
    public static async Task<RfqDto> ToDtoAsync(AppDbContext db, Rfq rfq, CancellationToken ct)
    {
        var supplierIds = rfq.Invitations.Select(i => i.SupplierId)
            .Concat(rfq.Clarifications.Select(c => c.AskedBySupplierId)).Distinct().ToList();
        var names = await SupplierNamesAsync(db, supplierIds, ct);
        return ToDto(rfq, names, await StaffNamesAsync(db, rfq, ct));
    }

    /// <summary>
    /// A-7: the display names for the owner and the current pass's assigned approver.
    ///
    /// <para>One query for both, and it returns the ids it could not resolve as absent rather than as
    /// an empty string: a deactivated user's row still exists, so a missing name here means the id
    /// points at nothing, which is a different fact from "this RFQ has no owner" and must not render
    /// as the same thing.</para>
    /// </summary>
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

    /// <summary>FEAT-10.3/FR-CLR-003: the anonymization boundary. Only <paramref name="supplierId"/>'s
    /// own clarifications (any Visibility) plus every OTHER supplier's PublishedToAll clarifications
    /// are included - a PrivateToAsker item belonging to someone else is not just anonymized, it is
    /// entirely absent from this list, matching OQ-008's "private to the asking supplier". IsMine is
    /// computed here, server-side, from the real AskedBySupplierId - never trust a client-supplied
    /// flag for this.</summary>
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
