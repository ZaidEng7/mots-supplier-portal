// The vocabulary for editing a supplier's core profile and for what that edit can answer.
//
// Every field is a patch, so a field absent from the request is left untouched while a field present and empty
// is explicitly cleared. Plain optional fields cannot express that difference.
//
//
// THE THREE REFUSALS
//
// Somebody else changed this supplier since the caller read it. The write was refused rather than merged or
// overwritten, and the version now in the database travels back so a client can re-read and retry
// deliberately.
//
// The field is not one the reviewer's open information request flagged, so the supplier may not currently edit
// it.
//
// And the ordinary not-found, which also covers a supplier outside the caller's scope.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;

public sealed record UpdateProfileCommand(
    Patch<string?> Description,
    Patch<string?> Website,
    Patch<string?> SupplierGroup,
    Patch<string?> CurrencyCode,
    Patch<string?> PrimaryContactPhone);

public abstract record UpdateProfileResult
{
    public sealed record Success(SupplierDto Supplier) : UpdateProfileResult;
    public sealed record NotFoundOrOutOfScope : UpdateProfileResult;
    public sealed record InvalidState(string Reason) : UpdateProfileResult;
    public sealed record Conflict(uint CurrentRowVersion) : UpdateProfileResult;
    public sealed record NotEditable(string Reason) : UpdateProfileResult;
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
