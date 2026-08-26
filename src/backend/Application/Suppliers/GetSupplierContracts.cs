namespace MotsSupplierPortal.Application.Suppliers;

public sealed record SupplierDto(string ReferenceCode, string DisplayNameAr, string DisplayNameEn, string OnboardingState);

public abstract record GetSupplierResult
{
    public sealed record Found(SupplierDto Supplier) : GetSupplierResult;
    public sealed record NotFoundOrOutOfScope : GetSupplierResult;
}

public interface IGetSupplierHandler
{
    /// <summary>Row-scoped: a caller may only ever resolve their own SupplierId (STORY-01.8.1).</summary>
    Task<GetSupplierResult> HandleAsync(string referenceCode, CancellationToken ct);
}
