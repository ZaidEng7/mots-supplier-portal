using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.1: wires the previously-dead Supplier.SetLogo/LogoStorageKey to an actual
/// upload path, reusing the same magic-byte/size validation UploadDocumentHandler uses. Images
/// only (no PDF) - logos are displayed inline, not reviewed like compliance documents, so this
/// skips the quarantine+AV-scan queue documents go through; flagged as a lighter-weight path,
/// not appropriate to reuse verbatim for anything the supplier can't just re-upload if wrong.</summary>
public sealed class UploadLogoHandler(AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger) : IUploadLogoHandler
{
    private static readonly HashSet<string> AllowedImageContentTypes = ["image/png", "image/jpeg"];

    public async Task<UploadLogoResult> HandleAsync(UploadLogoCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new UploadLogoResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new UploadLogoResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Logo, ct);
        if (refusal is not null) return new UploadLogoResult.NotEditable(refusal);

        if (command.SizeBytes <= 0 || command.SizeBytes > FileTypeSniffer.MaxSizeBytes)
        {
            return new UploadLogoResult.TooLarge();
        }

        var extension = Path.GetExtension(command.OriginalFileName).ToLowerInvariant();
        if (!FileTypeSniffer.AllowedExtensionToContentType.TryGetValue(extension, out var expectedContentType) || !AllowedImageContentTypes.Contains(expectedContentType))
        {
            return new UploadLogoResult.UnsupportedType();
        }

        await using var buffered = new MemoryStream();
        await command.Content.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        var header = new byte[16];
        var headerRead = await buffered.ReadAsync(header.AsMemory(0, 16), ct);
        buffered.Position = 0;

        if (!FileTypeSniffer.TryDetectContentType(header[..Math.Max(headerRead, 0)], out var sniffedContentType) || sniffedContentType != expectedContentType)
        {
            return new UploadLogoResult.ContentMismatch();
        }

        var key = $"logos/{supplier.Id}/{Guid.NewGuid():N}{extension}";
        try
        {
            supplier.SetLogo(key);
        }
        catch (DomainException ex)
        {
            return new UploadLogoResult.NotEditable(ex.Message);
        }

        await fileStorage.SaveAsync(key, buffered, expectedContentType, ct);
        await auditLogger.LogAsync("Supplier", supplier.Id, "logo_uploaded", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new UploadLogoResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

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
