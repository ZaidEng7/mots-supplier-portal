# Remediation backlog — MOTS Supplier Portal

> **This file is an invention.** No document specifies it, its name, or its format. It exists because
> the previous remediation backlog — roughly 131 items, referred to as `T3-xx` throughout sixteen
> batches of work — **was never an artifact**. It lived in a conversation that has since been lost,
> and `git log --all -S "T3-01"` finds nothing in this repository's entire history. Every `T3-xx`
> number quoted since was a reference to a document nobody could open.
>
> It lives at the repository root and **not** under `docs/`, because `docs/` is the externally-owned
> specification and is read-only to this work.
>
> **Numbering restarts at `T-001`.** The old `T3-xx` numbers cannot be reconciled with anything, so
> reusing them would imply a correspondence that does not exist.

---

## How this was produced, and what that does and does not prove

The specification was walked against the code. Three passes:

1. **Identifier coverage.** Every `BRULE-*`, `FR-*` and `SCR-*` in the documents was extracted and
   matched against every occurrence in `src/`. **431 identifiers; 172 are cited somewhere in the
   source.**
2. **Mechanism checks.** An identifier that is *not* cited is **not** evidence of a gap — most rules
   are implemented without quoting their own number. Each uncited rule was therefore checked for the
   *mechanism* it describes, and only the ones whose mechanism is genuinely absent became entries.
3. **§12 response-body conformance.** Never previously swept. Earlier work closed §12 at the level of
   *routes*; `profileCompleteness` is how we learned that routes and bodies are different claims.

**Verdict vocabulary.** Every entry is one of:

| Verdict | Meaning |
|---|---|
| **Reproduced** | Confirmed by reading the code at a named file and line, or by a failing test |
| **Inferred** | The mechanism is absent from a targeted search; not individually confirmed |
| **Unconfirmed** | Could not be established either way — recorded as such rather than guessed |

**Only a reproduced entry should carry work.** An inferred one needs five minutes of reading first.

### What this sweep does not cover

Stated so the next reader knows the shape of the hole rather than assuming completeness:

- **`FR-*` and `SCR-*` were not individually verified.** 172 functional requirements and ~40 screens
  is a sweep of its own. The identifier-coverage pass ran over them, but converting that into
  per-item verdicts would have meant inferring at a scale that makes the output untrustworthy.
  **This is the largest known gap in this file.**
- **§12.4 (RFQ) and §12.5 (Proposal) response bodies** were not swept field by field. §12.2 and §12.3
  were, and both diverge, so the other two are worth the same treatment.
- Only the backend was swept for rules. Frontend conformance to `SCREEN-SPECIFICATIONS.md` was not
  re-checked here.

---

## Entries

### Response-body conformance — §12.2 supplier profile

The documented body and the emitted body differ in nine places. All **reproduced** against
`src/backend/Application/Suppliers/GetSupplierContracts.cs:43-90`.

| Id | Source | Gap | Confirmed at | Verdict | Size |
|---|---|---|---|---|---|
| ~~T-001~~ | §12.2 | **Closed.** The entry was partly WRONG and is corrected here: something did compute a completeness — the supplier dashboard, as documents-supplied ÷ documents-total — and the SPA read it rather than computing its own. What was missing was the field on the §12.2 profile response, and a definition matching `T-03.1.1b` ("required sections + mandatory doc types"). Both endpoints now use one evaluator, which is the submit gate's own checklist | batch 3 | Reproduced | M |
| T-002 | §12.2 | `documentsSummary { required, approved, pending, rejected }` documented; absent | `GetSupplierContracts.cs:43`; absent | Reproduced | S |
| T-003 | §12.2 | `updatedAt` documented; absent from the DTO | `GetSupplierContracts.cs:43`; absent | Reproduced | S |
| T-004 | §12.2 | `externalId` and `syncStatus` documented on the profile; absent | `GetSupplierContracts.cs:43`; absent | Reproduced | S |
| T-005 | §12.2 | `supplierCode` documented; emitted as `referenceCode` | `GetSupplierContracts.cs:44` | Reproduced | S |
| T-006 | §12.2 | `defaultCurrency` documented; emitted as `currencyCode` | `GetSupplierContracts.cs:55` | Reproduced | S |
| T-007 | §12.2 | `categories` documented; emitted as `categoryCodes` | `GetSupplierContracts.cs:63` | Reproduced | S |
| T-008 | §12.2 | `legalName` documented; no such field (only `displayNameAr`/`En`) | `GetSupplierContracts.cs:45` | Reproduced | S |
| T-009 | §12.2 | `displayName` is one field in the document, two in the code | `GetSupplierContracts.cs:45-46` | Reproduced | S |

**T-005 through T-009 are a single decision, not five tasks.** Either the document's names are
authoritative and the DTO is renamed — a breaking change to a live SPA — or the code's bilingual
split is the correction and the document is stale. That is a decision, not a defect; see the section
at the end.

### Response-body conformance — §12.3 documents

| Id | Source | Gap | Confirmed at | Verdict | Size |
|---|---|---|---|---|---|
| T-010 | §12.3 | Document ids are raw GUIDs; the document specifies `DOC-2026-013377` reference codes | `SupplierDocument.cs:11`; every route uses `{id:guid}` | Reproduced | L |
| T-011 | §12.3 | Upload returns `201 Created`; the document specifies `202 Accepted` (correct for an async scan pipeline) | `DocumentEndpoints.cs:119` | Reproduced | S |
| T-012 | §12.3 | `Location` is `/api/v1/documents/{guid}`; documented as `/suppliers/{code}/documents/{docId}` | `DocumentEndpoints.cs:119` | Reproduced | S |
| T-013 | §12.3 | Download is `GET /documents/{id}/download-url` returning JSON; documented as `/documents/{id}/content` returning `302` | `DocumentEndpoints.cs:147` | Reproduced | M |
| T-014 | §12.3 | Oversize upload has no `413`; documented explicitly | No `Status413` anywhere in `src/backend` | Reproduced | S |
| T-015 | §12.3 | Upload response has no `scanStatus` field; scan state is folded into `state` | `DocumentContracts.cs` | Reproduced | S |

### Business rules with no implementing mechanism

All **inferred** unless noted: the mechanism was searched for by name across `Domain`,
`Infrastructure` and `Api` and not found. Each needs confirmation before it carries work.

| Id | Source | Gap | Verdict | Size |
|---|---|---|---|---|
| T-016 | BRULE-003 | Invite-only registration mode is not switchable; self-registration is hard-coded open | Inferred | M |
| T-017 | BRULE-012 | Re-application policy after rejection (allowed / cooldown) is not configurable | Inferred | M |
| T-018 | BRULE-035 | Deadline extension/shortening is not implemented; no endpoint, no notification | Inferred | L |
| T-019 | BRULE-050 | Whether commercial (price) revisions are permitted during clarification is not configurable | Inferred | M |
| T-020 | BRULE-054 | Default proposal currency is not configurable | Inferred | S |
| T-021 | BRULE-061 | Criteria requiring justification can be submitted without a comment | Inferred | S |
| T-022 | BRULE-069 | Ranking tie-breaks have no defined order | Inferred | M |
| T-023 | BRULE-074 | Approver authority limits and escalation are not implemented | Inferred | L |
| T-024 | BRULE-080 | Split/multi-line award policy is not implemented | Inferred | L |

### File handling

| Id | Source | Gap | Confirmed at | Verdict | Size |
|---|---|---|---|---|---|
| T-025 | BRULE-019, OQ-014 | RFQ and proposal attachments bypass quarantine-and-scan. **Blocked on OQ-014**, not on the scanner: `ClamAvScanner` is real and wired, but quarantine-first is a state machine and neither aggregate has any state. The asymmetry is now recorded at both upload sites rather than silent | `RfqEndpoints.cs`, `ProposalEndpoints.cs` — direct `IFileStorage.SaveAsync` | Reproduced | L |
| ~~T-026~~ | — | **Closed** — storage keys now derive from server-side values only; the file name is kept as row metadata. Also closed a second vector on the same line: the route's reference code was interpolated before validation | PR for batch 2 | Reproduced | S |
| ~~T-027~~ | §4.1 | **Closed** — RFC 6266 `filename` + `filename*`, so Arabic names survive. An ASCII-only escape would have been a regression | PR for batch 2 | Reproduced | S |
| T-028 | — | Proposal attachments have no download path at all | No route exists | Reproduced | M |

### Concurrency and audit

| Id | Source | Gap | Confirmed at | Verdict | Size |
|---|---|---|---|---|---|
| T-029 | §8.1 | Eight mutable aggregates carry no version column. **`Offering` closed in batch 3** — it was the one that bit, since every `supplier_user` edits the catalogue. Remaining: `Organization`, `OrgUnit`, `SupplierOrgLink`, `SupplierFieldConfig`, `SupplierDocument`, `Clarification`, `Addendum`. **No migration needed** — `xmin` is a Postgres system column, so each is a mapping change | Not `IVersionedAggregate` | Reproduced | M |
| T-030 | §8.1 | A child write does not bump the root's version. **Now reproduced**, and it does NOT let the excluded sub-resource routes back under `If-Match`: `ApplyExpectedVersion` only stamps an entry that is `Modified` **and** an `IVersionedAggregate`, and a child insert marks the CHILD `Added` — so the guard is skipped even when a correct `If-Match` was sent, and Postgres never advances the parent's `xmin` because the parent row is not written. Forcing a parent touch is not viable: `xmin` is database-generated and cannot be assigned, and a second UPDATE against the same row and token is the failure `AppDbContext.cs:192` already documents. A real fix needs an application-managed version column — **a second concurrency mechanism, so a decision** | `AppDbContext.cs:86-92` | Reproduced | M |
| T-031 | — | No `state_changed_at`; time-in-current-state is not derivable from the aggregate. Cycle time is derivable from the audit log instead (EPIC-19) | `Rfq.cs:42-66` | Reproduced | M |
| T-032 | §7 | `Correlation-Id` appears in problem bodies but is not echoed as a response header | `ProblemResponse.cs` | Reproduced | S |

### Platform and delivery

| Id | Source | Gap | Verdict | Size |
|---|---|---|---|---|
| T-033 | — | OpenAPI generation and a CI contract gate do not exist | Inferred | M |
| T-034 | — | `Category` and `DocumentType` have no write endpoints; both are seed-only | Inferred | M |
| T-035 | — | The ERPNext adapter is a stub; no real integration | Inferred | XL |
| T-036 | — | Notification preferences and reminder scheduling do not exist | Reproduced (no entity) | L |
| T-037 | — | The notification bell has no Actionable/Informational split | Reproduced (no classification) | S |
| T-038 | FEAT-17.5 | Consolidated deadlines view not built | Inferred | M |
| T-039 | FEAT-16.3 | Awards widget not built | Inferred | M |

### Accessibility and test health

| Id | Source | Gap | Confirmed at | Verdict | Size |
|---|---|---|---|---|---|
| T-040 | ACCESSIBILITY.md | The back-office shell header does not wrap at 320px — 424px against a 320px viewport. Affects every back-office screen | Measured in PR #102 | Reproduced | M |
| T-041 | — | `CrossOrganizationScopeTests` gives a full RFQ setup a 3-second window before `closesAt`; fails under CI load | Observed failing then passing on re-run, PR #101 | Reproduced | S |

### Closed — kept rather than dropped

A list that quietly loses entries is how the previous backlog became unusable.

| Id | Item | Verdict | Closed by |
|---|---|---|---|
| T-042 | Supplier dashboard header not reading the company name | **Closed** | PR #100 — `SupplierDashboardPage.tsx:97` renders it from the dashboard response |
| T-043 | "No document download in either direction" | **Was never true** for supplier documents — scoped, quarantine-gated, audited, wired into both screens | Pre-existing; hardened earlier |
| T-044 | RFQ attachment download missing | **Closed** | PR #103 |
| T-045 | Withdrawal permanently locks a supplier out | **Closed** | PR #103 — BRULE-048 and §4.1 both permit re-entry |
| T-046 | Nullable-bound filters silently widening results | **Was never true** — minimal APIs throw on unparseable values; the request was always refused | Corrected in PR #101 |
| T-047 | `ILIKE` `%`/`_` not escaped in offering search | **Closed** | PR #103 |
| T-048 | Unqualified `ToString` formatting under ambient culture | **Closed** — swept; production C# is clean, the single instance was in a test | PR #102 |
| T-049 | `BackgroundJob.Enqueue` | **Unconfirmed** — the symbol does not exist anywhere in `src/`. What the original note referred to cannot be recovered. Recorded rather than dropped, so it is not silently lost | — |

---

## Ordering — what I would do first

By what hurts in a live tender, not by size or by document section.

1. **T-025 — unscanned attachments are now downloadable.** PR #103 turned a theoretical gap into a
   live one. Malware reaching a buyer's machine through a tender portal is the worst outcome on this
   list. **Blocked on OQ-014** — it needs an answer more than it needs an engineer.
2. ~~T-027 — header injection in the download filename.~~ **Closed in batch 2.**
3. ~~T-026 — filenames shaping storage keys.~~ **Closed in batch 2.**
4. **T-028 — proposal attachments cannot be downloaded.** A buyer cannot open a bid document. Blocked
   on a decision, not on work.
5. **T-001 — `profileCompleteness`.** Documented, visible in the contract, computed by nothing.
   Documented-but-absent is worse than absent, because integrators build against it.
6. **T-010 — document reference codes.** Every other aggregate has one; documents leak GUIDs into
   URLs, which BRULE-040 forbids for RFQs and the same reasoning covers.
7. **T-021 — justification comments.** A one-line guard on a rule about defensible decisions, which
   is exactly what a tender challenge examines.
8. **T-029/T-030 — version columns and child-write propagation.** Silent lost updates on concurrent
   edits; invisible until two people edit one RFQ.
9. **T-018 — deadline extension.** Suppliers ask for it, the rule allows it, and there is no path.
10. **T-040 — the 320px shell header.** Every back-office screen, but it is chrome rather than
    correctness, and it needs a person's eye on the visual result.

## Needs a decision, not a batch

These belong to a person. Building any of them from a silent document would mean inventing policy.

| Question | Why it cannot be decided here |
|---|---|
| **Two-envelope discriminator on proposal attachments** | `ProposalDocument` has no technical/commercial field. Gating everything blinds evaluators; opening everything leaks commercial content. Only procurement can say which attachments are which. Blocks T-028 |
| **AV scanning scope (OQ-014)** | Tagged `[REQUIRES BUSINESS CONFIRMATION]`. Whether attachments must be scanned, and by what, is not ours. Blocks T-025 |
| **Signed URL vs streamed download** | §4.2 mandates signed URLs; the consequence is that the application sees neither the fetch nor the fetcher, and cannot revoke one. Acceptable or not is a policy call |
| **Which roles hold `report.read`** | Reports are built and reachable by nobody |
| **§12.2 field names** | T-005..T-009. Rename the DTO (breaking a live SPA) or accept that the document is stale. Someone owns that contract |
| **Bilingual fields vs the documented single-value shape** | The document shows `displayName`, `legalName`; the product is Arabic-first bilingual and the code split them. The code is probably right and the document probably predates the decision — but "probably" is not a contract |
| **Re-application policy after rejection (BRULE-012)** | "configurable" without a default is not implementable |
| **Ranking tie-break order (BRULE-069)** | The document gives an example, not a rule. Tie-breaks decide who wins a tender |
| **Approver authority limits (BRULE-074)** | The limits themselves are a finance policy nobody has stated |
| **Clarification visibility · review SLA duration · RFQ ownership and named approvers** | Already known to be open |
