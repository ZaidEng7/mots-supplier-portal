using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Application.Suppliers;

public abstract record ProfileMutationResult
{
    public sealed record Success(SupplierDto Supplier) : ProfileMutationResult;
    public sealed record NotFoundOrOutOfScope : ProfileMutationResult;
    public sealed record InvalidState(string Reason) : ProfileMutationResult;
    /// <summary>MSP-77: refused because this section is not flagged in the reviewer's open
    /// information request (STORY-03.3.1 AC1).</summary>
    public sealed record NotEditable(string Reason) : ProfileMutationResult;
}

public abstract record RevealBankAccountResult
{
    public sealed record Success(string AccountNumber) : RevealBankAccountResult;
    public sealed record NotFoundOrOutOfScope : RevealBankAccountResult;
}
