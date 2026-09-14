// A supplier's bank account.
//
// The account number is never stored in plain text. EncryptedAccountNumber holds it encrypted, and
// MaskedAccountNumber is the only form any list or detail screen reads. The real number is decrypted
// only on an explicit reveal, and that reveal is audited.
//
// That is the same rule that keeps personal data out of logs and out of web addresses, applied to the
// single most sensitive field on a supplier's profile.
//
// IsDefault marks the one account to use when only one is needed. Exactly one account is the default
// whenever any exist, and the supplier record's own add and remove methods are solely responsible for
// keeping that true. This setter is not meant to be flipped from outside.

namespace MotsSupplierPortal.Domain.Suppliers;

public sealed class BankAccount
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public required string AccountHolderName { get; set; }
    public required string BankName { get; set; }
    public string? BranchName { get; set; }
    public required byte[] EncryptedAccountNumber { get; set; }
    public required string MaskedAccountNumber { get; set; }
    public string? SwiftBic { get; set; }
    public required string CurrencyCode { get; set; }

    public bool IsDefault { get; set; }
}
