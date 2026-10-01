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
// are the ERP's business and none of ours, so this carries what the portal and the ministry's feed actually
// read and nothing else. A field added here should be a field something consumes; Country, CreatedAt, ModifiedAt
// and PrimaryContactName are the exceptions today - they are mapped, and nothing reads them yet.
//
// EVERY FIELD EXCEPT THE IDENTIFIER, THE DISABLED FLAG AND THE TIMESTAMPS IS NULLABLE, and that is the shape of the
// real data rather than defensive habit. The test instance held three suppliers carrying a name, a group, a type and
// a country, with no tax number, no email and no address at all, so the type refuses to pretend: a supplier with
// nothing but a name is a supplier this can represent, and the import decides what to do about it.
//
// A ROW WITH NO EMAIL STILL BECOMES AN ACCOUNT. A portal login needs an address, so ErpImportAdmission gives such a
// supplier a placeholder that can never deliver, and says so in the row's notes, rather than leaving it out or
// inventing an address that looks real.

// THE REAL SERVER FILLS MORE THAN THE TEST ONE DID, and the second group of fields is what it fills: an Arabic name
// and a registration number in fields Seven Gates added themselves, an approval state from their workflow, a contact
// person's name, and a billing address. They come last and default to null, because a server without those custom
// fields - the test instance is one - still has to be readable, and a supplier without them is still a supplier.
//
// WorkflowState IS WHATEVER THE ERP'S APPROVAL WORKFLOW SAYS, or null where there is no workflow. ContactPersonName
// is the name on the supplier's contact only when that contact is a person; the ERP names a contact it made on its
// own "<supplier> Contact", and that is not somebody to address a letter to.
//
// CreatedByPortal SAYS THE PORTAL'S OWN API USER OWNS THE RECORD: the push created it, for a supplier that registered
// in the portal. One the portal does not carry yet is the push's create whose name was not saved, which the push links
// on its next attempt, so the import leaves it alone rather than making a second portal supplier of it.

namespace MotsSupplierPortal.Application.Integration;

public sealed record ErpSupplierAddress(string? Line1, string? Line2, string? City, string? Country);

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
    DateTimeOffset ModifiedAt,
    string? ArabicName = null,
    string? RegistrationNumber = null,
    string? RegistrationType = null,
    string? Description = null,
    string? WorkflowState = null,
    string? ContactPersonName = null,
    ErpSupplierAddress? Address = null,
    bool CreatedByPortal = false);

// ListSupplierGroupsAsync is the one read that is not about a supplier. It lists the ERP supplier groups a supplier
// can be filed under, for the integrations screen, where an administrator picks the group every supplier the portal
// creates in the ERP goes into. It sits on this port because it is a read over the same connection and credential.
public interface IErpSupplierSource
{
    Task<IReadOnlyList<ErpSupplier>> ListSuppliersAsync(CancellationToken ct);

    Task<IReadOnlyList<string>> ListSupplierGroupsAsync(CancellationToken ct);
}
