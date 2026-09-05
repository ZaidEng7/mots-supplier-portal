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
| ~~T-005~~ | §12.2 | **Closed (R-9, batch 8).** Emits `supplierCode` | batch 8 | Reproduced | S |
| ~~T-006~~ | §12.2 | **Closed (R-9, batch 8).** The RESPONSE emits `defaultCurrency`. The PATCH request key and `ProfileFieldCodes.CurrencyCode` are deliberately unchanged: that string is a persisted field-code the reviewer flagged-field guard (BRULE-050) matches against, so renaming it is a data migration, not a contract rename | batch 8 | Reproduced | S |
| ~~T-007~~ | §12.2 | **Closed (R-9, batch 8).** Emits `categories` | batch 8 | Reproduced | S |
| T-008 | §12.2 | **R-9 does not reach this one, and batch 8 says so rather than conforming it.** `legalName` exists — as `legalInfo.legalNameAr`/`legalNameEn`. §12.2 shows it as a single top-level value, and its own example puts Arabic in `legalName` and English in `displayName`. Conforming is not a rename: it collapses two stored values into one and drops a language from an Arabic-first product. R-9 rules that the document's NAMES are authoritative; it does not rule a bilingual pair into a single value | `GetSupplierContracts.cs` LegalInfoDto | Reproduced | S |
| T-009 | §12.2 | Same as T-008 and the same refusal: `displayNameAr`/`displayNameEn` stay split. Still the open decision the table at the end of this file records as "bilingual fields vs the documented single-value shape" — which R-9 did not answer | `GetSupplierContracts.cs` | Reproduced | S |

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
| ~~T-011~~ | §12.3 | **Closed (batch 8).** Upload answers `202 Accepted`. It is the honest code as well as the documented one: the row exists, `DocumentScanJob` has not finished with it | batch 8 | Reproduced | S |
| ~~T-012~~ | §12.3 | **Closed (batch 9), the other way round.** Batch 8 left the `Location` non-conforming because neither path had a GET, and emitting a header that resolves to nothing is worse than one under a different shape. The answer was to make the documented path real: `GET /suppliers/{supplierCode}/documents/{documentCode}` now exists, resolved THROUGH the supplier named in the path, serving the owner and a `document.review` reviewer on the same rule as the download. The test follows the header rather than merely reading it | `DocumentEndpoints.cs`, `GetSupplierDocumentHandler.cs` | Reproduced | S |
| ~~T-013~~ | §12.3 | **Closed (batch 8).** `GET /documents/{code}/content` answers `302` to the pre-signed URL and the list's `downloadUrl` points at it. Added ALONGSIDE `download-url`, not instead of it: the SPA reads JSON, and `fetch()` cannot hand a cross-origin redirect back to application code. One handler, so the two routes cannot authorize differently | batch 8 | Reproduced | M |
| ~~T-014~~ | §12.3 | **Closed (batch 8).** Oversize answers `413` and a disallowed MIME `415`, both as §12.3 names them. They had shared one 400, which says the request was malformed rather than that the file was wrong | batch 8 | Reproduced | S |
| ~~T-015~~ | §12.3 | **Closed (batch 8).** `scanStatus` is emitted, derived from `DocumentState` rather than stored — the same treatment `expiryState` already had, and for the same reason: a second stored copy of a fact the state carries is a second thing to keep in step | batch 8 | Reproduced | S |

### Business rules with no implementing mechanism

All **inferred** unless noted: the mechanism was searched for by name across `Domain`,
`Infrastructure` and `Api` and not found. Each needs confirmation before it carries work.

| Id | Source | Gap | Verdict | Size |
|---|---|---|---|---|
| T-016 | BRULE-003 | Invite-only registration mode is not switchable; self-registration is hard-coded open | Inferred | M |
| T-017 | BRULE-012 | Re-application policy after rejection (allowed / cooldown) is not configurable | Inferred | M |
| ~~T-018~~ | BRULE-035 | **Closed (batch 9).** One `POST /rfqs/{code}/deadline` for both directions. **No permission filter on the route, and that was the correction**: extension is the officer's `rfq.edit`, shortening the manager's new `rfq.deadline.shorten` (D-23), and `procurement_manager` does not hold `rfq.edit` — a route requiring it 403'd the exact caller BRULE-035 names for shortening. Both checks are in the handler, the only place the direction is known. Unbounded per D-12; the audit row carries BOTH dates, because "extended" without from/to says nothing about by how much. Shortening gets its own audit action and its own notification (D-24) — a window closing earlier is what a bidder must hear about most urgently. Two coherence guards that are not policy: the new date must be in the future (a past date would close the RFQ via the timeline job, skipping `Close()`) and after the window opens. Recorded consequence: with no separate `ClarificationDeadlineAt`, extending the submission deadline also reopens clarifications — the documented fallback behaving as designed | `Rfq.ChangeSubmissionDeadline`, `RfqHandlers.cs`, `RfqEndpoints.cs`, `RfqDetailPage.tsx` | Reproduced | L |
| T-019 | BRULE-050 | Whether commercial (price) revisions are permitted during clarification is not configurable | Inferred | M |
| T-020 | BRULE-054 | Default proposal currency is not configurable | Inferred | S |
| ~~T-021~~ | BRULE-061 | **Closed (batch 8).** `RequiresJustification` sits on `Criterion`, set by the template author — the rule tags WHICH criteria need one as `[ASSUMPTION]`, so deciding it in code (say, every Commercial criterion) would have invented the policy the rule declines to state, and invisibly. Snapshotted onto `EvaluationCriterionSnapshot` with the weights, so the rule an RFQ bound is the rule it keeps. **Either language satisfies it**, not both: an evaluator writes their reasoning in the language they think in, and this comment is internal procurement evidence rather than supplier-facing product copy. Emitted on the evaluator's own view so the form can mark the field before the score is refused | `Criterion.cs`, `Evaluation.ScoreCriterion` | Closed (batch 8) | S |
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
| ~~T-029~~ | §8.1 | **Closed (batch 8).** `Offering` closed in batch 3; batch 4's survey ruled out five of the remaining seven (four have no update endpoint, `SupplierDocument`'s state machine already refuses a second decision, `Clarification` is T-030). The last one, `SupplierFieldConfig`, now carries `xmin` and its `PUT` requires `If-Match` — **and the single-item read was added in the same change**, because a precondition nobody can obtain refuses every caller. Two DTOs rather than a nullable version: the ETag filter matches `RowVersion` only when it is `uint` or `long`, so a `uint?` would have been silently ignored and the guard would have had no obtainable precondition — a failure invisible in a build | `SupplierFieldConfig.cs`, `AdminEndpoints.cs` | Closed (batch 8) | S |
| T-030 | §8.1 | **Split (1) of 4 CLOSED (batch 9 send 4). Splits (2)-(4) open.** All nine roots now carry an application-managed `RowVersion` (`bigint`, default 1) instead of Postgres `xmin`, advanced in `SaveChangesAsync` for every root the change set touches — directly or through a child, attributed by a one-hop foreign-key walk with `UnattributedChildTypes()` exposing what that cannot see. **The wire contract did not move**: still `uint`, still base64url in a strong ETag, so 34 ETag/If-Match sites and the whole SPA were untouched — which is what made split (1) landable alone. Red count **1 of 557**, and it was the change working: a third party's child write now invalidates a held ETag, so the test harness's ETag cache had to go (it was replaying versions a real client could not rely on). D-27 records that every in-flight ETag expires at cutover. **Still open: the 55 child-write routes that declare no `If-Match`** — `POST /me/contacts` moves the version now but still guards nothing. Splits (2) RFQ children + SPA, (3) supplier `/me/*` children + SPA, (4) the rest | `AppManagedVersion.cs`, `AppDbContext.cs`, migration `…_AppManagedRowVersion` | Reproduced | L (was XL) |
| T-031 | — | No `state_changed_at`; time-in-current-state is not derivable from the aggregate. Cycle time is derivable from the audit log instead (EPIC-19) | `Rfq.cs:42-66` | Reproduced | M |
| T-032 | §7 | `Correlation-Id` appears in problem bodies but is not echoed as a response header | `ProblemResponse.cs` | Reproduced | S |

### Platform and delivery

| Id | Source | Gap | Verdict | Size |
|---|---|---|---|---|
| T-033 | — | OpenAPI generation and a CI contract gate do not exist | Inferred | M |
| ~~T-034~~ | — | **Closed as part of T-059 (batch 9)** — confirmed to be the same work, as batch 6 predicted. Kept as a row so the id is not silently reused | batch 9 | Inferred | M |
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
| **§12.2 field names** | Answered as R-9 and applied in batch 8 for the three that are renames (T-005/006/007). T-008 and T-009 are NOT renames — see their rows — and remain open under the bilingual question below |
| **Bilingual fields vs the documented single-value shape** | Still open, and R-9 did not settle it: R-9 rules on names, and this is a shape. `displayName`, `legalName`, RFQ `title`, and §12.5's `technicalResponse` string all show one value where the code carries an Ar/En pair. Batch 8 conformed every name and left every pair, which is the only direction that loses nothing. Someone still owns the question |
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
| ~~T-064~~ | §4.1 | **Closed (batch 9).** The stop gate did NOT fire — both states have a documented entry and exit. `Shortlisted → AwardOffered` on **approve** (not recommend: a recommendation is not a decision, and telling a bidder they have won before the approver signs cannot be un-told); `AwardOffered → Awarded` on **execute**, matching §3.1's "Set winning proposal `AwardOffered→Awarded`"; `AwardOffered → Declined` on a new supplier route with a required reason, returning the RFQ to `Recommendation`. No acceptance window and no supplier accept — D-21. Blast radius found **two live traps before shipping**: the execute loser-query would have dropped the WINNER (now outside its predicate), and the comparison snapshot stored on the award would have omitted the winning bid, since the comparison filtered `InEvaluation` — hence `ProposalStates.UnderComparison` | `Proposal.cs`, `Rfq.cs`, `AwardHandlers.cs`, `ProposalHandlers.cs`, `ProposalState.cs` | Reproduced | L |
| ~~T-065~~ | §3 | **Closed.** Proposal transition refusals answer 409 with `invalid-state-transition`, `currentState` and `allowedNext`, via the same result type RFQ uses — generalised, not duplicated. Refusals that are shaped like validation keep their 400: §3 governs transitions, not every rejection | batch 8 | Reproduced | M |
| ~~T-066~~ | §12.5 | **Closed (batch 9).** Submit's completeness refusals answer **422** with a code — `PROPOSAL_ITEMS_REQUIRED` as §12.5 names it, plus two invented codes for the unanswered-requirement and missing-validity cases, marked as inventions where they are thrown. A new `ProposalIncompleteException` carries the identifier, which `ProblemDetailsMiddleware` already turns into §7's SCREAMING_SNAKE `code` — no second mapping table. Window and wrong-state refusals keep their 400/409, which is the distinction that matters: a 409 says the proposal moved on, a 422 says go and fill this in | `Proposal.cs`, `ProposalHandlers.cs`, `ProposalEndpoints.cs` | Reproduced | S |
| ~~T-067~~ | FEAT-11.3, OQ-009 | **Closed (batch 9). The entry UNDERSTATED it.** It recorded that `MyEvaluationDto` carried no bid content. Reproduced end to end, the surface was worse: an evaluator holds only `evaluation.score`, `evaluation.submit` and `rfq.clarify`, so `GET /rfqs/{code}` **403**, the comparison **403**, the proposal **403** and batch 8's buyer document list **403** — `my-evaluation` was their ONLY reachable window, and it carried neither the bid nor the SPECIFICATION the bid answers. The SPA printed a proposal GUID as the bid's identity. Now: the RFQ's title, description, items and requirements, plus each bid's supplier identity (D-19), narrative, requirement answers and Technical-envelope documents (D-7), with a download route gated on the assignment. Widened the READ rather than the role, so one already-assignment-scoped handler stays the only door. The seal is a SQL projection that never names a commercial column, asserted against raw JSON | `EvaluationContracts.cs`, `EvaluationHandlers.cs`, `ProposalDocumentDownloadHandlers.cs`, `MyEvaluationPage.tsx` | Reproduced | L |
| T-068 | §3 | **Evaluator surface closed (batch 9); the buyer surface remains.** `MyEvaluationDto` no longer emits `ProposalIds`, `TechnicallyQualifiedByProposal`, `MyScoreDto.ProposalId`, `Id` or `RfqId` — bids are keyed by `proposalCode` and the scoring route takes `proposalCode`. Still open: `ConsolidatedResultDto.ProposalId`, `ComparisonProposalDto`, and the award/RFQ-detail screens that read them. Deliberately not swept in the same pass — those are the buyer's path and moving them means moving `AwardPage` and `RfqDetailPage` too | `EvaluationContracts.cs`, `ComparisonContracts.cs` | Reproduced | M |
| ~~§12.1 sweep~~ | §12.1 | **Swept in batch 9 send 3 — §12 is now complete.** Five endpoints, field by field. **Conformed:** `tokenType` and `expiresIn` on login (additive, `accessTokenExpiresAt` kept — an absolute expiry does not ask a client to trust its own clock); verify-email's invalid token now **422 `VERIFICATION_TOKEN_INVALID`** where it answered 400 with a different slug; the register response's `referenceCode` → **`supplierCode`**, matching §12.2's R-9 spelling. **Already conforming:** logout's 204. **Deliberately NOT conformed, both with tests asserting the divergence so it cannot be silently "fixed":** the register response (D-25 — §12.1's 201 + `Location` + four fields + 409 on duplicate are each an account-enumeration oracle, against §1.6) and login's `user` object (D-26 — roles and permissions live in the token's claims, and a body copy is a second source of truth for authorization that goes stale on a role change). Also noted: locked-out answers **423 Locked** where §12.1 says 429 — kept, because 429 is rate-limiting and a client cannot distinguish the two if both share a status | `AuthEndpoints.cs`, `RegistrationEndpoints.cs` | Reproduced | S |
| T-072 | FR-ADM-004 | **FR-ADM-004 names SIX reference tables and only five exist.** `Incoterm` has no entity, no table and no seed — `Proposal.IncotermCode` is a free `varchar(10)` validated by nothing, so a supplier can submit any string as an Incoterm and the comparison matrix will print it. Found while building T-059's admin surface; the other five were added there and this one deliberately was not, because a code list nobody has supplied is not reference data. Needs the actual Incoterms set (2020 has eleven) from procurement, then it is the same shape as the other five plus a validation rule on the proposal terms | `Proposal.cs:67`, `AppDbContext.cs:860`; no entity in `Domain/ReferenceData` | Reproduced | M |
| ~~EPIC-18 (SCR-600)~~ | FR-DSH-005, BRULE-086/087 | **Closed (batch 9 send 4) for the P1 dashboard.** `ministry_viewer` held an **EMPTY** permission set — the persona could log in and reach nothing, the EPIC-11 defect at persona scale. Now `governance.read` (its own permission: `rfq.read` and `report.read` are row-scoped to an organization and this read deliberately is not), `GET /api/v1/ministry/overview` returning cross-organization aggregates with **no organization predicate anywhere**, and SCR-600 at `/ministry`. Every figure is a count or an average — nothing identifies a row, so there is no per-row filter a later edit could forget. D-6/BRULE-087's commercial visibility is ONE flag on the existing admin-editable config table, seeded OFF, defaulting closed if the row is missing; the response echoes it so the screen says WHY a figure is absent rather than rendering a blank. **Still open: SCR-601 registry, SCR-602 RFQ monitor (both P1), SCR-603/604/605 (P2)** | `GovernanceEndpoints.cs`, `GetGovernanceOverviewHandler.cs`, `MinistryOverviewPage.tsx` | Reproduced | L |
| T-073 | — | **`ManageRolesTests` overwrote `ministry_viewer`'s permissions and left them overwritten**, because that role was chosen as the safe subject precisely when its set was empty. EPIC-18 gave it `governance.read` and the governance suite then passed alone and failed in the full run. Fixed by restoring the role in that test. Recorded because the class of problem is general: any test that mutates a GLOBAL row — a role, a config flag, reference data — is an order dependence waiting for the row to start mattering. A sweep for other such tests has not been done | `ManageRolesTests.cs:188` | Reproduced | S |
| T-074 | — | **`storybook-axe` is flaky, and it has now cost a CI run.** A different story fails each time (Badge/Danger, Select/Preselected, Skeleton/List Light, Skeleton/Bar Dark across four runs) and every one passes in isolation and when that project runs alone. Not within-project parallelism: the project is already `fullyParallel: false`, `workers: 1`, `retries: 0` — the last set deliberately, because a previous `retries: 1` was masking an "Axe is already running" race rather than fixing it. The remaining suspect is contention with the OTHER Playwright projects, which run in parallel against the same machine while storybook-axe is served by a separate static server. **Not fixed, and deliberately not retried away** — adding retries here would re-mask exactly what the comment in `playwright.config.ts` says must not be masked. Needs someone to reproduce it under load and find the actual wait that is insufficient | `playwright.config.ts:38-48`, `tests/e2e/storybook-axe.spec.ts` | Reproduced | M |
| T-069 | §3, §4.1 | **`AwardApproval → Recommendation` was listed by `Rfq.AllowedNextFrom` and implemented by nothing** — the API advertised a transition it could not perform. Found while building T-064's decline path, which needed exactly that move, so `ReturnToRecommendation()` now exists and the decline uses it. **Still open: the award REJECT path does not use it either** and leaves the RFQ in `AwardApproval`, so a rejected award has no route back to choosing an alternate. Not fixed in batch 9 — reject is a separate flow with its own notifications | `Rfq.cs` AllowedNextFrom line 641; `RejectAwardHandler` | Reproduced | M |
| T-070 | FEAT-12.x | **The comparison view is empty once an award is executed.** `ComparisonHandlers` filters on the evaluation set, and execute moves every proposal to `Awarded`/`NotSelected` — both outside it. The award's own stored snapshot is fine (taken before the transition), but a buyer opening the comparison after an award sees nothing. Pre-existing, found while tracing T-064's snapshot trap; `ProposalStates.UnderComparison` narrows the gap but does not close it | `ComparisonHandlers.cs:45` | Reproduced | S |
| T-071 | FR-DSH-002 | **The supplier dashboard's "Award offers" KPI could never be non-zero** — it counts `ProposalState.AwardOffered`, which nothing assigned until T-064. Now reachable. Recorded because it is the second consequence of T-064 nobody had connected to it | `SupplierDashboardHandler.cs:82` | Reproduced | S |
| ~~T-052~~ | BRULE-024 | **Closed in batch 8.** `DocumentState.UnderReview` was read by three guards and assigned by nothing, so §12.3's own reviewer query — `?state=UnderReview,Rejected` — matched nothing that had ever existed. §4.4 already named the culprit: *"an async Hangfire job runs virus scan + validation, transitioning to `UnderReview`"* — `DocumentScanJob` stopped at `Uploaded`. Added `EnterReview()` and the job's second call. Kept as a separate transition rather than folded into `MarkScanClean`, so a crash between them leaves a reviewable `Uploaded` row instead of trading this gap for its mirror image. BRULE-024's re-upload path needed no change once the pipeline did: a new version is created `PendingScan` and traverses the same route. Every consumer that filters on `DocumentState` was enumerated first — none filter on `Uploaded`, so nothing emptied. SPA already rendered the state in both locales | `SupplierDocument.cs`, `DocumentScanJob.cs` | Closed (batch 8) | M |
| ~~T-053~~ | §13, §12.5, §8.2 | **Closed (batch 9 send 4)** for the three transitions §8.2 names as REQUIRED — `proposal.submit`, `award.approve`, `rfq.publish`. `idempotency_record` keyed `(UserId, Key)` with the unique index as the reservation, a SHA-256 fingerprint of method+path+body, verbatim replay with `Idempotency-Replayed: true`, `409 IDEMPOTENCY_KEY_REUSED` on a different fingerprint, `428 IDEMPOTENCY_KEY_REQUIRED` when absent, and an hourly Hangfire GC against the row's own stored expiry. The SPA sends a key per intent, generated OUTSIDE the 401-refresh retry — a key regenerated on the retry would be a second intent and would submit twice. D-29 records what is deliberately not atomic and why. **Still open:** §8.2 says every non-idempotent POST *accepts* the header; today only these three honour it, and the other 83 ignore one that is sent. Two shapes corrected while building — `text` not `jsonb` (jsonb normalises, so replay was not verbatim) and per-user keying (a shared key space would let one caller replay another's response) | `IdempotencyEndpoints.cs`, `IdempotencyRecord.cs`, `IdempotencyCleanupJob.cs` | Reproduced | L |
| ~~T-054~~ | §12.4 | **Closed.** `submissionClosesAt` is on the supplier RFQ list. Named for the aggregate rather than §12.4's `submissionDeadline` — the rename to §12.2's vocabulary is R-9's coordinated pass, and doing one field early would make the SPA read two conventions at once | batch 7 | Reproduced | S |
| T-055 | §12.4 | **Its own batch, and batch 9 says so rather than starting it.** `buyingOrg.code` needs an `ORG-` scheme on `Organization`: a counter, a format, a backfill migration with no unaddressable window, and an addressing decision — T-010 exactly, at T-010's size. Folding it into a batch that had already rebuilt the evaluator surface and the award chain would have produced a change nobody could review | `RfqContracts.cs` BuyingOrgDto | Reproduced | M |
| ~~T-056~~ | §12.5 | **Closed (batch 8).** `createdAt` is on `ProposalDto`. The column existed all along — this was a projection omission, not a missing fact | batch 8 | Reproduced | S |
| ~~T-057~~ | §12.5 | **Closed (batch 8).** `totals { currency, grandTotal }` is on `ProposalDto`, derived from the line items on every read. Not stored: a stored total is a second source of truth for a number the items already determine | batch 8 | Reproduced | M |
| ~~T-058~~ | §12.5 | **Closed (batch 8), and it was worse than "redundant".** The second field held the RFQ's code under the name `proposalReferenceCode` — every consumer reading it by name read a lie. Now `proposalCode` + `rfqCode`, which is §12.5's own shape | batch 8 | Reproduced | S |
| ~~T-059~~ | FR-ADM-004 | **Closed (batch 9 send 4), and T-034 with it — they were one item.** `/api/v1/admin/reference/{table}` gives list / create / update / deactivate / reactivate over all five tables behind a new `reference.manage`, held by `system_admin` only. **One route family and one handler, not five**: the operations are identical and the fifth copy is where the audit call gets forgotten. **No DELETE exists** — D-28: every table is referenced BY CODE with no cascade, so deleting a Category a published RFQ points at would leave that RFQ describing nothing, and renaming a code would silently change what a historical award was for. Inactive rows stay visible to an admin so deactivation does not read as deletion. Every write is audited with a distinguishable action per operation. Two defects found while building: a list projection that answered 500 (now filtered in SQL, projected in memory — tens of rows), and a code longer than `Currency.Code`'s ISO bound of 3 answering 500 from Postgres instead of a 422 naming the limit | `ReferenceDataAdminEndpoints.cs`, `ReferenceDataAdminHandler.cs` | Reproduced | L |
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
| ~~`validityDays` vs `validityStart`/`End`~~ | **Closed (batch 9) as D-22** — derived on read, refused on write. Assessed in batch 8 as: The code carries strictly more information than the document: two dates versus one duration. Converting loses the anchor, and nothing says which date `validityDays` counts from — creation, submission, or award. That is a business question, not a rename, and inventing an anchor would have silently fixed a validity window to the wrong event |
| `technicalResponse` string vs `requirementAnswers[]` | **Independent — justification re-checked in batch 8 and it holds.** Two reasons, both still true: FEAT-09.5's submit gate must verify every mandatory Requirement has an answer, which needs queryable rows rather than opaque text; and the answers are bilingual, so the document's single string is the same collapse as `displayName`. Unchanged |

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
5. ~~T-053 — no `Idempotency-Key`.~~ **Closed in batch 9** for the three transitions §8.2 requires it on. The remaining
   endpoint without a state guard is where it bites.
6. **T-054 — the deadline is missing from the supplier RFQ list.** One field, directly in a bidder's face.
7. ~~T-059 — reference data is seed-only.~~ **Closed in batch 9.** A ministry can now add a document
   type without a deploy — and cannot delete one, which is D-28's deliberate half.
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
