# MOTS Supplier Portal — progress

> **Taken at:** `main` @ `f73fef7`, 2026-09-08, after PR #126 (phase 3) and PR #127 (phase 4 and the
> Ministry screens) merged — plus two items finished on `fix/document-decision-etag` and named where they
> land: P12 item 26 (the document decisions' fresh ETag) and item 29 (the migration squash).
> **This edition moves verdicts for the first time in three editions.** Every screen the inventory names
> is built: the three that were missing, the five that were refused by decision, and the one that was
> unresolved. What remains is one other engineer's integration and one deferred testing pass.
> **Produced from source.** Numbers were re-derived on this commit — routes from the router, tests from
> the runners, lines over `git ls-files`, screens by opening the files. Rows carried forward from the
> previous edition are ones whose justification is a file that still exists and still says what it said.
> **`docs/` was read and not modified.** `ROADMAP.md`, `BACKLOG.md` and `SCREEN-INVENTORY.md` are the
> specification this document is measured against; they are not updated to match it.

---

## 0. Where the project stands

| | |
|---|---|
| Epics closed | **26 of 28** |
| Epics with named remaining work | **2** — EPIC-23 (the ERP adapter, another engineer), EPIC-27 (a language review, not a build) |
| Phases closed | **P0–P10** |
| Phases open | **P11** (ERP adapter), **P12** (four items, all deferred to a later testing pass by D-68) |
| Milestones reached | **M0–M7** |
| Screens: missing | **0** |
| Screens: refused by decision | **0** — all five were authorised: SCR-901 by D-60, SCR-601/602/603/606 by D-66 |
| Screens: unresolved | **0** — SCR-501 answered and built |
| Hand-written application code | **72,093 lines** production · **46,987** test · **31,571** generated |
| Backend tests | **1,243** — 17 architecture, 439 unit, 787 integration (Testcontainers: Postgres, MinIO, real clamd) |
| Frontend tests | **617** vitest · **226** Playwright, of which **137** are axe scans over 68 routes in both languages |
| Blocked on somebody else | **0 open questions.** Twelve were answered as D-57–D-68; two standing **gates** remain (§7) |
| Merged PRs | #79 → #127 in this record |
| Walkthrough findings | **13 raised, 12 fixed, 1 answered** — F-13 closed by D-67 (`WALKTHROUGH-FINDINGS.md`) |

**The single most important thing in this document is still §5**, and this edition adds a third kind of
finding to it. Batch 12 found that every suite was green while a clean install could not run a tender;
batch 13 found twelve more by walking the product differently. Phases 1–4 found a third class, and it is
about the instruments themselves: **a check that measures nothing looks exactly like a check that passes.**
Three of this batch's own new sweeps caught themselves being vacuous before they were trusted, and CI
caught three defects the local runs could not see.

---

## 1. Epics — all 28

Status vocabulary is deliberately narrow. **Closed** means nothing is outstanding against the epic's own
scope. **Open** names the specific screen, endpoint or decision that is missing — never the word "partial"
on its own. **Refused** means built work was declined with a recorded reason, not forgotten.

| # | Epic | Phase | Status | What is left, specifically |
|---|---|---|---|---|
| EPIC-01 | Identity & Access | 1 | **Closed** (#117) | Nothing |
| EPIC-02 | Supplier Registration | 1 | **Closed** (#109 and earlier) | Nothing |
| EPIC-03 | Onboarding | 2 | **Closed** (#117, #123) | The rule that was left off is on: **BRULE-016** derives the required-document set from the supplier's categories, decided as D-59 and shipped in phase 1 with `RequiredDocumentTypeResolver` as its single derivation — a category link NARROWS the set, and a supplier with no links owes what everyone owes |
| EPIC-04 | Supplier Profile | 2 | **Closed** (#117) | Nothing. The `/profile/*` path divergence is recorded in §6, not a gap |
| EPIC-05 | Documents | 2 | **Closed** (#117, #123, #127) | **BRULE-023 now fires.** D-58 named the commercial register and the tax card award-critical; phase 1 seeded the flags and proved the rule end to end; D-67 added the reinstatement that makes it safe — approving the replacement lifts the suspension, with an audit row naming the document and the reviewer |
| EPIC-06 | Offerings | 3 | **Closed** (#79, #80) | Nothing |
| EPIC-07 | RFQ authoring & lifecycle | 4 | **Closed** (#81, #116) | Nothing. T-030 split (4) belonged to P12 and is now closed there |
| EPIC-08 | Invitations | 5 | **Closed** (#82) | Nothing |
| EPIC-09 | Proposals | 6 | **Closed** (#84, #109, #117) | Nothing |
| EPIC-10 | Clarifications | 5 | **Closed** (#83, #116) | Nothing |
| EPIC-11 | Evaluation | 7 | **Closed** (#126) | **SCR-501 is built** — a standalone brief at `/back-office/rfqs/{code}/brief` carrying tender context, the template's instructions and every criterion's guidance, which the criteria snapshot now stores. And BRULE-061 stopped being unsatisfiable: the scoring form had no justification field, so a criterion that required one refused every score |
| EPIC-12 | Comparison | 7 | **Closed** (#86, #116) | Nothing |
| EPIC-13 | Procurement Workflow | 8 | **Closed** (#88, #116) | Nothing |
| EPIC-14 | Award | 8 | **Closed** (#87) | Nothing. D-63 settled the approval question with no code change: every award routes for approval, and there is no threshold |
| EPIC-15 | Notifications | 1 seeded, 9 deepened | **Closed** (#123, #126) | **SCR-901 is built, not refused.** D-60 ruled informational-only muteable; `NotificationClassification` classifies all 32 types — 22 actionable, 10 informational — and `IsMuteable` fails closed, so a type nobody has classified cannot be switched off by accident |
| EPIC-16 | Supplier Dashboard | 2 seeded, 9 deepened | **Closed** (#100) | Nothing |
| EPIC-17 | Procurement Dashboard | 4 seeded, 9 deepened | **Closed** (#99, #116, #118) | Nothing |
| EPIC-18 | Ministry Dashboard | 10 | **Closed** (#126, #127) | **All five outstanding screens are built.** SCR-604 category coverage under BRULE-086's aggregate grant; SCR-601/602/603/606 under **D-66**, which authorised the widest scope offered — every tender across every buying body, each named bidder and what it bid, including on tenders still open. Bounded to the demonstration environment; see the gate in §7 |
| EPIC-19 | Reporting | 10 | **Closed** (#101, #102, #117, #118) | Nothing |
| EPIC-20 | Search | 3 seeded, 10 deepened | **Closed** (#117) | Nothing |
| EPIC-21 | Administration | 3 seeded, deepened throughout | **Closed** (#126, #127) | The decision that blocked it is made: **D-63** — every award routes for approval, no value threshold, and the mechanism already did exactly that. SCR-402's supplier directory closed the other half |
| EPIC-22 | Audit & Compliance | 1 seeded, 10 surfaced | **Closed** (#116) | Nothing |
| EPIC-23 | ERP Integration | 11 | **Open — the adapter itself** | Another engineer owns the ERPNext adapter. Portal-side is done and honest about it: the Outbox, the dispatcher with backoff and dead-letter, `ExternalId`/`SyncStatus`/`LastSyncedAt` on the syncable roots, SCR-723's monitor, and `ErpSyncVacuityTests`, which **asserts the absence deliberately** — the only transport is `LoggingOutboxTransport`, so BRULE-011 passes vacuously and the test says so |
| EPIC-24 | Security | 0/1 seeded, 12 hardened | **Closed for scope; the review is deferred to M9** (#127) | The automatable half is built and it found real defects: `AuthorizationFuzzTests` calls every permissioned route as every persona lacking its permission, reading the roles' **live** claims, and treats a 5xx as a failure. That is **coverage, not a review** — see D-68 |
| EPIC-25 | Observability | 0 seeded, 12 hardened | **Closed** (#117, #127) | Item 25's gap — "telemetry is emitted and nothing consumes it" — is closed by `ops/`: six Prometheus alert rules and a ten-panel dashboard, every expression keyed to an instrument this product publishes, with what is deliberately absent stated rather than implied |
| EPIC-26 | Performance | verified per-slice, 12 hardened | **Closed for the read baseline; writes deferred to M9** | `perf/BASELINE.md` measures 18 reads over 30 samples, p95 2.2–17.3 ms against a 300 ms target. Write-path p95 and LCP/INP need a load-testing environment that does not exist; the harness does. D-68 |
| EPIC-27 | Localization | 0 seeded, 12 verified | **Open — a review, not a build** | Every screen ships AR and EN and SCR-716 lets an administrator reword any string without a release. D-62/D-65: the Arabic is **accepted, by Zaid Abdulkarim on 8 September 2026**, and the markers are off. The ~60 strings written after that acceptance — the five new screens and the four Ministry screens — are accepted **for the demonstration build without a line-by-line read**, recorded that way rather than as reviewed, and they need a proper read before any real tender runs |
| EPIC-28 | Responsive / Mobile | 0 seeded, 12 verified | **Closed** | The 320 px reflow guard covers every back-office route and both shells |

**Two epics carry outstanding work, and neither is a screen.** One is another engineer's integration; the
other is a person reading Arabic. Every screen in the inventory exists.

---

## 2. Phases P0 → P12

| Phase | What it was to deliver | Real state | What is left |
|---|---|---|---|
| **P0** Discovery & walking skeleton | Stack proven end to end; CI, design tokens, both shells, i18n/RTL, health, migrations | **Closed** | Nothing |
| **P1** Identity + registration | Self-register → verify → sign in, with real authn/z | **Closed** | Nothing |
| **P2** Onboarding · profile · documents | Profile, document lifecycle, reviewer approval, Outbox row on approval | **Closed** | Nothing. The two rules that were switched off — BRULE-016 and BRULE-023 — are on, and D-67 gave the second one its way back |
| **P3** Offerings · reference data · search seed | Category tree admin, offerings, scoped server-side lists | **Closed** | Nothing |
| **P4** RFQ authoring · review · publish | Author, bind a template, internal review, publish | **Closed** | Nothing |
| **P5** Invitations · clarifications | Invite Active suppliers, structured Q&A, window automation | **Closed** | Nothing |
| **P6** Proposals | Draft, price, validate, submit, guardrails | **Closed** | Nothing |
| **P7** Evaluation · comparison | Blind multi-evaluator scoring, consolidation, matrix | **Closed** | Nothing. SCR-501 is built, and BRULE-061's justification field makes the scoring form able to satisfy its own rule |
| **P8** Procurement workflow · award | Guided workspace, recommend → approve → issue, Outbox event | **Closed** | Nothing |
| **P9** Notifications+ · persona dashboards | Full notification centre; four persona dashboards | **Closed** | Nothing. SCR-901's preferences complete the centre |
| **P10** Ministry · reporting · audit · search deepening | Aggregate governance views, exports, audit UI, full-text search | **Closed** | Nothing. SCR-604 and the four D-66 screens close it |
| **P11** ERP integration | Outbox → ERPNext, supplier master sync, award → PO, resilience | **Open** | The adapter. Portal side complete; the transport is a logging stand-in and the suite asserts that rather than hiding it |
| **P12** Hardening · security · perf · a11y · launch | ASVS L2, p95 for reads **and writes**, LCP/INP, WCAG 2.2 AA audit, dashboards and alerts, concurrency | **Open — but now four items, not eleven** | **Closed here:** item 25 (dashboards and alerts), item 26 / T-030 split 4 (every guarded write now returns a fresh version), item 18 (the Arabic marker pass), item 29 (59 migrations squashed to one). **Deferred to a later testing pass under D-68, open against M9:** items 21 (ASVS L2 review), 22 (write-path p95), 23 (LCP/INP under load), 24 (WCAG 2.2 AA audit in both languages) |

### P12's four remaining items, and why they are not ours to close

Two need a specialist nobody has assigned — a security reviewer and an accessibility auditor. Two need a
load-testing environment that does not exist; the harness is written and the read baseline was taken with
it, so when the environment appears the work is short. D-68 records all four as deferred rather than done,
**open against M9, which does not close until they are done.** What exists meanwhile is coverage: the
authorisation sweep proves the gates we built behave as intended, and `axe` catches what a tool can catch.
Neither can find a class of problem nobody thought to test for, and this document says so in both places
rather than letting a green build imply otherwise.

---

## 3. Milestones

| Milestone | Phase | State |
|---|---|---|
| **M0** Walking skeleton | P0 | Reached |
| **M1** First business slice | P1 | Reached |
| **M2** Trusted registry | P2 | Reached |
| **M3** Buyer can publish RFQ | P4 | Reached |
| **M4** Suppliers can respond | P6 | Reached — see the caveat below |
| **M5** Evaluate & compare | P7 | Reached |
| **M6** End-to-end procurement | P8 | Reached — see the caveat below |
| **M7** Insight & governance | P10 | Reached, and now unambiguous: every Ministry screen the inventory names exists |
| **M8** ERP-integrated | P11 | Not reached. Waits on the adapter |
| **M9** Launch-ready | P12 | Not reached, and the four things standing in the way are named in §2 and D-68 |

### What M4 and M6 are worth, given what the walkthroughs found

Both were declared against a build in which a clean install could not complete a single tender (§5.1), and
then against one in which no tender with more than one line item could be bid on (§5.2). Both are fixed and
both were fixed after the milestone was declared. The lesson is recorded rather than filed away: **a
milestone reached by reading a table is a claim about code, and the only evidence that a milestone is real
is somebody doing the job the milestone describes.**

---

## 4. Reconciling the four views

The documents that answer "how far along are we" drift in a predictable direction: **an epic marked done
can contain a missing screen, and a phase marked closed can contain an epic that is not.** That drift is
now small — every screen exists — but the reason to keep this section is that the drift was never the
interesting part.

| View | Answers | Blind to |
|---|---|---|
| **Epics** (`BACKLOG.md`) | is the capability built | whether it works |
| **Phases** (`ROADMAP.md`) | did the sequence reach here | whether it can be walked again |
| **Screens** (`SCREEN-INVENTORY.md`) | does the surface exist | whether the surface is reachable, or lies |
| **Walking it** | can a person do the job | nothing — but it costs a person a day, and only covers the path they took |

> **A screen counts as delivered when a persona who owns it can reach it by clicking, and it renders
> populated data.** Anything else — an epic marked closed, a phase gate passed, an inventory row marked
> Built — is a claim about the code, not about the product.

**The fifth question, which this batch is about.** All four views above assume their instruments work. Three
of the sweeps written in phases 2–4 caught themselves measuring nothing before they were trusted, and one
authorisation sweep derived its expectations from a source that had gone stale. So the honest addition is:

| View | Answers | Blind to |
|---|---|---|
| **The instruments** | does the check pass | whether the check looks at anything |

That is not a hypothetical. §5.3 lists the four cases from this batch alone.

### The screen count, recomputed on this commit

`COMPLETION-INVENTORY.md` §0.1 measured 34 missing at batch 10. The last edition recorded 3 missing, 5
refused and 1 unresolved. All nine are now built, and each is a file you can open:

| Screen | Where it is | What authorised it |
|---|---|---|
| SCR-402 supplier directory | `routes/back-office/SupplierDirectoryPage.tsx` | Inside an existing grant; nothing to decide |
| SCR-307 compliance directory | `routes/ComplianceDirectoryPage.tsx` | Same |
| SCR-501 evaluator's brief | `routes/back-office/MyEvaluationBriefPage.tsx` | The product answer: yes, a standalone brief |
| SCR-604 category coverage | `routes/ministry/CategoryCoveragePage.tsx` | BRULE-086's aggregate grant |
| SCR-901 notification preferences | `routes/NotificationPreferencesPage.tsx` | **D-60** |
| SCR-601 national supplier registry | `routes/ministry/MinistrySupplierRegistryPage.tsx` | **D-66** |
| SCR-602 tender monitor | `routes/ministry/MinistryRfqMonitorPage.tsx` | **D-66** |
| SCR-603 awards & spend | `routes/ministry/MinistryAwardAnalyticsPage.tsx` | **D-66** |
| SCR-606 tender detail with bids | `routes/ministry/MinistryRfqDetailPage.tsx` | **D-66**, and the one a flag can withdraw |

**142 rows: 0 missing, 0 refused, 0 unresolved, 142 delivered.** The router declares **68 screen routes**
for them — its own count is 71, of which three are the layouts and the address you type, not screens; a
handful of screens appear twice because both shells own them. The a11y suite scans all 68 in both languages
on every build, and fails if the router declares a number it was not told about.

---

## 5. What driving the product finds that reading it does not

### 5.1 The batch-12 finding

**Every test suite was green while a clean install could not run a tender.** Five defects stood between an
empty database and one award: a supplier could not be activated without a document type nobody had seeded,
publishing refused without an active evaluation template with no message saying so, an Incoterm longer than
its column produced a 500, four built screens were reachable only by typing their address, and the
walkthrough's own driver asserted against the wrong shape. None of them was a unit-test failure, because
each was a gap between two things that were individually correct.

### 5.2 The batch-13 finding: the suites were green again, and a multi-line tender was unbiddable

Thirteen findings from one person walking the product by hand, twelve of them defects, **every one inside an
epic marked Closed, in a phase marked Closed, against inventory rows marked Built.** The product could not
price a second line item, correct a tender published with the wrong attachment, renew an expiring document,
or let a reviewer look at a decision they had already made. Five of the thirteen were one question — where
does the ETag come from, and does anything fetch it — with four different causes.

Closing them moved no verdict in this document, which is the finding rather than a quiet edition: **the
instruments had nothing to say either way.**

### 5.3 The phases 1–4 finding: an instrument that measures nothing looks exactly like one that passes

This batch wrote sweeps for the two classes batch 13 exposed. Four things happened, and all four are worth
recording because they are the same shape:

- **The precondition sweep found its own denominator empty.** The client-side half globbed `api/*.ts` with
  `import.meta.glob`, whose keys are relative to the importing file — so the sweep's audience was itself,
  and `proposals.ts` was silently skipped. Fixed, and the fix is the reason the sweep found anything.
- **The authorisation fuzz derived expectations from a stale source.** It compared each route's requirement
  against `Roles.DefaultPermissions` while a test elsewhere permanently grants `report.read` to
  `procurement_officer`. It now reads the roles' **live** claims, because the question is what the running
  system permits, not what a constant says it should.
- **CI found three defects the local runs could not.** Four TypeScript errors that `vitest` cannot see — a
  missing `aria-live` label on three loading tables, and two callers passing a bare array to a function that
  takes a filter object, which invalidated nothing at all. The a11y route guard failing at 58 against 68
  declared routes, which is precisely what it is for. And a duplication ceiling that made four screens share
  their scaffolding properly instead of by copy.
- **The migration squash had a live trap.** D-58's award-critical flags lived in a data migration. A squashed
  baseline seeds from the model, so without moving those flags into `HasData` first, both types would have
  come back `false`, BRULE-023 would have gone back to suspending nobody, **and every test that proves the
  rule fires would still have passed**, because they ran against a database seeded the old way.

The pattern across all four: the failure mode of a check is silence, not noise. Every sweep in this
repository now asserts its own denominator before it asserts its rule.

---

## 6. The instruments, and what each one cannot see

Six checks now exist for classes of defect that used to be found by a person pressing a button. Each is
listed with what it closes and what it does not, because an instrument trusted past its range is how the
next defect gets certified as absent.

| Instrument | The class it closes | What it still cannot see |
|---|---|---|
| `router.test.tsx` | A screen built, permissioned, tested and linked from nowhere — six defects before the guard existed | Whether the link is visible to the persona who owns the screen, and whether the screen renders populated data |
| `IfMatchPreconditionSweepTests` | A guarded write whose precondition no ETag-emitting read can supply, following the SPA's upward-only prefix walk | Whether a particular client actually calls that read |
| `preconditionCoverage.test.ts` | The client half: every guarded write in the committed contract has a caller that sends a version | Nothing about routes absent from the baseline — which is why a second test asserts the baseline knows every guarded write |
| `exportReachability.test.ts` | A capability no screen exposes — `PUT /rfqs/{code}` sat unused with its client function beside it | Whether the screen that references it is reachable |
| `AuthorizationFuzzTests` | Every permissioned route called as every persona lacking its permission, with a 5xx counted as a failure | A class of attack nobody thought to test for. It is coverage, not a review (D-68) |
| `app-a11y.spec.ts` route denominator | The scan quietly covering fewer routes than the router declares | Whether a screen reader can complete a tender in Arabic (D-68) |

Two exemption lists per sweep, hand-written, each entry naming why. The rule is the same everywhere: a
pattern-matched exemption lets the next instance join it silently, so exemptions are typed out by hand and
checked in both directions — a route that has gained what it was exempted for fails the sweep as loudly as
one that never had it.

---

## 7. Blocked on a person

> **Twelve questions were answered on 2026-09-08** and are recorded as **D-57 to D-68** in
> `DECISIONS-TAKEN.md`. **No question in this section is open any more.** What remains is two **gates** —
> conditions on work already built — and one unassigned testing pass.

### The two gates

| Gate | What it holds | Who can lift it |
|---|---|---|
| **Commercial visibility outside the demonstration** | D-66 authorised the Ministry screens' full disclosure **for the demonstration environment and its seeded data only** — where there is no real bidder, no real bid value and no live competition. The flag defaults to **off** in every environment and is switched on by the **demonstration seeder**, deliberately not by a migration: a migration runs everywhere, so the deployment step that creates the schema in production would have disclosed live bidder values there. Before this system holds a real supplier's bid, enabling it anywhere requires **written sign-off naming a person, a date and the scope** — live tenders or completed only, per-bidder values or awarded totals only | MOT Legal, or the Ministry official answerable for disclosure. Recorded as a gate on **D-57** |
| **A line-by-line read of the newer Arabic** | The Arabic is accepted and the markers are off. The ~60 strings written after that acceptance — the five new screens and the four Ministry screens — ship **for the demonstration build without a line-by-line read**, and are recorded that way rather than as reviewed. `ARABIC-REVIEW.md` groups them under their own phase headings so the read has a starting point | A native reviewer, before any real tender runs on this system. **D-65** |

### The one unassigned pass

ASVS L2, the WCAG 2.2 AA audit in both languages, and the two load-dependent measurements are deferred to a
later testing pass by **D-68**. They are unassigned, they are open against **M9**, and that milestone does
not close until they are done.

### Still genuinely undecided, and small

Whether a platform administrator should read across every organization's live procurements — the same
question BRULE-086 answers for the Ministry. Currently resolved by hiding the links rather than widening the
grant, which is the conservative default and needs no decision to stay correct.

---

## 8. What is genuinely left, with sizes

| Work | Epic / phase | Size | Note |
|---|---|---|---|
| ERPNext adapter and its ACL | EPIC-23 / P11 | **XL** | Another engineer. Portal side is done; `ErpSyncVacuityTests` marks the seam and asserts the absence rather than hiding it |
| OWASP ASVS L2 review | EPIC-24 / P12 item 21 | **L** | Deferred (D-68), open against M9. The automatable half exists and found real defects; the review is a person |
| WCAG 2.2 AA audit, both languages | EPIC-27, 28 / P12 item 24 | **M** | Deferred (D-68), open against M9. `axe` runs per build; an audit is a person with a screen reader in Arabic |
| Write-path p95 · LCP and INP under load | EPIC-26 / P12 items 22, 23 | **M** | Deferred (D-68), open against M9. **The blocker is an environment, not the work** — the harness is written and the read baseline was taken with it |
| Line-by-line read of ~60 Arabic strings | EPIC-27 | **S** | A gate, not a build (§7). Before any real tender |
| The disclosure sign-off | — | — | A gate, not a build (§7). Before any real bid |

**Ranked by what most changes whether the product can be used:** none of them. Every screen exists, every
rule that was switched off is on, and the two walkthroughs' eighteen findings are closed. What is left is
one integration owned elsewhere, one testing pass owned by nobody yet, and two conditions that are somebody's
signature rather than somebody's code.

**What is deliberately not on this list.** The two cross-cutting sweeps the last edition sized are built —
every `RequireIfMatch` route against a read that can supply it, and every `api/*.ts` export against a screen
that calls it. Both found defects; both are in §6 with what they cannot see.

---

## 9. Line counts

`cloc` and `tokei` are not installed on this machine, so this was counted over `git ls-files` — tracked files
only, which keeps `node_modules`, build output and scratch files out without an ignore list. Blank and
comment lines are **not** separated; these are physical lines.

| Bucket | Files | Lines | What is in it |
|---|---:|---:|---|
| **Production** | 569 | **72,093** | Hand-written application source |
| **Test** | 297 | **46,987** | Everything under a test directory or named as a test |
| **Walkthrough** | 9 | 3,122 | The driver, its scripts, the generated guide, and the `.docx` generator |
| **Generated** | 6 | **31,571** | The EF migration baseline with its Designer and snapshot, `package-lock.json`, the captured OpenAPI baseline |
| **Docs** | 52 | 19,866 | Markdown, including `docs/` |
| **Total** | 933 | **173,639** | Binary and image files not counted |

### Production, by language

| Language | Files | Lines |
|---|---:|---:|
| C# | 381 | 41,692 |
| TypeScript (TSX) | 96 | 16,857 |
| TypeScript | 62 | 9,319 |
| JSONC (notification copy) | 2 | 1,480 |
| YAML (CI) | 3 | 1,265 |
| JSON | 12 | 458 |
| CSS | 2 | 263 |
| Python (perf harness) | 1 | 250 |

### Test, by language

| Language | Files | Lines |
|---|---:|---:|
| C# | 192 | 34,407 |
| TypeScript (TSX) | 78 | 9,578 |
| TypeScript | 24 | 2,906 |

### What the generated figure means, and why it fell by 130,000 lines

Last edition it was **161,324 lines — 55% of the repository.** It is now **31,571, about 18%.** Nothing was
deleted from the product: 59 EF migrations, each carrying a `.Designer.cs` that restates the entire model,
were squashed into one baseline (P12 item 29). The generated bucket is now three files of C# — the baseline,
its Designer and the model snapshot — plus `package-lock.json` and the captured OpenAPI contract.

**The honest answer to "how much application code is there" is 72,093 lines**, against 46,987 lines of tests
— a test-to-production ratio of **0.65:1**. The two largest hand-written files are `i18n/config.ts` at 3,788
lines (every string in the product, in both languages) and `AppDbContext.cs` at 1,289.

---

## 10. What changed in this edition

### Phases 1–4 and the Ministry screens (PRs #123, #125, #126, #127, 2026-09-08)

**Verdicts moved, for the first time in three editions.**

- **§0/§1/§4: every screen exists.** The three missing, the five refused and the one unresolved are built.
  Four epics closed — EPIC-11, 15, 18, 21 — and EPIC-03 and EPIC-05 stopped carrying undecided rules.
- **§2: P12 went from eleven items to four.** Items 18, 25, 26 and 29 closed; 21–24 are deferred by D-68 and
  open against M9.
- **§7 has no open questions.** Twelve answers landed as D-57–D-68. The section is now two gates and one
  unassigned pass, which is a different shape of blocker and is labelled as one.
- **§4 gained a fifth question and §6 became its own section** — the instruments, six of them, each with what
  it cannot see. Written because three of this batch's own sweeps caught themselves measuring nothing.
- **§5.3 is new**: four cases from this batch where a check would have passed while looking at nothing,
  including a migration squash that would have silently reverted D-58.
- **§9 recounted, and the generated bucket fell from 161,324 to 31,571** because of the squash. Production
  72,093 (+4,477 since batch 13), tests 46,987 (+3,709).

### Batch 13 (PR #120, 2026-09-08)

**No epic, phase or milestone verdict moved**, and §5.2 explains why that was the finding. Thirteen defects
found by hand, all inside work marked complete; the two cross-cutting sweeps it sized are now built (§6).

### Batch 12 (PR #118, 2026-09-07)

Rewritten from source rather than edited, because statuses written by different batches had gone stale in the
same direction — work landed and the table did not move. Corrected EPIC-20, 21, 24, 25, 26, 01, 04, 05, 09,
19, recorded M7 as reached, and cut the missing-screen count from 34 to 3. Added the batch-12 finding (§5),
the router guard (§6), the milestone caveat (§3), the view reconciliation (§4) and the line counts (§9).
