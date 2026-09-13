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

public sealed class GetRfqHandler(AppDbContext db, IScopeContext scope) : IGetRfqHandler
{
    public async Task<RfqDto?> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, referenceCode, ct);
        return rfq is null ? null : await RfqDtoMapper.ToDtoAsync(db, rfq, ct);
    }
}
