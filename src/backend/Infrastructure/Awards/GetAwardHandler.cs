// Reading one tender's award.

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

public sealed class GetAwardHandler(AppDbContext db, IScopeContext scope) : IGetAwardHandler
{
    public async Task<AwardDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return null;

        return AwardDtoMapper.ToDto(
            loaded.Value.Award, loaded.Value.Rfq.ReferenceCode,
            await AwardWinner.CodeAsync(db, loaded.Value.Award, ct));
    }
}
