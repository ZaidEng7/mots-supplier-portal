// A supplier uploads their company logo.
//
// It reuses the same size and leading-byte checks the document upload uses, and accepts images only.
//
// Logos skip the quarantine and scan queue that documents go through, because a logo is displayed inline
// rather than reviewed as evidence. That makes this a deliberately lighter path, and it is flagged as one:
// it is not appropriate to reuse for anything a supplier cannot simply upload again if it is wrong.
//
// This is also what connects the domain's logo field to an actual upload path; both had existed unreachable.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

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
        await auditLogger.LogAsync("Supplier", supplier.Id, "logo_uploaded", scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new UploadLogoResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
