# MOTS Supplier Portal — progress

> **Taken at:** `main` @ `f070d9e`, 2026-09-08, after PR #120 (batch 13) merged.
> **Batch 13 changed no epic's status and is the most important thing in this document.** It closed
> thirteen defects found by a person walking the product by hand, several of them inside epics this
> table already called Closed. See §5.2 — the reconciliation in §4 gains a fourth view because of it.
> **Produced from source**, not from the previous editions of this table. Every verdict below names
> the file, route, endpoint or PR that justifies it, and anything that could not be settled from
> source is marked **Unresolved** rather than guessed.
> **`docs/` was read and not modified.** `ROADMAP.md`, `BACKLOG.md` and `SCREEN-INVENTORY.md` are the
> specification this document is measured against; they are not updated to match it.

---

## 0. Where the project stands

| | |
|---|---|
| Epics closed | **21 of 28** |
| Epics with named remaining work | **7** — EPIC-11, 15, 18, 19, 21, 23, 27 |
| Phases closed | **P0–P10** |
| Phases open | **P11** (ERP adapter, another engineer), **P12** (hardening: writes, LCP/INP, ASVS L2, a11y audit) |
| Milestones reached | **M0–M7** |
| Screens: missing | **3** — SCR-307, SCR-402, SCR-604 |
| Screens: refused by decision | **5** — SCR-901, SCR-601, SCR-602, SCR-603, SCR-606 |
| Screens: unresolved | **1** — SCR-501 |
| Hand-written application code | **67,616 lines** production · **43,278** test · **161,324** generated |
| Blocked on somebody else | **6 questions** (§7) — F-8's was answered as D-56 |
| Merged PRs | #79 → #120 in this record; the current batch is #120 |
| Walkthrough findings | **13 raised, 12 fixed, 1 answered** (`WALKTHROUGH-FINDINGS.md`) |

**The single most important thing in this document is §5**, and batch 13 doubled it. Every test suite
in this repository was green while a clean install could not complete one tender (§5.1, batch 12) —
and then, with those five fixed, while **no tender with more than one line item could be bid on at
all** (§5.2, batch 13). Both were found by driving the product, neither by reading it, and the second
was found in a state the first had already certified as working.

---

## 1. Epics — all 28

Status vocabulary is deliberately narrow. **Closed** means nothing is outstanding against the epic's
own scope. **Open** names the specific screen, endpoint or decision that is missing — never the word
"partial" on its own. **Refused** means built work was declined with a recorded reason, not forgotten.

| # | Epic | Phase | Status | What is left, specifically |
|---|---|---|---|---|
| EPIC-01 | Identity & Access | 1 | **Closed** (#117) | SCR-902 account settings, SCR-903 change-password/MFA/sessions, SCR-010 first-run locale and SCR-040's expiry overlay all landed in batch 11; T-084's component tests closed with them |
| EPIC-02 | Supplier Registration | 1 | **Closed** (#109 and earlier) | Nothing. Registration, verification and invitation acceptance are built and tested |
| EPIC-03 | Onboarding | 2 | **Closed for screens; one rule undecided** (#117) | Nothing screen-shaped. **BRULE-016** — whether the required-document set is category-dependent — has its join entity and admin surface built and the behaviour deliberately left off (§7) |
| EPIC-04 | Supplier Profile | 2 | **Closed** (#117) | SCR-121–126 all render. SCR-123/124/125/126 are served by the onboarding routes (`/onboarding/contacts`, `/addresses`, `/banking`, `/offerings`) rather than the `/profile/*` paths the inventory prints — a path divergence, recorded in §6, not a gap |
| EPIC-05 | Documents | 2 | **Closed for screens; one rule undecided** (#117) | SCR-130/131/132/133 are the documents centre and its filters; the version history endpoint exists (`GET /suppliers/{code}/documents/types/{typeCode}/history`). **BRULE-023** — which document types are award-critical — is a ministry data decision (§7) |
| EPIC-06 | Offerings | 3 | **Closed** (#79, #80) | SCR-127's offering editor is the catalog page at `/offerings` |
| EPIC-07 | RFQ authoring & lifecycle | 4 | **Closed** (#81, #116) | Nothing. #116's A-7 closed the last accountability gap. T-030 split (4) is concurrency hardening and belongs to P12 |
| EPIC-08 | Invitations | 5 | **Closed** (#82) | Nothing |
| EPIC-09 | Proposals | 6 | **Closed** (#84, #109, #117) | SCR-150 proposals list, SCR-155 revise and SCR-430/431 the buyer's view of bids all built; D-43's grant moved to `supplier_admin` |
| EPIC-10 | Clarifications | 5 | **Closed** (#83, #116) | Nothing. A-4 settled the visibility rule; both directions built and walked |
| EPIC-11 | Evaluation | 7 | **Open — one unresolved row** | **SCR-501** "evaluation instructions / brief". `MyEvaluationPage` renders each criterion's name, weight and max score and never renders the `guidance` text the template carries. `SCREEN-SPECIFICATIONS.md` names SCR-501 once, as an entry point, and never specifies it — so whether the criteria list *is* the brief cannot be decided from the documents. **Needs a person** (§7) |
| EPIC-12 | Comparison | 7 | **Closed** (#86, #116) | Nothing. A-1 settled tie-breaks |
| EPIC-13 | Procurement Workflow | 8 | **Closed** (#88, #116) | Nothing. A-7 gave the workflow an owner |
| EPIC-14 | Award | 8 | **Closed** (#87) | Nothing. A-3 kept the full recommend → approve → issue path, and batch 12 hid the three controls the permissions refuse |
| EPIC-15 | Notifications | 1 seeded, 9 deepened | **Open — one refusal** | T-076's 23 email bodies are admin-editable (#117). **SCR-901 notification preferences is refused, not missing** — D-48: FR-NOT-004 is tagged `[REQUIRES BUSINESS CONFIRMATION]` and nothing classifies the 30+ notification types as critical or not (§7) |
| EPIC-16 | Supplier Dashboard | 2 seeded, 9 deepened | **Closed** (#100) | Nothing |
| EPIC-17 | Procurement Dashboard | 4 seeded, 9 deepened | **Closed** (#99, #116, #118) | Nothing. SCR-400 was built in #99 and reachable only by URL until #118 put it in the navigation |
| EPIC-18 | Ministry Dashboard | 10 | **Open — one screen** | **SCR-604** category & sector analytics is not built; it sits inside BRULE-086's aggregate grant, so it is buildable today. SCR-601/602/603/606 stay **refused** under BRULE-086/A-10 |
| EPIC-19 | Reporting | 10 | **Closed** (#101, #102, #117, #118) | SCR-605 exists, `report.read` reached `ministry_viewer` in #117 (D-44), and #118 made the screen reachable by clicking |
| EPIC-20 | Search | 3 seeded, 10 deepened | **Closed** (#117) | **Superseded status — the old table said "SCR-906 and full-text search itself".** Both landed: `20260906011849_FullTextSearchVectors` adds `tsvector` computed columns over suppliers, offerings and RFQs, `/api/v1/search` is one cross-entity search authorised per kind, and SCR-906 renders it at `/back-office/search` |
| EPIC-21 | Administration | 3 seeded, deepened throughout | **Open — one decision** | **Superseded status — the old table listed SCR-716, 721, 722, 725, 726 as missing. All five are built** (#117): interface text at `/back-office/ui-strings`, and the operator's jobs, outbox, ERP, storage and security panels at `/back-office/operations`. What remains is **T-075's approval-hierarchy routing**, blocked on a threshold value nobody has specified (§7) |
| EPIC-22 | Audit & Compliance | 1 seeded, 10 surfaced | **Closed** (#116) | Nothing. T-079 closed the last screenless endpoint; SCR-720's explorer is at `/back-office/audit` |
| EPIC-23 | ERP Integration | 11 | **Open — the adapter itself** | Another engineer owns the ERPNext adapter. Portal-side is done and honest about it: the Outbox, the dispatcher with backoff and dead-letter, `ExternalId`/`SyncStatus`/`LastSyncedAt` on the syncable roots, SCR-723's monitor, and `ErpSyncVacuityTests`, which **asserts the absence deliberately** — `Supplier.MarkSynced` is never called because the only transport is `LoggingOutboxTransport`, so BRULE-011 passes vacuously and the test says so |
| EPIC-24 | Security | 0/1 seeded, 12 hardened | **Closed for scope; P12 verification outstanding** (#117) | **Superseded status — SCR-726 and SCR-903 are both built.** What is outstanding belongs to P12 rather than the epic: the OWASP ASVS L2 pass and authz fuzzing have not been run. OQ-014's AV-scanning scope stays a business question with A-11's fail-closed default in force |
| EPIC-25 | Observability | 0 seeded, 12 hardened | **Closed** (#117) | **Superseded status — the `Correlation-Id` request-header echo is built.** `CorrelationIdMiddleware` reads the caller's header, refuses a malformed value or `Guid.Empty` rather than carrying it, echoes the id on every response including successes, and makes it the id every audit row carries. Production dashboards and alerts are P12's, not the epic's |
| EPIC-26 | Performance | verified per-slice, 12 hardened | **Closed for the read baseline; writes unmeasured** (#117) | **Superseded status — a baseline exists.** `perf/BASELINE.md` measures 18 read endpoints over 30 samples: p95 **2.2–17.3 ms** against a 300 ms target. **Writes, LCP and INP have still never been measured** — that is P12 work, sized in §8 |
| EPIC-27 | Localization | 0 seeded, 12 verified | **Open — a review, not a build** | Every screen ships AR and EN; SCR-716 lets an administrator override any string. `ARABIC-REVIEW.md` holds drafted Arabic for the batches that added copy — including batch 12's five validation-catalogue entries — **awaiting a native reviewer** (§7) |
| EPIC-28 | Responsive / Mobile | 0 seeded, 12 verified | **Closed** | The 320 px reflow guard covers every back-office route and both shells |

**Seven epics carry outstanding work**, and only two of them are code: EPIC-18's SCR-604 and
EPIC-23's adapter. The other five wait on a person — a decision, a threshold, a classification, or a
language review.

---

## 2. Phases P0 → P12

| Phase | What it was to deliver | Real state | What is left |
|---|---|---|---|
| **P0** Discovery & walking skeleton | Stack proven end to end; CI, design tokens, both shells, i18n/RTL, health, migrations | **Closed** | Nothing. CI runs build, unit, integration on Testcontainers, architecture rules, axe, Playwright, container build and an OpenAPI contract gate |
| **P1** Identity + registration | Self-register → verify → sign in, with real authn/z | **Closed** | Nothing. MFA is enforced for `system_admin`, and batch 12 walked registration through to a verified account from an empty database |
| **P2** Onboarding · profile · documents | Profile, document lifecycle, reviewer approval, Outbox row on approval | **Closed** | Nothing. Walked in batch 12: upload with expiry dates, scan, submit, claim, review, approve, activate |
| **P3** Offerings · reference data · search seed | Category tree admin, offerings, scoped server-side lists | **Closed — gate re-checked, see below** | Nothing |
| **P4** RFQ authoring · review · publish | Author, bind a template, internal review, publish | **Closed** — but **this is where three of batch 12's five blockers lived**, and the phase gate did not catch them | Nothing outstanding now |
| **P5** Invitations · clarifications | Invite Active suppliers, structured Q&A, window automation | **Closed** | Nothing. The window opens on the `rfq-timeline` cron, walked in batch 12 |
| **P6** Proposals | Draft, price, validate, submit, guardrails | **Closed** | Nothing. The over-length Incoterm 500 (§5) was found and fixed here |
| **P7** Evaluation · comparison | Blind multi-evaluator scoring, consolidation, matrix | **Closed except SCR-501** | SCR-501, unresolved (§7) |
| **P8** Procurement workflow · award | Guided workspace, recommend → approve → issue, Outbox event | **Closed** | Nothing. §6.1 segregation of duties is enforced and demonstrated: the manager who recommends is offered no Approve |
| **P9** Notifications+ · persona dashboards | Full notification centre; four persona dashboards correct and in scope | **Closed** (#97, #99, #100, #116, #117, #118) | Nothing. Two dashboards — SCR-400 and SCR-300 — were built here and **reachable only by URL until #118** |
| **P10** Ministry · reporting · audit · search deepening | Aggregate governance views, exports, audit UI, full-text search | **Closed except SCR-604** (#101, #102, #116, #117) | SCR-604 category analytics. Full-text search landed in #117, which is what moved EPIC-20 |
| **P11** ERP integration | Outbox → ERPNext, supplier master sync, award → PO, resilience | **Open** | The adapter. Portal side complete; the transport is a logging stand-in and the test suite asserts that rather than hiding it |
| **P12** Hardening · security · perf · a11y · launch | ASVS L2, p95 targets for reads **and writes**, LCP/INP, WCAG 2.2 AA audit, dashboards and alerts | **Open — the largest single body of remaining work** | ASVS L2 pass and authz fuzzing; write-path p95; LCP and INP under load; a full AA audit in both languages (axe runs per-build, an audit is a different thing); production dashboards and alerts; T-030 split (4) concurrency hardening |

### P3's exit gate, quoted and re-checked

The gate reads:

> - Admin can build the Category tree and document types; a supplier can publish offerings against them.
> - Scoped supplier search returns only in-scope rows (verified by an authz/negative test).
> - All new list/table views paginate server-side, are RTL-aware, sortable, filterable, and accessible.

**It passes, and it did not pass as confidently when it was last quoted.** All three clauses are now
satisfiable from source: `ReferenceDataPage` builds the category tree and document types (including
`isAwardCritical` since #117); `OfferingsPage` publishes against them, walked in batch 12 at steps
32–33; `/api/v1/search` authorises per entity kind inside the handler rather than with a blanket
permission, with negative tests per kind; and the list endpoints share one `ListQueryPolicy` with
server-side paging. The clause that was weakest — the scoped search negative test — is the one
full-text search rebuilt in #117.

---

## 3. Milestones

| Milestone | Phase | State |
|---|---|---|
| **M0** Walking skeleton | P0 | Reached |
| **M1** First business slice | P1 | Reached |
| **M2** Trusted registry | P2 | Reached |
| **M3** Buyer can publish RFQ | P4 | Reached |
| **M4** Suppliers can respond | P6 | Reached — **but see below** |
| **M5** Evaluate & compare | P7 | Reached |
| **M6** End-to-end procurement | P8 | Reached — **but see below** |
| **M7** Insight & governance | P10 | **Reached in batch 11/12**, and never recorded until now: persona dashboards, Ministry oversight, audit explorer and reporting are all built and reachable |
| **M8** ERP-integrated | P11 | Not reached. Waits on the adapter |
| **M9** Launch-ready | P12 | Not reached. §8 sizes what stands between here and it |

**No later milestone exists in `ROADMAP.md` that has gone untracked.** M0–M9 is the whole list; M7 is
the one that had been reached without anybody writing it down, and M8 and M9 are the two ahead.

### What M4 and M6 are worth, given what batch 12 found

M4 and M6 were declared against a build in which **a clean install could not complete a single
tender**. Five defects (§5) stood between an empty database and one award, so the demoable journey
those milestones certify was demoable only against a seeded database — where the organization link,
the activated template and the supplier profile had all been written directly by fixtures, past every
path that was broken.

That does not retract them. The domain logic, the state machines and the authorisation they certify
were real and are still real. What it retracts is the *inference* people draw from a milestone: that
somebody could sit down in front of a fresh deployment and do this. Nobody could, until 2026-09-06.
**M4 and M6 hold as of `ba2dde2`, and they held only in principle before it.** The walkthrough is the
first evidence in this project that the end-to-end journey works from nothing, which is why it is a
deliverable rather than a test.

**And batch 13 narrowed them again, one day later.** Until `f070d9e`, a proposal could carry a price
for exactly one line item — the second answered a 500 (§5.2). So M4, "an invited supplier submits a
guarded, revisable proposal", was true only of **single-line tenders**, and no real ministry tender is
one line. The walkthrough that certified M4 priced one line, because the tender it authored had one.

That is not a reason to distrust the milestone. It is the reason a milestone should name the case it
was demonstrated on: **M4 and M6 were demonstrated on a one-line tender until 2026-09-08, and on a
three-line tender after it.**

---

## 4. Reconciling the three views

The three documents that answer "how far along are we" have drifted, and they drift in a predictable
direction: **an epic marked done can contain a missing screen, and a phase marked closed can contain
an epic that is not.**

| View | What it counts | Where it disagreed |
|---|---|---|
| **Epics** (`BACKLOG.md`) | Capability | EPIC-17 was closed while SCR-400, its officer dashboard, was reachable by nobody. EPIC-19 was closed while `ministry_viewer` could not open the only screen it delivered |
| **Phases** (`ROADMAP.md`) | Sequence and gates | P9 and P10 are closed and contain the two dashboards that were unreachable until #118. P4 is closed and contained three of the five blockers |
| **Screens** (`SCREEN-INVENTORY.md`) | Surface | Counts rows, not reachability. Its own count said 34 missing at batch 10; 31 of those are now built, and it does not know |

**Which is authoritative: the screen inventory, and only for what exists.** It is the finest-grained
of the three and the only one that names a thing a user can point at. An epic is a claim about
capability and a phase is a claim about sequence; both are satisfiable while a persona is stuck.

**But the inventory is not sufficient either**, and batch 12 is the proof: all four of the screens
#118 made reachable were "built" by the inventory's standard. A row can be built, permissioned,
tested and unreachable. So the honest rule is:

> **A screen counts as delivered when a persona who owns it can reach it by clicking, and it renders
> populated data.** Anything else — an epic marked closed, a phase gate passed, an inventory row
> marked Built — is a claim about the code, not about the product.

### The fourth view, added after batch 13

The three views above all count **surfaces**. None of them counts whether the surface can be *used in
sequence*, and batch 13 is what that omission costs.

Every one of its twelve defects sat inside an epic marked Closed, in a phase marked Closed, against
inventory rows marked Built — and the product could not price a second line item, correct a tender,
renew an expiring document, or let a reviewer look at a decision they had made. Closing them changed
no verdict anywhere in this document, which is precisely the problem: **the instruments had nothing to
say either way.**

| View | Answers | Blind to |
|---|---|---|
| Epics | is the capability built | whether it works |
| Phases | did the sequence reach here | whether it can be walked again |
| Screens | does the surface exist | whether the surface is reachable, or lies |
| **Walking it** | can a person do the job | nothing — but it costs a person a day, and only covers the path they took |

The two walkthroughs are the only instrument that has ever answered the fourth question, and each
found defects the others certified as absent. That is an argument for walking the product before
every release, not for distrusting the other three: an epic table is a plan, and a plan is not a
demonstration.

**And a walk only covers the path walked.** Batch 12's automated walk found none of batch 13's
thirteen, because it presses the buttons it was written to press. The person found them by pressing
the one beside it, mistyping a date, and going back to a screen the script visits once.

Batch 11 adopted that standard for its 34 screens and found eight defects with it. Batch 12 turned
half of it into a test (`router.test.tsx`, §6). The other half — "renders populated data" — is still
done by eye, and the walkthrough is how.

### The screen count, recomputed at `ba2dde2`

`COMPLETION-INVENTORY.md` §0.1 measured 34 missing at batch 10. Re-checking those 34 rows against
source today:

| | Count | Which |
|---|---|---|
| Built since (batches 11 and 12) | **30** | SCR-121–127, 130, 132, 133, 150, 155, 430, 431, 605, 716, 721, 722, 723, 725, 726, 902, 903, 906, 907, 908, 040, 010, 044, 047 |
| Still missing | **3** | SCR-307 and SCR-402 (the reviewer's and the buyer's supplier directory — `/back-office/search` finds suppliers, but no directory screen exists), SCR-604 (category & sector analytics) |
| Refused by decision | **1** of the 34 | SCR-901, under D-48 — joined by SCR-601/602/603/606 from elsewhere in the inventory, making 5 refusals in total |

30 + 3 + 1 = 34. ✓ SCR-501 was never in that 34: batch 10 recorded it separately as the one **Unresolved** row, and it still is.

**142 rows: 3 missing, 5 refused, 1 unresolved, 133 delivered.**

---

## 5. What driving the product finds that reading it does not

### 5.1 The batch-12 finding

**Every test suite was green while a clean install could not run a tender.**

731 integration tests, 431 unit tests, 17 architecture tests, 562 frontend unit tests and 206
end-to-end tests all passed, continuously, against a build in which a person starting from an empty
database could not get from registration to an award. Five defects stood in the way, each of them
fatal on its own:

1. **A staff invitation carried no organization**, and nothing else assigned one. BRULE-029 scopes
   every tender query by organization, so an invited procurement officer pressed *New RFQ* and got a
   bare 404 from a create with nowhere to put the row.
2. **An evaluation template could never be activated.** Activate, archive and fork all declare
   `RequireIfMatch`; no route in that family emitted an ETag, so the version those three demand could
   never be obtained. Every attempt answered 428 — and a tender cannot reach internal review without
   a bound template.
3. **The officer could bind a template they were forbidden to list.** Binding is gated on `rfq.edit`;
   listing was gated on `evaluation.template.manage`. The picker came back 403-empty.
4. **Binding cleared the client's ETag and put nothing back.** Submit-for-review, which can only ever
   happen after binding, answered 428 every time.
5. **A supplier could not save its own profile.** Three faults stacked: a hand-built `If-Match`
   overriding the correct stored value, a read path and a write path the ETag store could not join,
   and the one mutating supplier route of twenty-three that returned no fresh ETag.

### Why no suite could see any of them

**The tests seed past the setup.** Each of these five defects lives in a path the fixtures do not
travel:

| Defect | What the test fixture does instead |
|---|---|
| Staff has no organization | Writes `User.OrganizationId` directly when building the persona |
| Template cannot be activated | Inserts an `EvaluationTemplate` already `Active` |
| Officer cannot list templates | Calls the bind endpoint with an id it already holds; never opens the picker |
| Bind returns no ETag | The integration client attaches `If-Match` itself (`ETagAttachingHandler`) |
| Supplier profile cannot save | Writes the profile through the DbContext, not the API |

Every one of those shortcuts is reasonable in isolation. A test about *scoring* should not have to
walk the tender that produced the scores. But the sum of the shortcuts is a fixture that builds a
world the product cannot build — and each broken path was **correct in the fixture and broken in the
UI**, which is exactly the shape a green suite cannot detect.

This is not an argument for fewer fixtures. It is an argument that **a suite that seeds its own
preconditions can only test what happens after them**, and something has to exercise the
preconditions themselves. That something is now `walkthrough/run.sh`: reset, restart, walk 96 steps
from an empty database, and fail on the first step whose write does not land.

The five were found in one afternoon of driving the product, by somebody who had all the tests
passing in another window.

---

### 5.2 The batch-13 finding: the suites were green again, and a multi-line tender was unbiddable

Batch 12 fixed the five blockers above and left every suite green. A person then walked the product
by hand a second time — same path, same personas, from an empty database — and found **thirteen more
things**, twelve of them defects. `WALKTHROUGH-FINDINGS.md` carries all thirteen with the evidence.

**The one that matters most:** pricing the second line item of a proposal answered
`23505 duplicate key value violates unique constraint "PK_proposal_item"`. The patch handler told the
change tracker about every line in the array, including ones already stored, so saving the second
issued an INSERT carrying the first's primary key.

It cannot fire on the first line — RFC 7396 replaces an array wholesale, so the client resends what
it wants kept, and the first write on an empty proposal has nothing to re-add. **Every test in the
proposal suite priced exactly one line. So did every run of the automated walkthrough.** A defect that
made any real tender unbiddable sat behind a suite that could not reach it, in an epic this document
called Closed, one batch after a walkthrough certified the end-to-end journey.

**The other twelve, grouped by what they say about the instruments:**

| Kind | Findings | What the instruments could not see |
|---|---|---|
| A capability with no screen | no editor for a Draft tender; items and requirements addable and removable but not correctable | `PUT /rfqs/{code}` and `updateRfqBasics` both existed and nothing called either. Nothing asks whether an exported API function is reachable from a screen |
| A precondition nothing supplies | the reviewer's document decisions; offering edit and deactivate; adding a contact | Four separate 428s with four different causes. Each guard was correct; each read that should have issued the version either did not, or filed it where the write could not walk to it |
| An affordance the state forbids | the admin's procurement links; the supplier shell with no supplier guard; sign-in routing an administrator to the evaluator's dashboard | Every one of them gates on a permission, and `system_admin` holds all 104 |
| A rule that was true and invisible | an approved supplier could change nothing about itself, including renewing a document the product was warning them was expiring | The domain refused it correctly. No screen said so, and no reviewer could reopen them either |

**The pattern worth carrying forward.** Five of the thirteen were the same question — *where does the
version come from, and does anything actually fetch it* — with four different answers. That is now
the strongest candidate for a systematic pass: every route declaring `RequireIfMatch`, checked
against whether any client read supplies its precondition. Four were found by a person pressing
buttons; nobody knows how many remain.

**What batch 13 did not change: any epic's status.** Every defect above sat inside an epic already
marked Closed, and closing them changed no verdict in §1. That is the finding, not a footnote — see
§4, which now carries it.

## 6. The router guard — a class, not an instance

Batch 12's navigation fix was four screens. The guard beside it is the part that matters, because
**this was the sixth time** a screen had been built, permissioned, tested and left reachable only by
typing its address.

Every instrument in this repository asked whether a route **resolves**:

- the router's own type-checked route table,
- the Playwright suite, which navigates by URL,
- the axe suite, which visits a list of paths,
- the permission catalogue, which asserts each route's guard,
- the screen inventory, which records that a row has a route.

**None asked whether anything links to it.** So all four screens passed every check the project had,
five times over, and the defect kept coming back in a different place.

`router.test.tsx` now collects every `/…` string literal referenced anywhere outside `router.tsx` and
asserts each declared route appears among them, failing with the screen's name. It is deliberately
broader than matching `to=`: links here are written three ways — a bare attribute, a template
carrying a parameter, and a data array rendered as `to={step.path}` — and matching only the attribute
form produced three false positives on the first run. What matters is whether any component *names*
the path.

Two exemption lists, both hand-written so a new unreachable route cannot join one by accident:

```
ENTERED_FROM_EMAIL  /reset-password, /verify-email, /accept-invite, /accept-staff-invite
NOT_A_SCREEN        /  (the address you type)   /back-office  (a layout, not a screen)
```

Proved by reverting one link, which made it report `/back-office/review-dashboard` by name.

**What it does not close.** It asserts a path is *named*, not that the link is visible to the persona
who owns the screen, and not that the screen renders populated data when reached. Those are still
found by walking. The class it closes is "built and linked by nothing" — which is the class that
produced six defects.

---

## 7. Blocked on a person

Nothing here is an engineering task. Each is a value, a classification or a judgement that belongs to
somebody who owns the policy, and each has been refused rather than invented.

| # | Question | Who owns it | What it holds up |
|---|---|---|---|
| 1 | **What may the Ministry see?** Commercial figures, or aggregates only | MOT Legal | The Ministry dashboard shows no commercial values. BRULE-087 defaults to aggregate-only wherever visibility is undecided, so the product is correct either way — but SCR-601/602/603/606 stay refused until this is answered |
| 2 | **Which document types are award-critical?** (BRULE-023) | Ministry — a procurement-risk judgement | The auto-suspend rule fires on nothing. The flag is settable on SCR-710 since #117 and all three seeded types are still `false`. "Was blocked from participating for a fortnight" is not undone by reactivation, which is why no default was invented |
| 3 | **Is the required-document set category-dependent?** (BRULE-016) | Ministry | The join entity and admin surface ship; the behaviour is off. Switching it on changes what every already-approved supplier was required to have submitted |
| 4 | **Which notifications may a user switch off, and does it differ by role?** (FR-NOT-004, D-48) | Ministry | SCR-901. The requirement is tagged `[REQUIRES BUSINESS CONFIRMATION]` and nothing classifies the 30+ notification types. Whether a supplier may mute the message telling them they have won is not a default anyone should pick |
| 5 | **What is the approval threshold?** (T-075) | Ministry / finance | EPIC-21's approval-hierarchy routing. The mechanism is cheap; the number is not ours |
| 6 | **Is SCR-501 a separate brief, or is the criteria list the brief?** | Product / UX | One evaluation screen. `SCREEN-SPECIFICATIONS.md` names it once, as an entry point, and never specifies it |
| 7 | **Arabic review** | A native Arabic reviewer | `ARABIC-REVIEW.md` holds every drafted string from batches 9–12, including batch 12's five validation-catalogue entries. All are marked `[drafted]` and none has been reviewed |

Plus **OQ-014** (the scope of AV scanning), which is answered *provisionally* by A-11's fail-closed
default and does not block anything.

**Answered since this list was written:** whether an addendum may carry the revised document, raised
when a tender was published with the wrong file attached and no route existed to correct it. Ruled
**no** — D-56. A published tender's attachments stay locked, because bidders price against what they
downloaded; the remedy is to cancel and re-author, and the screen now says so before the officer
attaches anything.

**Left open by batch 13, and belonging to the same owners:** whether approving a renewed document
should automatically reinstate a supplier that an expiry suspended (§5.2's renewal path makes the
question live for the first time), and whether a platform administrator should read across every
organization's live procurements — the same question BRULE-086 answers for the Ministry, currently
resolved by hiding the links rather than widening the grant.

---

## 8. What is genuinely left, with sizes

| Work | Epic / phase | Size | Note |
|---|---|---|---|
| ERPNext adapter and its ACL | EPIC-23 / P11 | **XL** | Another engineer. Portal side is done; `ErpSyncVacuityTests` marks the seam |
| OWASP ASVS L2 pass + authz fuzzing | EPIC-24 / P12 | **L** | Not started. Per-endpoint negative tests exist; a systematic pass does not |
| Write-path p95, LCP and INP under load | EPIC-26 / P12 | **M** | `perf/BASELINE.md` covers 18 reads only, and says so |
| WCAG 2.2 AA audit in both languages | EPIC-27, 28 / P12 | **M** | axe runs per build; an audit is a person in both languages, not a linter |
| Production dashboards and alerts | EPIC-25 / P12 | **M** | Traces, metrics and logs are emitted; nothing consumes them |
| T-030 split (4) — concurrency hardening | P12 | **M** | Deliberately re-sized and deferred in batch 9, not forgotten |
| SCR-604 category & sector analytics | EPIC-18 / P10 | **S** | Inside BRULE-086's aggregate grant; buildable today |
| SCR-307 + SCR-402 supplier directory | EPIC-03, 21 | **S** each | `/back-office/search` finds suppliers; neither persona has a directory screen |
| SCR-501 | EPIC-11 | **S**, after the question | Blocked on #6 above |
| SCR-901 | EPIC-15 | **M**, after the question | Blocked on #4 above |
| Arabic review pass | EPIC-27 | — | Blocked on #7 above. Batch 13 added drafted strings for the tender-details editor, the close-reason prompt and the attachment warning |
| **A sweep of every `RequireIfMatch` route** | cross-cutting | **M** | The strongest instrument-shaped item on this list. Five of batch 13's thirteen findings were "the guard is right and nothing supplies its precondition", with four different causes. Each was found by a person pressing a button; nobody knows how many remain |
| **A reachability check for API functions** | cross-cutting | **S** | Batch 12's router test catches a screen nothing links to. Nothing catches a capability no screen exposes — `PUT /rfqs/{code}` sat unused with its client function beside it. Same two-list shape: every export in `api/*.ts` is referenced outside `api/`, or listed as deliberately not surfaced |

**Ranked by what most changes whether the product can be used:** none of them, with the caveat batch
13 earned. Batch 12 closed every blocker to running a tender end to end *as the walkthrough walked
it*; batch 13 then found twelve more by walking it differently, including one that made a multi-line
tender unbiddable. What remains on this list is somebody else's integration, somebody else's
decision, or P12's verification work — plus the two cross-cutting sweeps, which exist precisely
because the last two batches suggest more of the same is sitting in code nobody has clicked.

---

## 9. Line counts

`cloc` and `tokei` are not installed on this machine, so this was counted over `git ls-files` —
tracked files only, which keeps `node_modules`, build output and scratch files out without an ignore
list. Blank and comment lines are **not** separated; these are physical lines.

| Bucket | Files | Lines | What is in it |
|---|---:|---:|---|
| **Production** | 551 | **67,616** | Hand-written application source |
| **Test** | 269 | **43,278** | Everything under a test directory or named as a test |
| **Walkthrough** | 9 | 3,121 | The driver, its scripts, the generated guide, and the `.docx` generator |
| **Generated** | 118 | 161,324 | EF migrations with their Designer and snapshot files, `package-lock.json`, the captured OpenAPI baseline |
| **Docs** | 51 | 19,181 | Markdown, including `docs/` |
| **Total** | 998 | **294,520** | 103 binary/asset files not counted |

Batch 13 moved production by **+640** lines and tests by **+877** — more test than product, which is
what a batch of thirteen fixes with a regression test each looks like.

### Production, by language

| Language | Files | Lines |
|---|---:|---:|
| C# | 366 | 39,422 |
| TypeScript (TSX) | 94 | 15,460 |
| TypeScript | 62 | 8,775 |
| JSON | 13 | 1,758 |
| YAML (CI) | 2 | 1,135 |
| CSS | 2 | 263 |
| Python (perf harness) | 1 | 250 |
| Other (XML, config, HTML, JS, Docker, assets) | 11 | 544 |

### Test, by language

| Language | Files | Lines |
|---|---:|---:|
| C# | 181 | 32,094 |
| TypeScript (TSX) | 64 | 8,698 |
| TypeScript | 21 | 2,391 |

### What the generated figure means

**161,144 lines is 55% of the repository and none of it was written by hand.** 141,293 of those lines
are C# under `Migrations/`: 57 migrations, each with a `.Designer.cs` and the model snapshot beside
it, every one of which restates the entire model. The remaining 19,851 are `package-lock.json` and
the captured OpenAPI baseline.

**The honest answer to "how much application code is there" is 67,616 lines**, against 43,278 lines
of tests — a test-to-production ratio of **0.64:1**. The two largest hand-written files are
`i18n/config.ts` at 3,444 lines (every string in the product, in both languages) and
`AppDbContext.cs` at 1,255.

---

## 10. What changed in this edition

### Batch 13 (PR #120, 2026-09-08)

**No epic, phase or milestone verdict moved**, and §5.2 explains why that is the finding rather than
a quiet edition. What changed:

- **§5** became "what driving the product finds that reading it does not", with the batch-12 five as
  §5.1 and batch 13's thirteen as §5.2.
- **§4 gained a fourth view.** The three counting views are all blind to whether a surface can be used
  in sequence; the walk is the only instrument that has ever answered it, and it only covers the path
  taken.
- **§3's milestone caveat narrowed again**: M4 and M6 were demonstrated on a one-line tender until
  2026-09-08, because until then a second line could not be priced.
- **§7** records D-56 as answered, and adds the two questions batch 13 raised.
- **§8** gained the two cross-cutting sweeps — every `RequireIfMatch` route against a read that
  supplies it, and every `api/*.ts` export against a screen that calls it.
- **§9** recounted: production 67,616 (+640), tests 43,278 (+877).

### Batch 12 (PR #118, 2026-09-07)

Rewritten from source rather than edited, because several statuses were written at different times by
different batches and had gone stale in the same direction — work landed and the table did not move.

**Corrected:** EPIC-20 (search was listed as missing; full-text search and SCR-906 shipped in #117) ·
EPIC-21 (five admin screens listed as missing; all five shipped) · EPIC-24 (SCR-726 and SCR-903 both
shipped) · EPIC-25 (the `Correlation-Id` echo shipped) · EPIC-26 (a baseline exists and reads are
17–136× inside target) · EPIC-01, 04, 05, 09, 19 (all closed by batch 11's 34 screens) · M7 (reached
and never recorded) · the missing-screen count (34 → 3).

**Added:** the batch-12 finding (§5), the router guard (§6), the milestone caveat (§3), the
three-view reconciliation (§4), and the line counts (§9).
