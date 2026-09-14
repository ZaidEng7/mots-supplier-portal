// What a write to one of a supplier's attached things can answer.
//
// One refusal is worth naming separately: the section is not one the reviewer's open information request
// flagged, so the supplier may not currently edit it. That is a permission outcome rather than a clash of
// state, and the screen says something different about each.
//
// Revealing a bank account's real number has its own outcome type, because it is a read with an audit
// consequence rather than a write.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Domain.Suppliers;

public abstract record ProfileMutationResult
{
    public sealed record Success(SupplierDto Supplier) : ProfileMutationResult;
    public sealed record NotFoundOrOutOfScope : ProfileMutationResult;
    public sealed record InvalidState(string Reason) : ProfileMutationResult;
    public sealed record NotEditable(string Reason) : ProfileMutationResult;
}

public abstract record RevealBankAccountResult
{
    public sealed record Success(string AccountNumber) : RevealBankAccountResult;
    public sealed record NotFoundOrOutOfScope : RevealBankAccountResult;
}
