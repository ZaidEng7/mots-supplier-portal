# ERP supplier field map

What the Seven Gates ERP can tell us about a supplier, what the portal stores, and what the ministry's
dashboard is owed. Three columns, one row per fact about a supplier.

> **Since this was written.** This map was drawn up on 2026-09-27 (#216), before the import existed. The
> import has since been built (#218 to #230) and runs against the real Seven Gates server. It reads each
> supplier with its contact and address, then creates and updates portal suppliers, and suspends the
> ones the ERP no longer returns, has disabled or has not approved. It runs every hour, and an
> administrator can also start it by hand. How it works, and how to change it, is in
> [`docs/handbook/ERP-IMPORT.md`](../handbook/ERP-IMPORT.md).
>
> The rows and sections below have been corrected wherever the import made them false, and §7's questions
> are answered in place. Two things are still not built: nothing writes supplier data to the ERP, and the
> ERP's `creation` and `modified` are read but not stored. Where this map and the code disagree, the code
> is right.

**Status of this document.** The ERP column was first read from the live sandbox on 2026-09-27, by asking
`GET /api/resource/Supplier?fields=["*"]`, so those are the fields that exist on that doctype. The sandbox
*data* is not real. It holds three suppliers, all named "(seed)", with no contact details, no identifiers
and no addresses, and its `Address` table is empty across the whole instance. The real server adds the
custom fields Seven Gates created (`custom_supplier_arabic_name`, `custom_registration_number`,
`custom_registration_type`) and a `workflow_state`. The rows for those fields describe what the import
reads today.

The ERP is ERPNext (Frappe), and its REST conventions are in the Seven Gates Posting API document:

- records live at `/api/resource/<Type>`;
- list reads take `fields`, `filters` and `limit_page_length`;
- datetimes arrive as `YYYY-MM-DD HH:MM:SS` in **server local time with no zone**.

---

## 1. The map

Ministry column names are the ones in the Syria Hotels Dashboard workbook, reproduced exactly, including
capitalisation. The portal column names the property the value is read from today, as
`MinistrySupplierFeedProjection` reads it.

| ERP field | Portal field | Ministry column | Notes |
|---|---|---|---|
| `name` | `Supplier.ExternalId` | — | The ERP's own key. Never shown to the ministry; it is what we match on. |
| — | `Supplier.ReferenceCode` | `SupplierID` | Ours, minted on creation (`SUP-2026-000001`). The ERP has no say in it. |
| `supplier_name` | `LegalInfo.LegalNameEn` | `SupplierName` | |
| `custom_supplier_arabic_name` | `LegalInfo.LegalNameAr` | `SupplierNameAr` | A field Seven Gates added on the real server. When it is empty, a new supplier's Arabic name starts as the English one, and an existing supplier keeps its own. |
| — | — | `SupplierCode` | Always empty. The portal has no second code, and the ERP's `name` is not for the ministry. |
| `supplier_group` | `Supplier.SupplierGroup` | `SupplierGroup` | Kept as the ERP wrote it. The ministry column reads the portal *category* (`Supplier.PrimaryCategoryCode`). The import leaves the category unset, so the column stays empty until the ministry classifies the supplier. See §5. |
| — | `Supplier.OnboardingState` | `ApprovalStatus` | Ours. An imported supplier is `Approved`; see §6. |
| `disabled`, `workflow_state` | `Supplier.LifecycleState` | `Disabled` | `disabled` = `1`, or a `workflow_state` other than `Approved`, makes the supplier `Suspended`, never `Deactivated`. A new supplier arrives suspended; an existing active one is suspended once. The ministry column is true while the supplier is suspended or deactivated. `on_hold` and `is_frozen` are separate ERP flags that we ignore. |
| `default_currency` | `Supplier.CurrencyCode` | `DefaultCurrency` | The portal knows `SYP` and `USD` only. Any other currency is left empty, with a note, rather than guessed. |
| `supplier_type` | `LegalInfo.SupplierType` | `RegistrationType` | `Individual` imports as Individual; every other type imports as Company. See §5. |
| `custom_registration_number` | `LegalInfo.RegistrationNumber` | `CommercialRegisterNo` | A field Seven Gates added on the real server. It is also what we match self-registrations against. A number that another portal supplier already holds is left empty with a note, as is a number the ERP gives to two suppliers. |
| `custom_registration_type` | `LegalInfo.RegistrationType` | — | A field Seven Gates added on the real server. |
| `supplier_details` | `Supplier.Description` | — | Cut to the portal's 2000 characters, with a note. |
| `tax_id` | `LegalInfo.TaxId` | `TaxID` | |
| `Address.country` | `Address.Country` | `Country` | An address whose country is anything other than `Syria` is not imported. An address with no country is taken as Syrian. Either way it is stored as `SY`, and the feed sends ISO alpha-2. The supplier record's own `country` is read and not used. |
| `Address.address_line1` | `Address.Line1` | `AddressLine` | Lives on a separate `Address` record, not on the supplier. See §3. |
| `Address.city` | `Address.City` | `City` | |
| — | `Address.RegionCode` | `Governorate` | The import does not read the ERP's `state`. It finds the governorate named in the street line, then in the city. See §5. |
| — | `Address.Latitude` | `Latitude` | **No ERP field.** Only a supplier placing their own map pin fills this. |
| — | `Address.Longitude` | `Longitude` | As above. |
| `mobile_no`, or `Contact.mobile_no` | `Representative.Phone` | `Phone` | |
| `email_id`, or `Contact.email_id` | `Representative.Email` | `Email` | A supplier without an email still gets an account, on a placeholder address. See §2. |
| `Contact.full_name` | `Representative.FullName` | — | Only when the contact is a person. The ERP names contacts it makes itself `<supplier> Contact`, and then the representative is named after the company. |
| — | `Representative.UserId` | `PortalUser` | Set when the account is created, not by the ERP. |
| `creation` | — | `CreatedOn` | Parsed as server local time, then stored nowhere. The ministry column reads `Supplier.CreatedAt`, which is when the portal created the record. |
| `modified` | — | `LastModified` | As above; the ministry column reads `Supplier.UpdatedAt`. |

## 2. The one field that decided how much of this works

`email_id`.

A portal supplier needs a representative email, and an account needs a sign-in address. The first plan
was to drop any supplier with no email. The import does not drop it. A supplier with no email on its
record or on its linked Contact gets a placeholder on `erp-import.invalid`. That domain is reserved, so
nothing sent there can ever be delivered, and the row's note says so.

Accounts are created already confirmed, with the configured initial password, and no email is sent.
When the ERP later has a real address, the next run moves the login onto it.

So every ERP supplier becomes a portal account. What the email decides is whether anybody can reach the
person behind that account. Most real records carry no email.

## 3. Address and contact are not on the supplier

In ERPNext a supplier's street, city, phone and email are separate `Address` and `Contact` records,
joined to the supplier through a third table, `Dynamic Link`.

The import reads them in one request each, not one request per supplier. The Contact and Address lists
can be filtered on their `Dynamic Link` child rows, and can return the linked supplier's name from them.
So the import never lists `Dynamic Link` itself, and the 403 that listing gave the sandbox key no longer
matters.

A supplier's address is chosen in this order:

1. the address named by `supplier_primary_address`;
2. an address ticked as primary;
3. a billing address;
4. the first by name.

A disabled address is never chosen. `supplier_primary_contact` is read but not used.

`email_id` and `mobile_no` also exist directly on the supplier record. Which of the two places holds them
is a question of how the data was entered, not of the schema, so the import reads the supplier's own
fields first and falls back to the linked `Contact`.

## 4. What the ERP cannot give us

| Missing | Consequence |
|---|---|
| Latitude / longitude | Stays empty, as it does today. No accounting system holds map pins. This is the coordinate gap the ministry has already been told about. |
| Uploaded documents | Commercial register, tax certificate, insurance: none of it exists in the ERP. This is why an imported supplier cannot pass the normal approval gate. |
| Bank account details | The ERP has `default_bank_account` as a *link* to its own record. That is the ERP's own banking, not the supplier's payment details as the portal models them. Treat it as absent. |

This table used to list the Arabic name and the commercial register number as well. The real server
carries both, in custom fields (see §1).

It does not always carry them cleanly. The server gives registration number 14142 to two suppliers. The
first keeps it, and the second arrives with the field empty and a note, because the portal allows one
supplier per number.

## 5. Four value translations that need a decision, not a mapping

**Governorate: decided and built.** The portal's list now holds all fourteen governorates (#217). The
import does not read the ERP's free-text `state`. `ErpAddressMapper` looks for a governorate named in the
street line first, then in the city, because the city field says Damascus for nearly everybody. Some
addresses are not imported: one outside Syria, one with no street or no city, and one that names no
governorate. The note says which case it was.

**Supplier group: decided and built.** On the sandbox, the ERP's eight groups are ERPNext's factory
defaults: `Distributor`, `Electrical`, `Hardware`, `Pharmaceutical`, `Raw Material`, `Services`,
`Local`, `All Supplier Groups`. The real server uses its own purchasing vocabulary. The portal's six
categories are the ministry's tourism taxonomy: Accommodation & Hotels, Catering & Hospitality,
Transport, Tour Operations, Events & Conferences, Maintenance & Technical Services.

**There is no honest mapping between the ERP's groups and the portal's categories**, so the import leaves
the category unset for the ministry to classify. It keeps the ERP's group on `Supplier.SupplierGroup`,
exactly as the ERP wrote it.

**Legal type: still open.** `Company` and `Individual` agree on both sides. Seven Gates confirmed on
2026-09-29 that their ERP also has `Partnership`, as the portal does. The import still maps only
`Individual` (`LegalTypeOf` in `RunErpImportHandler`), so a partnership in the ERP imports as `Company`.
Fixing that is logged and deferred.

**Currency: decided and built.** The portal knows `SYP` and `USD`. The ERP could send any currency
enabled on its instance. An unknown currency is left empty, with a note, rather than defaulted to `SYP`:
a supplier silently repriced into the wrong currency is worse than one whose currency is not known.

## 6. Identity, matching and approval

**Matching** is on `Supplier.ExternalId`, which holds the ERP's `name`. `MarkSynced()` is called on
every run that finds the supplier (except when the run's suspensions are held back and the supplier is
marked as gone), and it sets `SyncStatus` to Synced; `LastSyncedAt` moves only when the ERP changed or
re-linked the supplier. The unused
`MarkSyncFailed()` has been removed.

`SyncStatus` also carries the sync's memory of a supplier leaving the ERP. Read the warning in
`ERP-IMPORT.md` §4 before using it for anything else.

A supplier who registered in the portal themselves has no `ExternalId`, so the hourly sync passes over
them without needing to be told to.

**Approval cannot go through the normal path.** `Supplier.Approve()` requires the state to be
`UnderReview` and every required document to be present. Imported suppliers have no documents, so a walk
through the nine onboarding states ends in a refusal, by design and correctly.

The import therefore has its own entry point, `Supplier.ImportFromErp`. It lands the supplier
`Approved`, and `Active` unless the ERP turns the supplier away. The run then records what actually
happened, in an audit row: imported from the ERP, and approved without portal review. The alternative, walking
the states with the document check suppressed, writes an audit trail claiming a reviewer reviewed them. No
reviewer did.

**One consequence to carry forward.** An approved supplier who edits a compliance-critical field is sent
back to `UnderReview` automatically. The first time an imported supplier changes their register number,
a reviewer will open the file and find nothing in it.

## 7. Open questions, and their answers

1. **Is that sandbox the right server for supplier master data?** No. The real registry is on Seven
   Gates' real server, which the import now reads. This map has been re-read against the import's code
   for it.
2. **Will the real records carry an email per supplier?** Mostly not. A supplier without one gets a
   placeholder login (§2).
3. **Will they carry a commercial register number?** Yes, in `custom_registration_number`. One number is
   shared by two suppliers (§4).
4. **Are `supplier_primary_contact` and `supplier_primary_address` populated?** This no longer matters.
   Contacts and addresses are read by filtering on their `Dynamic Link` rows (§3), so the 403 never comes
   into play.
5. **Does the region list grow to all fourteen governorates before the import?** Yes, it did (#217). An
   address outside Syria is not imported, so that supplier has no governorate.
