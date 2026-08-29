# Requirements Audit

A durable, version-controlled record of what this system does and does not do, so that a
re-audit is a diff instead of a message that gets lost.

**Why this exists.** The first 160-item audit (2026-08-28) lived only in a chat exchange. That
is the same pathology as the Sonar config that used to live only in a vendor UI, and the
flagged-field vocabulary that used to live independently in two frontends — both fixed this
week after costing real time. This document is the fix applied to the audit itself.

**How to read the columns.**

| Column | Meaning |
|---|---|
| Baseline verdict | The 2026-08-28 result, where it was explicitly stated to this document's author. Left blank where it was not — a blank is "unknown," not a reconstructed ✅. |
| Current verdict | This pass's result: ✅ / ⚠️ / ❌ / N/A / UNVERIFIED. |
| Evidence | `file:line`, read in the session that produced the current verdict. |
| Why changed | `code changed` / `evidence standard changed` / `no change` / `(blank if baseline unknown)`. These are counted separately — summing them answers a different question than either alone. |

**The evidence standard, applied from this pass forward.** A verdict may not rest on an
instrument without naming what that instrument examined — measure, scope, and period for
anything cited from SonarCloud; file count for anything cited from a sweep; test count for
anything cited from a suite. "It passed" is not evidence. "It passed, over N of M things, as of
this run" is.

---

## Status

| Tier | Scope | Status |
|---|---|---|
| Tier 1 | Instrument-tainted items (A–G below) | **Complete this pass** — see below |
| Tier 2 | The 32 ❌ and 51 ⚠️ from the baseline | Not started |
| Tier 3 | The 73 ✅ from the baseline, re-checked against code | Not started |

---

## TIER 1 — Instrument-tainted items

Every item below had its original verdict established, in whole or in part, by an instrument
this project has since found reporting success over an empty, absent, or misdescribed
denominator. Checked first because they are the ones most likely to move.

### A. Established by the axe / a11y gate

| ID | Requirement | Baseline | Current | Evidence | Why changed |
|---|---|---|---|---|---|
| NFR-A11Y-001 | WCAG 2.2 AA across pages/components, automated axe-core in Playwright | ⚠️ ("gate exists, scope is the limitation, not existence") | ⚠️ | `src/frontend/tests/e2e/storybook-axe.spec.ts:36` — `withTags(['wcag2a','wcag2aa'])`, **not** `wcag22aa`. Runs against `storybook-static/index.json` entries only. Measured this session: 8 story files scanned, **0 of 18** `src/frontend/src/routes/*.tsx` route components ever reach axe. | no change (baseline already stated the limitation correctly) |
| NFR-A11Y-002 | Full keyboard operability, visible focus order, no keyboard traps | UNVERIFIED | **UNVERIFIED** | No automated check exists that could verify this; requires manual/e2e keyboard testing against real dialogs, which MSP-72 has not yet built. | no change |
| NFR-A11Y-003 | Contrast ≥ 4.5:1 / 3:1, validated in both AR and EN | UNVERIFIED | **UNVERIFIED** | Same reason as -002: axe's contrast rule only ever sees Storybook fixtures, not real pages in both locales. | no change |
| NFR-A11Y-004 | Semantic structure + ARIA on interactive components | (blank) | ⚠️ | Same instrument as -001 — axe does check ARIA rules, but only against 8 isolated components, never a composed real page where ARIA relationships (e.g. `aria-describedby` across a form) can break on integration. | evidence standard changed |
| NFR-A11Y-005 | Screen-reader support, correct `lang`/`dir` attributes | (blank) | ✅ | `src/frontend/src/i18n/useDirection.ts:11-12` — `document.documentElement.dir = dir; document.documentElement.lang = i18n.language`, driven by the active i18next locale. This is not axe-dependent; verified directly. | evidence standard changed |
| NFR-A11Y-007 | Adequate target sizes; errors announced and associated with fields | (blank) | ⚠️ | Target-size (WCAG 2.5.8) is a `wcag22aa` criterion and the tag set doesn't include it — see NFR-A11Y-001. Error-association was not checked this pass (needs a form-level read, deferred to Tier 2/3). | evidence standard changed (target-size half); unverified (error-association half) |

### B. Established by a coverage figure

| ID | Requirement | Baseline | Current | Evidence | Why changed |
|---|---|---|---|---|---|
| NFR-MNT-003 | Domain/Application ≥ 80% line coverage; critical flows have integration + E2E tests | ❌ ("no coverage collection exists") | ⚠️ | Coverage collection exists now (backend: `--collect:"XPlat Code Coverage;Format=opencover"` across 4 assemblies — `Api`/`Application`/`Domain`/`Infrastructure`; frontend: vitest lcov). Whole-project figure from SonarCloud, this run, scope=project, period=90-day (post-hotfix): **51.4%, 14,771 ncloc.** No gate enforces 80% on Domain/Application specifically — the 45% ratchet is new-code-only and project-wide, not layer-scoped. 80% is not met by any measure. | code changed (collection now exists) — verdict moved ❌→⚠️ rather than ❌→✅ because the 80% figure itself is unmet and unenforced at the layer level |
| NFR-CMP-003 | Illegal transitions rejected by the domain; domain unit tests per state machine | (blank) | ✅ | `src/backend/Tests/Unit/Domain/SupplierDocumentStateMachineTests.cs` — 12 `[Fact]`/`[Theory]` methods (several `[Theory]` with multiple `InlineData`/`MemberData` rows, so actual assertion count is higher), including an explicit "every state is covered" guard (`Every_document_state_is_covered_by_these_tests`). Onboarding transitions covered in `SupplierTests.cs` / `SupplierLifecycleTests.cs`, not a single dedicated file but present and exercising `Submit`/`Resubmit`/`Approve`/`Reject`/`Suspend`/`Reactivate`/`Deactivate`. | evidence standard changed |

### C. Established by a CI gate since found absent or empty

| ID | Requirement | Baseline | Current | Evidence | Why changed |
|---|---|---|---|---|---|
| NFR-SEC-011 | Dependencies scanned for known vulns; builds fail on high/critical | ⚠️ (unconfirmed) | ✅ | `.github/workflows/ci.yml:61-100` — `.NET`: `dotnet list package --vulnerable --include-transitive`, with an explicit denominator guard (`grep -qE 'has the following vulnerable\|has no vulnerable packages'` — a scan that enumerated nothing is treated as a failure, not a pass). `npm`: `npm audit --audit-level=high`, currently reporting 0. Caveat: npm's 0 closed by removal of `@lhci/cli` (MSP-80), not adjudication — the mechanism is real, but current npm cleanliness is partly circumstantial and MSP-47 reinstates the dependency. | code changed |
| NFR-MNT-006 | Code style enforced; builds fail on lint/analyzer errors | (blank) | ✅ | `src/backend/Directory.Build.props:25` — `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. Verified live this session: `dotnet build -c Release` → **0 warnings, 0 errors.** Frontend: `no-unused-vars`/`react/rules-of-hooks` are `error` in `.oxlintrc.json` (MSP-95), currently 0 violations of either. | evidence standard changed |
| NFR-PERF-003 | Web LCP < 2.5s | ❌ (~2.9s measured) | **UNVERIFIED** | `grep -c "lhci\|lighthouse" .github/workflows/ci.yml` → 0. `@lhci/cli` was removed in PR #8 (MSP-80) and the `lhci` npm script no longer exists in `package.json`. Not a stale failing number — genuinely unmeasured. Tracked as MSP-47. | evidence standard changed (the old ❌ was real evidence at the time; today there is no evidence at all, which is a different and worse state) |
| NFR-PERF-005 | Route-level code splitting; initial JS ≤ 250KB gzipped | (blank) | ✅ | `npm run build` this session, largest chunk: `dist/assets/index-JvpKJlhD.js — 340.57 kB, gzip: 107.73 kB`. Second largest: `authStore-*.js — 37.72 kB gzip`. Route-level splitting confirmed present (`OnboardingPage`, `ReviewApplicationPage`, `SettingsPage` etc. each their own chunk, 1.3–4.0 kB gzip each). Largest single chunk (107.73 kB gzip) is well under 250KB; no evidence of an un-split monolith. | evidence standard changed |

### D. Established by a one-off live check

| ID | Requirement | Baseline | Current | Evidence | Why changed |
|---|---|---|---|---|---|
| NFR-SEC-010 | Security headers applied | ✅ (confirmed once via browser fetch) | ⚠️ | `src/backend/Api/Program.cs:301-307` — HSTS, CSP, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` all present in code. `grep -rln "X-Frame-Options\|Strict-Transport-Security\|SecurityHeaders" src/backend/Tests` → **no matches.** The headers are real; nothing asserts they stay present. One middleware reorder from silently absent, with no instrument that would notice. | evidence standard changed |
| NFR-MNT-008 | DB changes are versioned EF migrations; no manual schema drift | (blank, "sound" per baseline framing) | ⚠️ | **15** migrations on disk (not 11 as the original audit's premise assumed), verified by listing `Infrastructure/Persistence/Migrations/*.cs` excluding `Designer`/`Snapshot`. `grep -rln "PendingModelChanges" src/backend/Tests` and the same across `.github/workflows/ci.yml` → **no matches, either place.** No instrument anywhere checks that the migration set actually reproduces the current model from scratch. The versioning mechanism is real; the drift-detection half the requirement implies is not built. | evidence standard changed |
| NFR-SEC-006 | TLS 1.2+, HSTS, secure cookies | (blank) | ⚠️ | `docs/security/SECURITY-ARCHITECTURE.md:299-303` states TLS 1.3/HSTS/cipher policy as an edge/WAF-layer decision. Searched for an explicit "not verifiable in code" caveat on this section — **not found** in the current text (`grep -n "not verifiable in code" docs/security/SECURITY-ARCHITECTURE.md` → no match). Either that caveat was never written, or existed in a different document/message and was lost — same durability problem this whole file exists to fix. The underlying claim (TLS terminates upstream) is architecturally reasonable but is not something this repository's code can prove or disprove. | evidence standard changed |

### E. Established by a grep sweep with no denominator

| ID | Requirement | Baseline | Current | Evidence | Why changed |
|---|---|---|---|---|---|
| NFR-L10N-001 | Every UI string keyed via i18next; no hard-coded strings | (blank) | ✅ | Swept **32** route+component `.tsx` files (`src/frontend/src/routes` + `src/frontend/src/components`, excluding `.stories.tsx`/`.test.tsx`) for a JSX-text heuristic (`>[A-Z][a-zA-Z ]{3,}<`). **0 hits.** Caveat stated plainly: this is a heuristic over JSX children text, not attribute strings (`aria-label`, `title`, `placeholder`) or template literals — a narrower check than the requirement, not a full proof. | evidence standard changed |
| NFR-L10N-002 | CSS logical properties, no hard-coded left/right | (blank) | ✅ | Swept all `.tsx`/`.css` under `src/frontend/src` for physical-direction utility classes (`ml-`/`mr-`/`pl-`/`pr-`/`left-`/`right-`/`text-left`/`text-right`). **1 candidate**, `src/frontend/src/components/ui/Dialog.tsx:22` — inspected: `left-1/2` paired with `-translate-x-1/2`, a direction-agnostic centering idiom, not an asymmetric RTL bug. False positive, correctly excluded. | evidence standard changed |
| FR-IAM-012 | All auth events audited with correlationId | (blank, "55 call sites" cited pre-MSP-64) | ✅ | Re-derived post-MSP-64 (which removed the `correlationId` parameter from `IAuditLogger.LogAsync` entirely — every call now gets it from `IAuditContext` automatically, so a per-call count of that parameter is meaningless now). Current count in `Infrastructure/Auth`, `Infrastructure/Registrations`, `Infrastructure/Identity`: **14 call sites, 11 distinct event types** — `login_succeeded`, `login_failed`, `login_locked_out`, `login_mfa_failed`, `login_blocked_mfa_enrollment_required`, `mfa_enrolled`, `password_reset`, `refresh_rotated`, `refresh_reuse_detected`, `session_revoked`, `sessions_revoked_all`. Covers login success/failure, lockout, MFA (enrol + fail), refresh rotate/reuse, session revoke, password reset. | evidence standard changed (the "55 call sites" figure no longer means anything post-MSP-64) |

### F. Established by, or contradicted by, architecture/authorization tests

| ID | Requirement | Baseline | Current | Evidence | Why changed |
|---|---|---|---|---|---|
| FR-IAM-008 | Every protected endpoint declares required permission(s) | (blank) | ✅ | `Tests/Integration/EndpointAuthorizationCoverageTests.cs:41-58` — asserts every endpoint declares `IAuthorizeData` or `IAllowAnonymous`, with a denominator guard added this session (>40 endpoints examined, <10 excluded as infrastructure; current: 63 total endpoints, per `grep -c 'Map(Get\|Post\|Put\|Patch\|Delete)' Api/Endpoints/*.cs`). | evidence standard changed |
| NFR-SEC-004 | Authorization deny-by-default | (blank) | ✅ | `Api/Program.cs:157-164` — `options.FallbackPolicy = ...RequireAuthenticatedUser()`. Verified **independently** of FR-IAM-008 per instruction: this is the mechanism that catches an endpoint with no policy at all; FR-IAM-008's test is the mechanism that catches an endpoint declaring neither `[Authorize]` nor `[AllowAnonymous]` explicitly. They are complementary, not the same test, and both are present. | evidence standard changed |
| NFR-MNT-001 | Clean Architecture layering enforced by NetArchTest in CI | (blank, "does it still run as its own job" was the concern post-CI-consolidation) | ✅ | `.github/workflows/ci.yml:158` — `Architecture tests (NetArchTest)` step confirmed present (not a separate *job* since CI consolidation in PR #7, but it does still run, which was the actual concern). `src/backend/Tests/Architecture/LayerDependencyTests.cs` — **9** rules, including a denominator assertion added this session on the one filtered rule. | evidence standard changed |
| NFR-MNT-004 | Real-DB integration tests via Testcontainers; no mocked persistence | (blank, "sound" per baseline framing) | ✅ | Mechanism confirmed real: `PostgresApiFixture` + `WebApplicationFactory`, 63 integration tests, real Postgres via Testcontainers. **Stated explicitly per instruction, so this ✅ does not imply more than it covers:** behavioral reach is **16 of 52** distinct route patterns actually invoked by an integration test (measured this session via `grep -rhoE '"/api/v1/...'` against `Tests/Integration/*.cs` vs. `Api/Endpoints/*.cs`). The mechanism is sound; roughly two-thirds of mapped endpoints have no integration-level behavioral test, only the structural authorization-declaration check from FR-IAM-008. | evidence standard changed |

### G. Anything whose citation is a SonarCloud rating

No Tier 1 item's final verdict rests solely on a SonarCloud rating this pass — each item above cites a file, a test, or a CI step directly. Recorded here as the standing rule for Tier 2/3: any item whose natural evidence is "SonarCloud says X" must state **measure, scope, and period**, not just a rating letter.

**Current whole-project reading**, for reference in Tier 2/3 (measure=`security_rating`/`reliability_rating`/`sqale_rating`/`coverage`/`duplicated_lines_density`, scope=project, period=90-day post-hotfix, as of run `33265178268` / commit `5230053`):

```
Security rating:       A
Reliability rating:    C   <- new since the new-code-period fix; see note below
Maintainability:       A
Coverage:               51.4%
Duplication:             4.0%
Lines of code:       14,771
```

**Reliability C is a live finding, surfaced as a side effect of fixing the new-code period, not sought.** It was previously invisible because the whole-project block only started reading a valid, complete analysis after today's hotfix sequence. Not yet triaged — flagged for Tier 2/3 or its own ticket depending on severity once the specific violations are read.

---

## An emergency fix that happened before this report, and belongs in it

Setting the new-code period to `reference_branch`/`main` (PR #31/#32, same day) **broke main's own push-to-main CI**: `Invalid new code period 'main': version is none of the existing ones`. A long-lived branch cannot reference itself as its own diff target. This was invisible to the PR that made the change, because a PR run never exercises a push-to-main analysis.

Fixed in two PRs (#33, #34), both merged, both green:
- **#33** — reset the period to a fixed `days`/`90` window, applied live via a direct API call independent of merge timing, so main recovered without waiting on the merge.
- **#34** — the days/90 window then exposed a *second* problem: on a project this young, a 90-day window still covers nearly the entire codebase on main's own analysis, so the Sonar rating conditions (Reliability, Duplication) that had never been exempted from push-to-main enforcement went red — `Reliability C`, `Duplication 3.4%` over "5,327 new lines." Extended the existing coverage-only exemption (documented in this file already, for the identical reason) to the rating conditions as well: enforced on PRs, reported-only on push to main.

**Left open, stated rather than assumed:** whether the SonarCloud PR-gate conditions (`new_coverage`, `new_security_rating`, etc.) read this project-level new-code-period setting at all, or whether PR analysis always diffs against the PR's target branch through a separate mechanism regardless of the setting. If the latter, the entire premise of PRs #31/#32/#33/#34 — that the project-level setting affects what PR gates enforce — needs re-examination. Not yet answered.

One integration test (`AuditCorrelationTests.Document_download_writes_a_persisted_audit_row`) failed once in CI during this sequence with a 404 and Npgsql teardown noise; passed 63/63 locally and on CI re-run. Treated as a confirmed flake, not a regression, on the strength of the local pass — not re-run blindly.

---

## Tier 2 / Tier 3 — Skeleton

The full 160-item list from the 2026-08-28 baseline follows, one row per item, for tracking. Baseline verdicts are filled where stated to this document's author; blank otherwise, per the rule above. Current verdict is `TBD` pending the Tier 2/3 pass. This section is the skeleton Part Zero asked for — it is not yet an audit.

*(Sections A–O, ~138 remaining items after Tier 1's 23, to be filled row-by-row in the Tier 2/3 pass. Superseded by that pass's actual content once delivered — this skeleton exists so the document is complete and trackable from today rather than partially populated with no record of what's missing.)*

| Section | ID range | Item count | Baseline verdicts known | Status |
|---|---|---|---|---|
| A — Identity & Access | FR-IAM-001…012 | 12 | 0 stated | TBD |
| B — Registration | FR-REG-001…007 | 7 | 0 stated | TBD |
| C — Onboarding | FR-ONB-001…012 | 12 | 0 stated | TBD |
| D — Profile | FR-PROF-001…011 | 11 | 0 stated | TBD |
| E — Documents | FR-DOC-001…009 | 9 | 0 stated | TBD |
| F — Business rules A | BRULE-001…015 | 15 | 0 stated | TBD |
| G — Business rules B | BRULE-016…028 | 13 | 3 stated (016 ⚠️→addressed MSP-91/BRULE-017 closed, 023 modeled MSP-68, 025 shipped MSP-68) | TBD |
| H — Visibility/audit/integrity | BRULE-084…100 (subset) | 10 | 0 stated | TBD |
| I — Audit | FR-AUD-001…006 | 6 | 0 stated | TBD |
| J — Notifications | FR-NOT-001…006 (subset) | 4 | 0 stated | TBD |
| K — Security/Privacy NFRs | NFR-SEC-*, NFR-PRIV-* | ~19 | NFR-SEC-004/006/010/011 in Tier 1 | TBD |
| L — Maintainability NFRs | NFR-MNT-001…009 | 9 | 001/003/004/006/008 in Tier 1 | TBD |
| M — Observability/compliance NFRs | NFR-OBS-*, NFR-CMP-*, NFR-AVL-* | ~11 | NFR-CMP-003 in Tier 1 | TBD |
| N — Localization/a11y NFRs | NFR-L10N-*, NFR-A11Y-* | ~16 | L10N-001/002, A11Y-001…005/007 in Tier 1 | TBD |
| O — Performance/portability NFRs | NFR-PERF-*, NFR-PORT-* | ~8 | PERF-003/005 in Tier 1 | TBD |

**Confirmed exclusions** (out of scope, procurement domain not yet built): `FR-OFF-*`, `FR-RFQ-*`, `FR-INV-*`, `FR-PRP-*`, `FR-CLR-*`, `FR-EVL-*`, `FR-CMP-*` (procurement), `FR-PWF-*`, `FR-AWD-*`, `FR-DSH-003/004/005/006`, `FR-SRCH-001/002/006`, `FR-ADM-001..010`, `FR-INT-002/004/006/007/008/009`, `BRULE-029..083`. No wrongly-excluded item found this pass.
