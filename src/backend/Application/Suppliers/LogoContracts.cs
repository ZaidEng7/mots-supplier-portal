// Uploading a supplier's logo, and asking for a link to read it back.
//
// The upload takes the file as a stream rather than bytes, so a large image is not held in memory to be
// rejected for being large.

namespace MotsSupplierPortal.Application.Suppliers;

public sealed record UploadLogoCommand(Stream Content, string OriginalFileName, long SizeBytes);

public abstract record UploadLogoResult
{
    public sealed record Success(SupplierDto Supplier) : UploadLogoResult;
    public sealed record NotFoundOrOutOfScope : UploadLogoResult;
    public sealed record TooLarge : UploadLogoResult;
    public sealed record UnsupportedType : UploadLogoResult;
    public sealed record ContentMismatch : UploadLogoResult;
    public sealed record NotEditable(string Reason) : UploadLogoResult;
}

public interface IUploadLogoHandler
{
    Task<UploadLogoResult> HandleAsync(UploadLogoCommand command, CancellationToken ct);
}

public abstract record LogoDownloadUrlResult
{
    public sealed record Success(string Url) : LogoDownloadUrlResult;
    public sealed record NotFoundOrOutOfScope : LogoDownloadUrlResult;
}

public interface IGetLogoDownloadUrlHandler
{
    Task<LogoDownloadUrlResult> HandleAsync(CancellationToken ct);
}
