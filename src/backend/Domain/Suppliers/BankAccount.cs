namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>FR-PROF-006/STORY-04.6.1, DOMAIN-MODEL.md BankAccountInfo shape. The account number is
/// never stored in plaintext: <see cref="EncryptedAccountNumber"/> is AES-256-GCM ciphertext
/// (FieldEncryptionService) and <see cref="MaskedAccountNumber"/> is the only value list/detail
/// views ever read directly - the encrypted value is decrypted only on an explicit, audited
/// reveal (BRULE-014/090/091: no PII in logs/URLs, sensitive-field access is audited).</summary>
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

    /// <summary>DOMAIN-MODEL.md: exactly one default bank account whenever any exist - the
    /// aggregate (Supplier.AddBankAccount/RemoveBankAccount) is solely responsible for maintaining
    /// this invariant, this setter is not meant to be flipped directly from outside the aggregate.</summary>
    public bool IsDefault { get; set; }
}
