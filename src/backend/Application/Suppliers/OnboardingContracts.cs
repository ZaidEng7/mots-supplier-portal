using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Application.Suppliers;

public sealed record UpdateProfileCommand(
    string? Description,
    string? Website,
    string? SupplierGroup,
    string? CurrencyCode,
    string? PrimaryContactPhone);

public abstract record UpdateProfileResult
{
    public sealed record Success(SupplierDto Supplier) : UpdateProfileResult;
    public sealed record NotFoundOrOutOfScope : UpdateProfileResult;
    public sealed record InvalidState(string Reason) : UpdateProfileResult;
    /// <summary>BRULE-098/MSP-65: someone else changed this supplier since the caller read it.
    /// The write was refused, not merged and not overwritten. <paramref name="CurrentRowVersion"/>
    /// is the version now in the database so a client can re-read and retry deliberately.</summary>
    public sealed record Conflict(uint CurrentRowVersion) : UpdateProfileResult;
}

public interface IUpdateProfileHandler
{
    Task<UpdateProfileResult> HandleAsync(UpdateProfileCommand command, CancellationToken ct);
}

public sealed record UpdateLegalInfoCommand(
    string LegalNameAr,
    string LegalNameEn,
    string? RegistrationNumber,
    string? TaxId,
    SupplierLegalType SupplierType,
    DateOnly? EstablishedOn);

public interface IUpdateLegalInfoHandler
{
    Task<UpdateProfileResult> HandleAsync(UpdateLegalInfoCommand command, CancellationToken ct);
}

public abstract record AcceptTermsResult
{
    public sealed record Success(SupplierDto Supplier) : AcceptTermsResult;
    public sealed record NotFoundOrOutOfScope : AcceptTermsResult;
    public sealed record InvalidState(string Reason) : AcceptTermsResult;
}

public interface IAcceptTermsHandler
{
    Task<AcceptTermsResult> HandleAsync(CancellationToken ct);
}

public abstract record SubmitApplicationResult
{
    public sealed record Success(SupplierDto Supplier) : SubmitApplicationResult;
    public sealed record NotFoundOrOutOfScope : SubmitApplicationResult;
    public sealed record Incomplete(IReadOnlyList<string> MissingFields) : SubmitApplicationResult;
    public sealed record InvalidState(string Reason) : SubmitApplicationResult;
}

public interface ISubmitApplicationHandler
{
    Task<SubmitApplicationResult> HandleAsync(CancellationToken ct);
}
