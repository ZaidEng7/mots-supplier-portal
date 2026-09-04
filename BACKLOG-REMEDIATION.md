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

**Separable from the §12.2 field-name decision — assessed batch 4.** The §12.2 block is one question:
are the document's field NAMES authoritative, or is the code's bilingual shape right and the document
stale. Five of these six do not touch that question at all: T-011 is a status code, T-013 a route and
mechanism, T-014 an absent error response, T-015 an additive field, and T-010 is a VALUE-format
change that §3 settles independently (internal GUIDs must not appear in URLs). Only the narrow
`documentId` vs `id` spelling rides with §12.2, and it rides with T-010's value change anyway.

So these can be conformed without pre-judging §12.2. **Not conformed this batch** — T-010 is a batch
of its own and T-013 changes a route the SPA calls.

| Id | Source | Gap | Confirmed at | Verdict | Size |
|---|---|---|---|---|---|
| ~~T-010~~ | §12.3, §3 | **Closed.** Documents carry `DOC-YYYY-NNNNNN` and are addressed by it; the internal GUID is gone from URLs *and* payloads. The entry cited §12.3's shape, but the governing rule is **§3 principle 3** — GUIDs never in "URLs, payloads, or errors" — which a comment in `DocumentContracts` had read as paths-only, so the Guid was being emitted in bodies deliberately. Backfilled in-migration with the counter seeded past it; SPA needed **no change** | batch 5 | Reproduced | L |
| T-011 | §12.3 | Upload returns `201 Created`; the document specifies `202 Accepted` (correct for an async scan pipeline) | `DocumentEndpoints.cs:119` | Reproduced | S |
| T-012 | §12.3 | `Location` now emits `/api/v1/documents/DOC-…` — the GUID half is closed by T-010. The remaining divergence is only the PATH SHAPE: documented as `/suppliers/{code}/documents/{docId}` | `DocumentEndpoints.cs:119` | Reproduced | S |
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
| T-018 | BRULE-035 | Deadline extension/shortening not implemented. **Now reproduced, and the documents settle more than assumed**: extension is `procurement_officer` while Published/SubmissionOpen, shortening is `procurement_manager`, the audit event is named (`rfq.deadline_extended`), and "notify all invitees" IS specified — so the notification consequence is not an open question. What is open: **no bound on how far a deadline may be extended**, and the NotificationCatalogue has no deadline-change type, so building it needs new bilingual copy — the same reviewer dependency as the three Arabic sets already waiting. Whole rule is `[ASSUMPTION]` | `BUSINESS-PROCESSES.md:242-244` | Reproduced | L |
| T-019 | BRULE-050 | Whether commercial (price) revisions are permitted during clarification is not configurable | Inferred | M |
| T-020 | BRULE-054 | Default proposal currency is not configurable | Inferred | S |
| T-021 | BRULE-061 | Criteria requiring justification can be submitted without a comment. **Reproduced**: `Criterion` has no `RequiresJustification` field at all, so there is nothing to enforce against. The rule's own document tags **which** criteria require one as `[ASSUMPTION]` — buildable without inventing policy by putting the flag on the criterion and letting the template author set it, which is where the document points | `Criterion.cs:9-20` | Reproduced | S |
| T-022 | BRULE-069 | Ranking tie-breaks have no defined order | Inferred | M |
| T-023 | BRULE-074 | Approver authority limits and escalation are not implemented | Inferred | L |
| T-024 | BRULE-080 | Split/multi-line award policy is not implemented | Inferred | L |

### File handling

| Id | Source | Gap | Confirmed at | Verdict | Size |
|---|---|---|---|---|---|
| T-025 | BRULE-019, OQ-014 | RFQ and proposal attachments bypass quarantine-and-scan. **Closed in batch 8 under D-10 as a default, not an answer** — OQ-014 is still `[REQUIRES BUSINESS CONFIRMATION]`, so the decision lives in one enum comment and one default value rather than in the design. Both aggregates carry `ScanState`; the download gate scans on first access, deletes an infected object, and refuses with the same 404 as any other miss. Existing rows enter as `PendingScan`, not as clean | `AttachmentScanState.cs`, `AttachmentScanner.cs`, `GetRfqAttachmentDownloadUrlHandler.cs`, migration `20260904163101_AttachmentScanState` | Closed (batch 8) | L |
| ~~T-026~~ | — | **Closed** — storage keys now derive from server-side values only; the file name is kept as row metadata. Also closed a second vector on the same line: the route's reference code was interpolated before validation | PR for batch 2 | Reproduced | S |
| ~~T-027~~ | §4.1 | **Closed** — RFC 6266 `filename` + `filename*`, so Arabic names survive. An ASCII-only escape would have been a regression | PR for batch 2 | Reproduced | S |
| ~~T-028~~ | — | **Closed in batch 8 under D-7.** A supplier reads files on their own proposal ungated; a buyer reads another's only once that RFQ's evaluation reaches `Consolidated`/`Finalized` — the evaluation's state, not the proposal's, and the same predicate `ComparisonHandlers` already uses. The refusal is a 404 on the LIST as well as the download, because an attachment count is itself a signal about a live competitor bid. `ProposalDocument.Envelope` is stored now and applied by nothing yet; it exists so relaxing the gate for technical files later is a predicate change rather than a backfill nobody could perform after the fact | `ProposalDocumentDownloadHandlers.cs`, `ProposalDocumentEnvelope.cs`, migration `…_ProposalDocumentEnvelope` | Closed (batch 8) | M |

### Concurrency and audit

| Id | Source | Gap | Confirmed at | Verdict | Size |
|---|---|---|---|---|---|
| T-029 | §8.1 | Version columns. **`Offering` closed (batch 3). The remaining seven are NOT all gaps** — surveyed in batch 4 against their actual routes: `Organization`, `OrgUnit`, `SupplierOrgLink` and `Addendum` have **no update endpoint at all** (create/delete only), so there is no lost update to prevent. `SupplierDocument` was implemented and then **reverted**: its state machine already refuses any second decision with a 409, so a version can never be the thing that refuses a caller — the guard added nothing and made the endpoint harder to use. `Clarification` (answer/publish) is a child of the already-versioned `Rfq`, so it is **T-030**, not this. That leaves **`SupplierFieldConfig`** (`PUT /{category}/{fieldCode}`) as the only genuine remaining candidate, and it needs a single-item read added first | surveyed `Api/Endpoints/*.cs`; `SupplierDocument.cs:137-152` | Reproduced | S |
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
| ~~T-050~~ | — | **Closed.** The gate now separates its two failure modes: exit 1 is an advisory and names the package, exit 2 is a transport failure and says it is not a finding. A canary fixture pinned to a known critical CVE runs first and must fail, so the gate cannot silently stop checking — which it *had* done on the first attempt, because npm prints its error object as JSON and that parsed as a clean report. Root cause is the registry, not the endpoint: the **bulk** endpoint times out too, reproduced locally | batch 4 | Reproduced | S |

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

1. ~~T-025 — unscanned attachments are now downloadable.~~ **Closed in batch 8.** It was ranked first
   because malware reaching a buyer's machine through a tender portal is the worst outcome on this
   list; it was held on OQ-014 because nobody here can say what the scanning policy is. Batch 8 broke
   that deadlock by separating the two: the policy stays open, and the default is fail-closed. A
   business answer now changes a value, not a design.
2. ~~T-027 — header injection in the download filename.~~ **Closed in batch 2.**
3. ~~T-026 — filenames shaping storage keys.~~ **Closed in batch 2.**
4. ~~T-028 — proposal attachments cannot be downloaded.~~ **Closed in batch 8.** The framing was
   narrower than the fact: it was not that a download route was missing, it was that *no buyer-side
   read of a proposal existed at all* — see T-067, which is what remains once T-028 is closed.
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
| **Two-envelope discriminator on proposal attachments** | Answered as D-7 and shipped in batch 8: the uploading supplier declares it, unstated means Commercial, and the buyer gate refuses both kinds until consolidation. What is still procurement's call is whether technical files should open EARLY, during scoring — that is T-067, and it is the half that actually blinds evaluators |
| **AV scanning scope (OQ-014)** | Still tagged `[REQUIRES BUSINESS CONFIRMATION]`. Whether attachments must be scanned, and by what, is not ours. No longer *blocks* T-025 — batch 8 shipped fail-closed as a marked default. An answer of "scan nothing" or "scan on upload only" would change the default and delete the on-access path |
| **Signed URL vs streamed download** | §4.2 mandates signed URLs; the consequence is that the application sees neither the fetch nor the fetcher, and cannot revoke one. Acceptable or not is a policy call |
| **Which roles hold `report.read`** | Reports are built and reachable by nobody |
| **§12.2 field names** | T-005..T-009. Rename the DTO (breaking a live SPA) or accept that the document is stale. Someone owns that contract |
| **Bilingual fields vs the documented single-value shape** | The document shows `displayName`, `legalName`; the product is Arabic-first bilingual and the code split them. The code is probably right and the document probably predates the decision — but "probably" is not a contract |
| **Re-application policy after rejection (BRULE-012)** | "configurable" without a default is not implementable |
| **Ranking tie-break order (BRULE-069)** | The document gives an example, not a rule. Tie-breaks decide who wins a tender |
| **Approver authority limits (BRULE-074)** | The limits themselves are a finance policy nobody has stated |
| **Clarification visibility · review SLA duration · RFQ ownership and named approvers** | Already known to be open |

---

## Batch 6 sweep — `FR-*`, `SCR-*`, §12.4 and §12.5

The file's own largest stated hole, closed in part. **This sweep adds entries; it closes none.**

### Method, and the trap it is built to avoid

Batch 2's distinction carried forward: **uncited is not a gap.** 172 functional requirements, **94
cited** in source and **78 not**. Every one of the 78 got a mechanism check before any verdict, and
**67 of them turned out to be implemented** — anything else would have recorded 67 false gaps.

The reverse trap is checked too: before recording anything as undefined, the documents were searched
for a definition. That is how `profileCompleteness` turned out to be specified twice while everyone
treated it as an invention.

**A refinement this sweep had to make.** For state machines, "does the mechanism exist" is the wrong
question — a symbol can exist and be unreachable. `ProposalState.ClarificationRequested` and
`Shortlisted` both passed a presence check and are never assigned anywhere. The check for a state
machine is **reachability**, and applying it produced the largest finding here (T-051).

### Findings

| Id | Source | Gap | Confirmed at | Verdict | Size |
|---|---|---|---|---|---|
| ~~T-051~~ | FR-PRP-009/010/011, §4.1 | **Closed for four of six states.** `UnderReview`, `ClarificationRequested`, `Revised` and `Shortlisted` are now reachable: intake at evaluation-open, the clarification loop as two endpoints, shortlisting at consolidation. `AwardOffered` and `Declined` remain unreachable — they are the award-offer sub-chain, fully documented in §4.1 but a change to EPIC-14's award flow rather than to evaluation intake, and recorded as **T-064** rather than half-built | batch 7 | Reproduced | XL |
| T-064 | §4.1 | `ProposalState.AwardOffered` and `Declined` are still unreachable. §4.1 defines both fully (`Shortlisted → AwardOffered` on `award.approve`, `AwardOffered → Declined` on `proposal.decline` within an acceptance window). Today `Award()` goes straight to `Awarded`, so building the offer step changes EPIC-14's award flow — scoped out of batch 7 deliberately, not overlooked. The acceptance window's length is undecided | `Proposal.cs` Award(); no assignment of either state | Reproduced | L |
| ~~T-065~~ | §3 | **Closed.** Proposal transition refusals answer 409 with `invalid-state-transition`, `currentState` and `allowedNext`, via the same result type RFQ uses — generalised, not duplicated. Refusals that are shaped like validation keep their 400: §3 governs transitions, not every rejection | batch 8 | Reproduced | M |
| T-066 | §12.5 | Submit's completeness refusals (unpriced required item, unanswered mandatory requirement) answer **400**, where §12.5 names **422 `PROPOSAL_ITEMS_REQUIRED`**. Found while separating state refusals from completeness refusals for T-065; deliberately left as 400 there rather than swept into 409, which would have told a supplier with an unpriced item that their proposal had moved on | `SubmitProposalHandler`; `Proposal.Submit` | Reproduced | S |
| T-067 | FEAT-11.3, OQ-009 | **An evaluator scores bids they cannot read.** `MyEvaluationDto` hands an evaluator a list of proposal GUIDs, the criteria, and their own scores — and no bid content of any kind: no narrative, no requirement answers, no documents. Found while closing T-028, and larger than it: T-028's buyer gate opens at consolidation, which is *after* scoring, so nothing in batch 8 gives an evaluator anything to score against. The fix needs a decision, not just work — which technical content opens during scoring, and whether it opens per-evaluator or per-evaluation | `EvaluationContracts.cs` MyEvaluationDto; no handler produces proposal content for an evaluator | Reproduced | L |
| T-068 | §3 | Proposal **GUIDs** are exposed in evaluation payloads — `ConsolidatedResultDto.ProposalId`, `MyEvaluationDto.ProposalIds`, and the scoring command's `proposalId`. §3 principle 3 says internal identifiers never appear in "URLs, payloads, or errors", and proposals have carried a public `referenceCode` since before this. Batch 8's buyer document routes are keyed by the same GUID deliberately — inventing a second addressing scheme for two routes would have made this harder to fix, not easier. Same shape as T-010, which is now closed and is the template | `EvaluationContracts.cs`, `EvaluationEndpoints.cs`, `ComparisonContracts.cs` | Reproduced | M |
| ~~T-052~~ | BRULE-024 | **Closed in batch 8.** `DocumentState.UnderReview` was read by three guards and assigned by nothing, so §12.3's own reviewer query — `?state=UnderReview,Rejected` — matched nothing that had ever existed. §4.4 already named the culprit: *"an async Hangfire job runs virus scan + validation, transitioning to `UnderReview`"* — `DocumentScanJob` stopped at `Uploaded`. Added `EnterReview()` and the job's second call. Kept as a separate transition rather than folded into `MarkScanClean`, so a crash between them leaves a reviewable `Uploaded` row instead of trading this gap for its mirror image. BRULE-024's re-upload path needed no change once the pipeline did: a new version is created `PendingScan` and traverses the same route. Every consumer that filters on `DocumentState` was enumerated first — none filter on `Uploaded`, so nothing emptied. SPA already rendered the state in both locales | `SupplierDocument.cs`, `DocumentScanJob.cs` | Closed (batch 8) | M |
| T-053 | §13, §12.5 | **`Idempotency-Key` is not implemented anywhere.** §13's checklist requires it on unsafe POSTs and §12.5 makes it *required* on submit, with a documented replay response. Harm is bounded — a second submit hits the state guard and 409s rather than duplicating — so this is a contract gap, not a double-submission bug | no occurrence in `Api`/`Infrastructure` | Reproduced | L |
| ~~T-054~~ | §12.4 | **Closed.** `submissionClosesAt` is on the supplier RFQ list. Named for the aggregate rather than §12.4's `submissionDeadline` — the rename to §12.2's vocabulary is R-9's coordinated pass, and doing one field early would make the SPA read two conventions at once | batch 7 | Reproduced | S |
| T-055 | §12.4 | `buyingOrg.code` (`ORG-HTL-0007`) is documented; `Organization` has no public short code, so `ExternalId` or null is emitted. Same class T-010 just closed for documents | `RfqContracts.cs:90-96` | Reproduced | M |
| T-056 | §12.5 | `createdAt` documented on the create response; absent from `ProposalDto` | `ProposalContracts.cs:21-31` | Reproduced | S |
| T-057 | §12.5 | The submit response's `totals { currency, grandTotal }` object does not exist on any proposal DTO | `ProposalContracts.cs` — no totals member | Reproduced | M |
| T-058 | §12.5 | `ProposalDto` carries BOTH `ReferenceCode` and `ProposalReferenceCode`. One of them is redundant and no document asks for two | `ProposalContracts.cs:22` | Reproduced | S |
| T-059 | FR-ADM-004 | No write endpoints for `Category`, `DocumentType`, `Currency`, `UnitOfMeasure`, `Incoterm`, `Region` — all seed-only. (Supersedes the older T-034, which named only two of the six) | no `MapPost`/`MapPut` on reference data | Reproduced | L |
| T-060 | FR-ADM-006 | System settings (registration mode, default currency, numeral system, expiry windows, approval hierarchy) are not configurable. Marked `[ASSUMPTION]` in its own requirement | no settings entity | Reproduced | L |
| T-061 | FR-ADM-007 | Notification templates are a compiled catalogue, not admin-editable AR/EN templates | `NotificationCatalogue.jsonc` | Reproduced | L |
| T-062 | FR-DSH-006 | No admin dashboard (users/roles, reference-data health, integration status, job health) | no route, no handler | Reproduced | L |
| T-063 | FR-INT-008 | No inbound ERP path through an ACL. Marked `[ASSUMPTION]` in its own requirement | no inbound handler | Reproduced | L |

### `FR-*` — verdict spread

| Verdict | Count |
|---|---|
| Built (mechanism reproduced) | 152 |
| Missing / stubbed | 11 |
| Deferred with a recorded rationale in code | 1 — FR-RFQ-013, ERP mapping fields left off `Rfq` deliberately rather than as dead scaffolding |
| Ambiguous in the requirement's own wording | 8 |

**The eight ambiguous ones are documentation findings, not code verdicts** — "built" and "partial"
both defend, because the requirement does not say how much is enough: FR-ADM-006, FR-INT-008,
FR-NOT-004, FR-NOT-007, FR-PROF-009, FR-REG-002, FR-SRCH-006, FR-DOC-004. Every one carries
`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` in its own text, which is the documents telling us
they are unfinished rather than us failing to read them.

### `SCR-*` — route level only, and that limit is the point

`SCREEN-INVENTORY.md` holds **128 screen rows with routes**. The router declares **41 paths**.

That gap is real but it **overstates** what is missing, and the overstatement is why this section is
not a per-screen verdict table. Many documented screens are sections of one page (`/onboarding/company`,
`/onboarding/documents` — the onboarding page renders these as sections), or states rather than routes
(`/404`, `/error`, `/maintenance`, `/account/locked`), or differ only in spelling
(`/password/forgot` against the router's `/forgot-password`).

**What moved since the original sweep's "0% fully built across Epics 7–14":** that is no longer true.
SCR-100, 101, 105, 106, 120, 140, 151, 160, 300, 301, 400, 401, 432, 500, 600, 700 and 900 are cited
in source and have routes — seventeen screens across the epics that sweep called empty.

**Not established here:** whether each built screen matches its specification's layout and required
states, and whether those states are *tested* rather than merely present. That is the distinction that
found EPIC-16's per-widget error isolation gap, and it is the one thing this section does not deliver.
See the holes below.

### §12.4 / §12.5 — riding with R-9, or independent

R-9 rules that §12.2's names are authoritative and the DTOs rename to match.

| Divergence | Rides with R-9? |
|---|---|
| `rfqCode` → `referenceCode` | **Rides** |
| `title` → `titleAr`/`titleEn` | **Rides** — same bilingual-split question as `displayName` |
| `invitationStatus` → `myInvitationStatus` | **Rides** |
| `proposalCode` → `referenceCode` | **Rides** |
| `currency` → `currencyCode` | **Rides** |
| T-054 `submissionDeadline` absent | **Independent** — a missing field |
| T-055 `buyingOrg.code` unproducible | **Independent** — needs an `ORG-` scheme |
| T-056 `createdAt` absent | **Independent** |
| T-057 `totals` object absent | **Independent** |
| T-058 duplicate reference-code fields | **Independent** |
| `validityDays` vs `validityStart`/`End` | **Independent** — shape, not name |
| `technicalResponse` string vs `requirementAnswers[]` | **Independent**, and already justified in `RequirementAnswer.cs` as a deliberate deviation |

**Five rides with R-9, seven are independent** — the same split §12.3 showed, and the seven can be
conformed without waiting on that decision.

### What this sweep says about itself

**Counts.** 63 entries total, **13 added** by this sweep. Of the 13: **13 reproduced, 0 inferred** —
every one is backed by a file and line, or by a reachability sweep over production code. Across the
whole file the balance is now roughly 37 reproduced to 15 inferred, with 1 unconfirmed
(`BackgroundJob.Enqueue`) and 11 closed.

**Ordering of everything outstanding, by what hurts in a live tender.**

1. **T-051 — the proposal lifecycle's middle is unreachable.** A buyer cannot request clarification on
   a proposal, and nothing is ever shortlisted. This is the largest functional hole in the product and
   it sits in the tender's critical path.
2. ~~T-025 — unscanned attachments are downloadable.~~ **Closed in batch 8** under a marked default.
3. ~~T-028 — proposal attachments cannot be downloaded.~~ **Closed in batch 8.**
4. ~~T-052 — documents never enter UnderReview.~~ **Closed in batch 8.** The visible consequence was
   narrower and worse than the entry said: the documented reviewer queue returned nothing, always.
5. **T-053 — no `Idempotency-Key`.** Bounded by state guards today, but §13 requires it and the next
   endpoint without a state guard is where it bites.
6. **T-054 — the deadline is missing from the supplier RFQ list.** One field, directly in a bidder's face.
7. **T-059 — reference data is seed-only.** A ministry cannot add a document type without a deploy.
8. **R-9's DTO rename plus the seven independent §12.4/§12.5 divergences** — one coordinated pass.
9. **T-029's `SupplierFieldConfig`** and the §12.3 five — small, known, ready.
10. **T-062 / T-060 / T-061 / T-063** — whole admin surfaces, large and not tender-blocking.

**The remaining holes, named as plainly as batch 2 named its own.**

- **`SCR-*` per-screen verification is NOT done.** This sweep established which routes exist. It did
  not check any screen against its specification's layout, its required states, or whether those
  states are tested. That is the largest hole in this file and it is now the *only* one of batch 2's
  three that remains open.
- **`BRULE-*` was not re-swept.** Batch 2 covered it; nothing since has re-verified those 100 rules
  against five epics of subsequent change.
- **The frontend was not swept for anything.** No component, i18n, or accessibility conformance check
  has ever run item by item.
- **§12.1 (Auth) response bodies** were never swept. §12.2 through §12.5 now have been. That is the
  last unswept section of §12.
