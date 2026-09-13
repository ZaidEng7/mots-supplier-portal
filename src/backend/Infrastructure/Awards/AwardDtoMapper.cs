// Turning an award into its read model.
//
// The winning bid's public code is passed in rather than read here, because this mapper is static and holds no
// database context. The evaluation's mapper settled on the same shape for the same reason.

namespace MotsSupplierPortal.Infrastructure.Awards;

using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Awards;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class AwardDtoMapper
{
    public static AwardDto ToDto(Award award, string rfqReferenceCode, string winningProposalCode) => new(
        award.Id, rfqReferenceCode, award.State,
        award.WinningProposalId, winningProposalCode, award.JustificationAr, award.JustificationEn,
        award.RecommendedByUserId, award.RecommendedAt, award.RecommendationRevision,
        [.. award.Approvals.Select(a => new AwardApprovalDto(a.StepNo, a.ApproverUserId, a.Decision, a.Comment, a.DecidedAt))],
        award.AwardedAt, award.ComparisonSnapshotJson,
        award.ErpSyncStatus, award.ExternalPurchaseOrderRef, award.ErpSyncedAt, award.ErpRetryCount,
        award.RowVersion);
}
