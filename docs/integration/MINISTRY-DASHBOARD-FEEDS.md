# Ministry dashboard feeds — integration guide

This is the document their requirements sheet asks for under "API documentation (endpoints, fields, error
codes)". It is written for the person building the nightly load, not for us, so it says what the feeds do rather
than why they are built that way. The reasons live in the code.

It covers feeds **1 (Suppliers)** and **4 (Requests for quotation)** of the Syria Hotels Dashboard workbook.
Feeds 2 and 3 — supplier documents, items and payments — are the ERP's and are not served here.

---

## 1. Getting a credential

The feeds need an API key. Keys are issued by the ministry's own system administrator in the portal, under
**Back office → API keys**.

A key:

- is shown **once**, at creation — the portal stores only a hash and cannot show it again
- carries one permission, `supplier.registry.export`, and is refused everywhere else
- expires after **365 days** by default, and the expiry date is shown in the list
- may be restricted to one or more source addresses (see §6)
- can be revoked at any time, taking effect on the next request

To rotate a key without an outage: issue the second key, switch the loader over, then revoke the first. There is
no edit — a key's reach and expiry are fixed at issue.

## 2. Authenticating

Send the key in the `Authorization` header:

```
Authorization: ApiKey mots_a1b2c3d4_XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
```

The part before the second underscore is the key's **prefix**. It identifies the key in our logs and audit
trail and is safe to record on your side; the rest is the secret and is not.

Every request over HTTPS. A request without a usable key answers **401**.

## 3. The endpoints

| Feed | Method and path |
|---|---|
| 1 · Suppliers | `GET /api/v1/feeds/suppliers` |
| 4 · Requests for quotation | `GET /api/v1/feeds/rfqs` |

Both accept the same query parameters and both answer either CSV or JSON.

### Choosing a format

| Want | Send |
|---|---|
| JSON (recommended for a loader) | `Accept: application/json` |
| CSV file | no `Accept` header, or anything other than `application/json` |

CSV is the default so that a person clicking a link in the portal still receives a file.

### Query parameters

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `limit` | integer | `500` | Rows per page. Values above `2000` are reduced to 2000; zero or negative are treated as absent. |
| `cursor` | string | — | Opaque. Take it from `pagination.nextCursor` of the previous page and send it back unchanged. |
| `modified_since` | ISO 8601 timestamp | — | Only rows changed strictly **after** this instant. |

Paging and `modified_since` apply to the JSON representation. The CSV download is always the whole feed.

## 4. The JSON envelope

```json
{
  "data": [ { "SupplierID": "SUP-2026-000042", "...": "..." } ],
  "pagination": {
    "mode": "cursor",
    "nextCursor": "U1VQLTIwMjYtMDAwMDQy",
    "prevCursor": null,
    "pageSize": 500,
    "totalCount": null,
    "hasMore": true
  },
  "meta": { "sort": "SupplierID", "filtersApplied": ["modified_since"] }
}
```

Read until `hasMore` is `false`. When it is, `nextCursor` is `null`.

**Field names are exactly the workbook's**, including capitalisation: `SupplierID`, not `supplierID`.

## 5. Loading incrementally

1. **First load:** call without `modified_since`, following cursors to the end.
2. **Each night after:** record the time you started the previous successful run, and pass it as
   `modified_since` on the next.

Two things to build for:

- **Upsert, never insert.** A row edited while you are part-way through paging can appear on two pages. Key feed
  1 on `SupplierID` and feed 4 on `RFQNo` + `SupplierID`.
- **Use a timestamp with a zone.** `2026-09-21T10:15:00Z` is read as given. A value without a zone is read as
  UTC, which is the safe reading but not the one a local-time loader intends — so send the `Z`.

**What counts as modified:**

| Feed | A row comes back when |
|---|---|
| 1 · Suppliers | anything about the supplier changes, including their address, representative or bank details |
| 4 · RFQs | the invitation is created, **or** the supplier's bid changes — including a corrected price with no change of status |

## 6. Restricting a key to your server

If you tell us the outbound address of the machine running the load, the key can be restricted to it. Addresses
are given as a single address or a CIDR range, for example `10.42.0.0/24`. A key with no list works from
anywhere, which is how keys are issued today.

A request from an address the list does not cover is refused with **401**, the same as an unknown key. We do not
distinguish, deliberately: a caller learns whether their credential works and nothing about why it does not.

## 7. Error codes

| Status | Meaning | What to do |
|---|---|---|
| **200** | Success | — |
| **400** | `cursor` is not one we issued, or `modified_since` is not a readable timestamp | Fix the request. A malformed value is never treated as "no filter" — that would silently re-load everything. |
| **401** | Key missing, unknown, wrong secret, expired, revoked, or used from an address its list does not cover | Check the key; ask the ministry to issue a new one if it has expired. |
| **403** | The key is being used on a route it has no permission for | These feeds are the only routes a feed key may call. |
| **429** | Too many requests | Back off and retry. |
| **5xx** | Our fault | Retry with backoff; the run can be repeated safely. |

Errors are RFC 9457 problem documents:

```json
{ "type": "...", "title": "The cursor is not one this feed issued.", "status": 400 }
```

## 8. Fields

### Feed 1 — Suppliers

`SupplierID`, `SupplierName`, `SupplierNameAr`, `SupplierCode`, `SupplierGroup`, `ApprovalStatus`, `Disabled`,
`DefaultCurrency`, `RegistrationType`, `CommercialRegisterNo`, `TaxID`, `Country`, `AddressLine`, `City`,
`Governorate`, `Latitude`, `Longitude`, `Phone`, `Email`, `PortalUser`, `CreatedOn`, `LastModified`

Notes against the workbook:

- **`SupplierCode`** is always empty. The portal has no second internal code; `SupplierID` is the only identifier.
- **`ApprovalStatus`** is one of `Approved`, `Pending Financial Approval`, `Draft` — our nine onboarding states
  mapped onto your three.
- **`RegistrationType`** uses our vocabulary (`Company`, `Individual`, `Partnership`), not
  Commercial/Industrial/Individual.
- **`Disabled`** and **`PortalUser`** are `0` or `1` in both formats, as the sheet specifies.
- **`Country`** is ISO 3166 alpha-2. A spelling we have not mapped passes through unchanged rather than being
  guessed at.
- **`Latitude`** and **`Longitude`** are numbers in JSON, six decimal places in CSV, and `null` where the
  supplier has not yet placed their map pin.
- **`LastModified`** carries a `Z` in JSON; the CSV form has no zone and is UTC.
- A supplier with no address still has a row, with the six address fields empty.

### Feed 4 — Requests for quotation

`RFQNo`, `SupplierID`, `RFQDate`, `QuoteStatus`, `QuotationNo`, `QuotationTotal`, `QuotationCurrency`

- One row per supplier per tender. A supplier invited and never answering still has a row.
- **`QuoteStatus`** is `Received`, `No Quote`, or `Pending`:
  - `Received` — a quote arrived and entered evaluation, including one that lost
  - `No Quote` — declined, withdrawn, lapsed, or the tender was cancelled
  - `Pending` — a draft still being written, or no bid at all
- **`QuotationTotal`** is computed from the bid's lines; the portal stores no grand total.
- **`QuotationCurrency`** is **not** in your sheet and is sent anyway. Bids here are quoted in SYP or USD and we
  hold no exchange rates, so a column of bare numbers in two currencies would sum into a figure that means
  nothing.
- Where a supplier withdrew a bid and submitted another, the row reports their live bid.

## 9. What we cannot answer yet

- **A separate test environment.** There is one deployment. If you need somewhere to point a loader while it is
  being built, that is an infrastructure decision for MOT IT.
- **A named technical contact** and notice of changes — to be named by the ministry.
- **Whether the portal is reachable from wherever Power BI runs** — to be arranged.

## 10. The machine-readable contract

The full OpenAPI description, including both routes, their parameters and every response shape, is in
`contracts/openapi-v1.baseline.json` in this repository and can be shared as a file.
