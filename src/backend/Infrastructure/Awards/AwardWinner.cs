// The winning bid's public code, for the response that names it.
//
// One query in one place. The award stores the bid's internal identifier, because it is a foreign key and has to
// be, and what the interface emits is the code, so exactly one translation is needed.
//
// A recommendation always points at a live bid and nothing deletes bids. It returns an empty string rather than
// throwing if that ever stops being true: an award screen that renders a blank winner is a bug report, and one
// that fails on a read is an outage.

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

internal static class AwardWinner
{
    public static async Task<string> CodeAsync(AppDbContext db, Award award, CancellationToken ct) =>
        await db.Proposals.AsNoTracking()
            .Where(p => p.Id == award.WinningProposalId)
            .Select(p => p.ReferenceCode)
            .FirstOrDefaultAsync(ct)
        ?? string.Empty;
}
