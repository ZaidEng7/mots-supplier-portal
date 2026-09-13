using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class GetLogoDownloadUrlHandler(AppDbContext db, IScopeContext scope, IFileStorage fileStorage) : IGetLogoDownloadUrlHandler
{
    public async Task<LogoDownloadUrlResult> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return new LogoDownloadUrlResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier?.LogoStorageKey is null) return new LogoDownloadUrlResult.NotFoundOrOutOfScope();

        var url = await fileStorage.GetSignedDownloadUrlAsync(supplier.LogoStorageKey, TimeSpan.FromMinutes(5), "logo", ct);
        return new LogoDownloadUrlResult.Success(url);
    }
}
