# ERP supplier field map

What the Seven Gates ERP can tell us about a supplier, what the portal stores, and what the ministry's
dashboard is owed. Three columns, one row per fact about a supplier.

**Status of this document.** The ERP column is real: it was read from the live sandbox on 2026-09-27 by asking
`GET /api/resource/Supplier?fields=["*"]`, so these are the fields that exist on that doctype. The *data* behind
them is not real — the sandbox holds three suppliers, all named "(seed)", with no contact details, no
identifiers and no addresses, and the `Address` table is empty across the whole instance. So this map says what
each field *would* carry. Whether it carries anything is question 1 in §7.

The ERP is ERPNext (Frappe) and its REST conventions are in the Seven Gates Posting API document: records live
at `/api/resource/<Type>`, list reads take `fields`, `filters` and `limit_page_length`, and datetimes arrive as
`YYYY-MM-DD HH:MM:SS` in **server local time with no zone**.

---

## 1. The map

Ministry column names are the ones in the Syria Hotels Dashboard workbook and are reproduced exactly, including
capitalisation. The portal column names the property the value is read from today, as
`MinistrySupplierFeedProjection` does it.

| ERP field | Portal field | Ministry column | Notes |
|---|---|---|---|
| `name` | `Supplier.ExternalId` | — | The ERP's own key. Never shown to the ministry; it is what we match on. |
| — | `Supplier.ReferenceCode` | `SupplierID` | Ours, minted on creation (`SUP-2026-000001`). The ERP has no say in it. |
| `supplier_name` | `LegalInfo.LegalNameEn` | `SupplierName` | |
| — | `LegalInfo.LegalNameAr` | `SupplierNameAr` | **The ERP has one name field, not two.** See §5. |
| — | — | `SupplierCode` | Always empty. The portal has no second code and the ERP's `name` is not for the ministry. |
| `supplier_group` | `Supplier.PrimaryCategoryCode` | `SupplierGroup` | Vocabularies do not overlap. See §5. |
| — | `Supplier.OnboardingState` | `ApprovalStatus` | Ours. An imported supplier is `Approved`; see §6. |
| `disabled` | `Supplier.LifecycleState` | `Disabled` | `1` becomes `Deactivated`. `on_hold` / `is_frozen` are separate ERP flags we ignore. |
| `default_currency` | `Supplier.CurrencyCode` | `DefaultCurrency` | Portal knows `SYP` and `USD` only. Anything else is refused, not silently dropped. |
| `supplier_type` | `LegalInfo.SupplierType` | `RegistrationType` | `Company` / `Individual` on both sides; `Partnership` is ours alone. See §5. |
| — | `LegalInfo.RegistrationNumber` | `CommercialRegisterNo` | **No ERP field for this.** See §4 — it is also what we match self-registrations against. |
| `tax_id` | `LegalInfo.TaxId` | `TaxID` | |
| `country` | `Address.Country` | `Country` | ERP sends a country *name* (`Syria`); the feed sends ISO alpha-2. Existing mapping handles it. |
| `Address.address_line1` | `Address.Line1` | `AddressLine` | Lives on a separate `Address` record, not on the supplier. See §3. |
| `Address.city` | `Address.City` | `City` | |
| `Address.state` | `Address.RegionCode` | `Governorate` | Free text on one side, a controlled list of **four** on the other. See §5. |
| — | `Address.Latitude` | `Latitude` | **No ERP field.** Only a supplier placing their own map pin fills this. |
| — | `Address.Longitude` | `Longitude` | As above. |
| `mobile_no`, or `Contact.mobile_no` | `Representative.Phone` | `Phone` | |
| `email_id`, or `Contact.email_id` | `Representative.Email` | `Email` | **Required to create a supplier at all.** See §2. |
| — | `Representative.UserId` | `PortalUser` | Set when the account is created, not by the ERP. |
| `creation` | `Supplier.CreatedAt` | `CreatedOn` | Parse as server local time. Reading it as UTC is wrong by the Damascus offset. |
| `modified` | `Supplier.UpdatedAt` | `LastModified` | As above. |

## 2. The one field that decides how much of this works

`email_id`.

`Supplier.Register()` takes a representative email as a **required** parameter — a supplier cannot be created
without one. An account they can log into needs a real mailbox on top of that, because the only way they get a
password is a link sent to it.

So the count of ERP suppliers carrying a usable email is the count of portal accounts this import can produce.
Every other gap in this document degrades a row. This one drops it.

## 3. Address and contact are not on the supplier

In ERPNext a supplier's street, city, phone and email are separate `Address` and `Contact` records, joined to the
supplier through a third table, `Dynamic Link`. One supplier is therefore up to three reads, not one.

Two routes to them, and the cheap one may be enough:

- `supplier_primary_address` and `supplier_primary_contact` sit directly on the supplier record and point at the
  right rows. If the real data fills them, we read those two names and fetch them directly.
- Otherwise we query `Dynamic Link`, which **returns 403 for the current sandbox key**. That permission is only
  worth asking for if the two fields above turn out to be empty.

`email_id` and `mobile_no` also exist directly on the supplier record. Which of the two places is populated is a
question about how their data was entered, not about the schema, so the import reads the supplier's own fields
first and falls back to the linked `Contact`.

## 4. What the ERP cannot give us

| Missing | Consequence |
|---|---|
| Arabic name | Every imported supplier's `SupplierNameAr` is the English name until somebody corrects it. |
| Commercial register number | The ministry column is empty, **and** we lose the identifier that catches a duplicate when an imported supplier later registers themselves. Tax ID is the fallback; it is weaker because not every supplier has one. |
| Latitude / longitude | Stays empty, as it does today. No accounting system holds map pins. This is the coordinate gap the ministry has already been told about. |
| Uploaded documents | Commercial register, tax certificate, insurance — none of it exists in the ERP. This is why an imported supplier cannot pass the normal approval gate. |
| Bank account details | The ERP has `default_bank_account` as a *link* to its own record, which is its banking, not the supplier's payment details as the portal models them. Treat as absent. |

## 5. Four value translations that need a decision, not a mapping

**Governorate.** The ERP's `state` is free text. The portal's `RegionCode` is a controlled list with **four
entries** — `DIM` Damascus, `ALP` Aleppo, `LAT` Latakia, `HOM` Homs. Syria has fourteen governorates. A supplier
in Tartous or Deir ez-Zor has nowhere to go. Either the reference list grows before the import, or those
suppliers arrive with no governorate. Growing the list is the smaller job and should happen first.

**Supplier group.** The ERP's eight groups are ERPNext's factory defaults — `Distributor`, `Electrical`,
`Hardware`, `Pharmaceutical`, `Raw Material`, `Services`, `Local`, `All Supplier Groups`. The portal's six
categories are the ministry's tourism taxonomy — Accommodation & Hotels, Catering & Hospitality, Transport, Tour
Operations, Events & Conferences, Maintenance & Technical Services. **There is no honest mapping between these
two lists.** `Local` is not a category of anything. Import should leave the category unset and let the ministry
classify, rather than invent a translation that quietly miscategorises every supplier.

**Legal type.** `Company` and `Individual` agree on both sides. The portal also has `Partnership`, which the ERP
cannot express, so a partnership imports as `Company`. Acceptable, and worth writing down so nobody later reads
it as data loss caused by a bug.

**Currency.** The portal knows `SYP` and `USD`. The ERP could send anything enabled on its instance. An unknown
currency fails the row loudly rather than defaulting to `SYP` — a supplier silently repriced into the wrong
currency is worse than a supplier who did not import.

## 6. Identity, matching and approval

**Matching** is on `Supplier.ExternalId`, holding the ERP's `name`. It is already on the aggregate, together with
`SyncStatus` and `LastSyncedAt` and the `MarkSynced()` / `MarkSyncFailed()` methods, written for exactly this and
unused until now.

A supplier who registered in the portal themselves has no `ExternalId`, so the nightly refresh passes over them
without needing to be told to.

**Approval cannot go through the normal path.** `Supplier.Approve()` requires the state to be `UnderReview` and
every required document to be present. Imported suppliers have no documents, so the walk through the nine
onboarding states ends in a refusal, by design and correctly.

The import therefore needs its own entry point that lands `Approved` / `Active` and records what actually
happened — imported from the ERP on this date, no portal review. The alternative, walking the states with the
document check suppressed, writes an audit trail claiming a reviewer reviewed them. No reviewer did.

**One consequence to carry forward.** An approved supplier who edits a compliance-critical field is sent back to
`UnderReview` automatically. The first time an imported supplier changes their register number, a reviewer will
open the file and find nothing in it.

## 7. Open questions

1. **Is that sandbox the right server for supplier master data?** It holds three seed suppliers and zero
   addresses. If the ~80 real suppliers are elsewhere, this map needs re-reading against that instance.
2. **Will the real records carry an email per supplier?** §2 — this is the one that decides scope.
3. **Will they carry a commercial register number?** If not, duplicate detection falls back to tax ID.
4. **Are `supplier_primary_contact` and `supplier_primary_address` populated?** If yes, the `Dynamic Link` 403
   never matters.
5. **Does the region list grow to all fourteen governorates before the import, or do out-of-list suppliers
   import without one?**
