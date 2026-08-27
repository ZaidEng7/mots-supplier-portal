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
