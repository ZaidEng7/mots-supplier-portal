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

namespace MotsSupplierPortal.Infrastructure.Awards;

/// <summary>
/// T-068: the winning bid's public code, for the response that names it.
///
/// <para>One query in one place. The award record stores the proposal's id - it is a foreign key and
/// has to be - and what the API emits is the code, so exactly one translation is needed and this is
/// it.</para>
/// </summary>
internal static class AwardWinner
{
    public static async Task<string> CodeAsync(AppDbContext db, Award award, CancellationToken ct) =>
        await db.Proposals.AsNoTracking()
            .Where(p => p.Id == award.WinningProposalId)
            .Select(p => p.ReferenceCode)
            .FirstOrDefaultAsync(ct)
        // A recommendation always points at a live bid, and nothing deletes proposals. An empty
        // string rather than a throw if that ever stops being true: an award screen that renders a
        // blank winner is a bug report, and one that 500s on a read is an outage.
        ?? string.Empty;
}
