using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Globalization;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

namespace MotsSupplierPortal.Infrastructure.Evaluation;

/// <summary>
/// T-068: proposal id to §3 reference code, for one tender.
///
/// <para>Every handler that returns an evaluation needs this, because a consolidated result carries a
/// code and nothing else now. One query and one answer, here rather than in each handler - six of the
/// eight call sites used to pass nothing, which is how a GUID reached the screen where a manager
/// decides who wins a tender.</para>
/// </summary>
internal static class EvaluationProposalCodes
{
    internal static async Task<IReadOnlyDictionary<Guid, string>> ForRfqAsync(
        AppDbContext db, Guid rfqId, CancellationToken ct) =>
        await db.Proposals.AsNoTracking()
            .Where(p => p.RfqId == rfqId)
            .ToDictionaryAsync(p => p.Id, p => p.ReferenceCode, ct);
}
