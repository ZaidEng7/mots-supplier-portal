// Creating, in the ministry's ERP, a supplier that registered in the portal.
//
// WHY THIS IS A PORT, as IErpSupplierSource is for reading. The push decides in portal terms - is the supplier there
// yet, is its contact, which call comes next - and it will be tested far more often than the transport. Keeping the
// ERP behind an interface lets the push be driven from a fake, and keeps tokens, encoded paths and the ERP's error
// envelope out of the Application layer.
//
// IT COVERS THE ERP COLLEAGUE'S SIX CALLS, IN HIS ORDER, and the reads a push needs around them:
//
//   1 CreateSupplierAsync     the Supplier record; its name comes back and becomes the portal's ExternalId
//   2 CreateAddressAsync      the address, linked to that Supplier
//   3 CreateContactAsync      the representative as a contact, linked the same way
//   4 CreateUserAsync         a website user for the representative, with the Supplier role and no password
//   5 AddPortalUserAsync      that user added to the Supplier's portal users
//   6 SetContactUserAsync     the contact pointed at the user, which is needed when the contact was made first
//
//   ReadFieldsAsync           the fields a record type has on this ERP. The test server and the real one differ, so
//                             ErpSupplierPayload leaves out what the ERP does not have; a run reads each list once.
//   FindSuppliersByTaxIdAsync           Supplier records that could already be this supplier, by its tax number.
//   FindSuppliersCreatedByPortalAsync   the fallback for a create whose answer was lost: records with this name that
//                                       the portal's own API user created since the attempt began. The ERP makes a
//                                       second supplier for a second request, so a push looks here before it creates
//                                       again. since is the portal's clock and the ERP's differs from it, so a caller
//                                       passes a time early enough to allow for that.
//   FindUserAsync, ListLinkedAddressesAsync, ListLinkedContactsAsync
//                                       what a push that stopped part-way already made, so it creates only the rest.
//
// THE BODIES ARE ErpSupplierPayload's, in the ERP's field names, and go to the ERP as they are. Each create returns the
// ERP's name for the new record. What a failure throws is the implementation's, and ErpSupplierRegistrar says it.

namespace MotsSupplierPortal.Application.Integration;

using System.Text.Json.Nodes;

public static class ErpRecordType
{
    public const string Supplier = "Supplier";
    public const string Address = "Address";
    public const string Contact = "Contact";
    public const string User = "User";
}

// One field of a record type as this ERP defines it. Options are a Select field's choices, or the record type a Link
// field points at. MaxLength is the longest value the ERP stores, when that is known.
public sealed record ErpFieldDefinition(string Name, string Type, IReadOnlyList<string> Options, int? MaxLength);

public sealed class ErpRecordFields(string recordType, IEnumerable<ErpFieldDefinition> fields)
{
    private readonly Dictionary<string, ErpFieldDefinition> _fields =
        fields.GroupBy(f => f.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public string RecordType { get; } = recordType;

    public IReadOnlyCollection<string> Names => _fields.Keys;

    public bool Has(string name) => _fields.ContainsKey(name);

    public ErpFieldDefinition? this[string name] => _fields.GetValueOrDefault(name);
}

public sealed record ErpSupplierMatch(string Name, string? SupplierName, string? TaxId);

public sealed record ErpLinkedAddress(string Name, string? AddressType, string? Line1, string? City);

public sealed record ErpLinkedContact(string Name, string? Email, string? User);

public interface IErpSupplierRegistrar
{
    Task<ErpRecordFields> ReadFieldsAsync(string recordType, CancellationToken ct);

    Task<IReadOnlyList<ErpSupplierMatch>> FindSuppliersByTaxIdAsync(string taxId, CancellationToken ct);

    Task<IReadOnlyList<ErpSupplierMatch>> FindSuppliersCreatedByPortalAsync(
        string supplierName, DateTimeOffset since, CancellationToken ct);

    Task<string> CreateSupplierAsync(JsonObject body, CancellationToken ct);

    Task<string> CreateAddressAsync(JsonObject body, CancellationToken ct);

    Task<string> CreateContactAsync(JsonObject body, CancellationToken ct);

    Task<string> CreateUserAsync(JsonObject body, CancellationToken ct);

    Task<string?> FindUserAsync(string email, CancellationToken ct);

    Task<IReadOnlyList<ErpLinkedAddress>> ListLinkedAddressesAsync(string erpSupplierName, CancellationToken ct);

    Task<IReadOnlyList<ErpLinkedContact>> ListLinkedContactsAsync(string erpSupplierName, CancellationToken ct);

    Task AddPortalUserAsync(string erpSupplierName, string user, CancellationToken ct);

    Task SetContactUserAsync(string contactName, string user, CancellationToken ct);
}
