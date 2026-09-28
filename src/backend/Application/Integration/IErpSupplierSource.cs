// Reading suppliers out of the ministry's ERP.
//
// WHY THIS IS A PORT AND NOT A CLIENT. The ERP is to become master for supplier data, which means the import
// that writes portal suppliers will be tested far more often than the transport that fetches them. Keeping the
// fetch behind an interface lets that import be driven from a list in memory, and keeps the one place that
// knows about tokens, percent-encoded doctypes and timestamps without zones out of the Application layer
// entirely.
//
// ErpSupplier IS DELIBERATELY NOT THE ERP'S RECORD. A supplier there carries forty-four fields, most of them
// about purchasing - payment terms, price lists, whether invoices may be raised without a purchase order. Those
// are the ERP's business and none of ours, so this carries the fourteen the portal and the ministry's feed
// actually read and nothing else. A field added here should be a field something consumes.
//
// EVERY FIELD EXCEPT THE IDENTIFIER AND THE TIMESTAMPS IS NULLABLE, and that is the shape of the real data
// rather than defensive habit. The test instance holds three suppliers carrying a name, a group, a type and a
// country, with no tax number, no email and no address at all. Whether the real instance fills them is a
// question nobody has answered yet, so the type refuses to pretend: a supplier with nothing but a name is a
// supplier this can represent, and the import decides what to do about it.
//
// EMAIL IS THE FIELD THAT DECIDES SCOPE. A portal account needs a mailbox to send a password link to, and
// Supplier.Register refuses to create a supplier without a representative email. A row arriving here with
// Email null cannot become an account, so the import reports it rather than inventing an address.

namespace MotsSupplierPortal.Application.Integration;

public sealed record ErpSupplier(
    string ExternalId,
    string? Name,
    string? SupplierGroup,
    string? LegalType,
    string? TaxId,
    string? Country,
    string? Email,
    string? Phone,
    bool Disabled,
    string? Currency,
    string? PrimaryAddressName,
    string? PrimaryContactName,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);

public interface IErpSupplierSource
{
    Task<IReadOnlyList<ErpSupplier>> ListSuppliersAsync(CancellationToken ct);
}
