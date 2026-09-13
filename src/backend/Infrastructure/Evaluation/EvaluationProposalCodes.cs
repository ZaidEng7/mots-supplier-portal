// One tender's bid identifiers mapped to their public codes.
//
// Every handler that returns an evaluation needs this, because a consolidated result carries a code and
// nothing else now.
//
// One query and one answer, here rather than repeated in each handler. Six of the eight call sites used to
// pass nothing at all, which is how a database identifier reached the screen where a manager decides who wins
// a tender.

namespace MotsSupplierPortal.Infrastructure.Evaluation;

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

internal static class EvaluationProposalCodes
{
    internal static async Task<IReadOnlyDictionary<Guid, string>> ForRfqAsync(
        AppDbContext db, Guid rfqId, CancellationToken ct) =>
        await db.Proposals.AsNoTracking()
            .Where(p => p.RfqId == rfqId)
            .ToDictionaryAsync(p => p.Id, p => p.ReferenceCode, ct);
}
