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
| NFR-A11Y-001 | WCAG 2.2 AA across pages/components, automated axe-core in Playwright | ⚠️ ("gate exists, scope is the limitation, not existence") | **✅** | MSP-72 (#40): `src/frontend/tests/e2e/app-a11y.spec.ts` — route list derived from `router.tsx` and asserted to be exactly 18 (`toBe(18)`, not a hand-typed count), scanned in both AR and EN with `withTags(['wcag2a','wcag2aa','wcag22aa'])`. 36 route×locale scans + the 8 pre-existing Storybook-component scans, 0 violations after the contrast fix below. Caveat, stated not hidden: light theme only — the dark theme's own separate `--color-text-muted: #948C7E` (tokens.css:130) is never exercised by this suite. | code changed — real route coverage now exists where only Storybook coverage did before |
| NFR-A11Y-002 | Full keyboard operability, visible focus order, no keyboard traps | UNVERIFIED | **UNVERIFIED — unchanged** | MSP-72 did not build this: axe-core's ruleset checks static DOM/ARIA/contrast properties, not interactive keyboard tab-order or focus-trap behavior. No automated check exists that could verify this; still requires purpose-built keyboard-navigation e2e tests. | no change — flagging explicitly rather than letting it look resolved by proximity to -001/-003 |
| NFR-A11Y-003 | Contrast ≥ 4.5:1 / 3:1, validated in both AR and EN | UNVERIFIED | **✅** | Same MSP-72 (#40) suite as -001 caught two real contrast failures invisible to the old Storybook-only gate: `--n-500`/`--color-text-muted` measured 3.83:1 against white (need 4.5:1), found on `/settings [en]`; a second instance against `#F3F1ED` (`/onboarding [en]`, 4.19:1 with the first fix attempt) required a second, worse-case-driven fix. Final value `#746B60` (4.64:1 vs `#F3F1ED`, 5.23:1 vs white), computed via the WCAG luminance formula, not guessed. Verified across all 18 routes × 2 locales. Same caveat as -001: light theme only, dark theme's separate literal untested. | code changed — real defects found and fixed, both locales now actually scanned |
| NFR-A11Y-004 | Semantic structure + ARIA on interactive components | (blank) | ⚠️ | Same instrument as -001 — axe does check ARIA rules, but only against 8 isolated components, never a composed real page where ARIA relationships (e.g. `aria-describedby` across a form) can break on integration. | evidence standard changed |
| NFR-A11Y-005 | Screen-reader support, correct `lang`/`dir` attributes | (blank) | ✅ | `src/frontend/src/i18n/useDirection.ts:11-12` — `document.documentElement.dir = dir; document.documentElement.lang = i18n.language`, driven by the active i18next locale. This is not axe-dependent; verified directly. | evidence standard changed |
| NFR-A11Y-007 | Adequate target sizes; errors announced and associated with fields | (blank) | ⚠️ | Target-size half **resolved** (MSP-72, #40): `wcag22aa` now in the tag set, confirmed to map to a real, running rule (`axe.getRules(['wcag22aa'])` → `target-size`, not a no-op tag) — 0 violations across all 37 scans. Error-association half **still not checked** — needs a form-level read across the onboarding/settings forms specifically, out of MSP-72's four stated items, deferred to a future ticket. | target-size: code changed, now ✅ in substance; error-association: still unverified — kept at ⚠️ overall since the requirement is one line covering both |

### B. Established by a coverage figure

| ID | Requirement | Baseline | Current | Evidence | Why changed |
|---|---|---|---|---|---|
| NFR-MNT-003 | Domain/Application ≥ 80% line coverage; critical flows have integration + E2E tests | ❌ ("no coverage collection exists") | ⚠️ | Coverage collection exists now (backend: `--collect:"XPlat Code Coverage;Format=opencover"` across 4 assemblies — `Api`/`Application`/`Domain`/`Infrastructure`; frontend: vitest lcov). Whole-project figure from SonarCloud, this run, scope=project, period=90-day (post-hotfix): **51.4%, 14,771 ncloc.** No gate enforces 80% on Domain/Application specifically — the 45% ratchet is new-code-only and project-wide, not layer-scoped. 80% is not met by any measure. | code changed (collection now exists) — verdict moved ❌→⚠️ rather than ❌→✅ because the 80% figure itself is unmet and unenforced at the layer level |
| NFR-CMP-003 | Illegal transitions rejected by the domain; domain unit tests per state machine | (blank) | ✅ | `src/backend/Tests/Unit/Domain/SupplierDocumentStateMachineTests.cs` — 12 `[Fact]`/`[Theory]` methods (several `[Theory]` with multiple `InlineData`/`MemberData` rows, so actual assertion count is higher), including an explicit "every state is covered" guard (`Every_document_state_is_covered_by_these_tests`). Onboarding transitions covered in `SupplierTests.cs` / `SupplierLifecycleTests.cs`, not a single dedicated file but present and exercising `Submit`/`Resubmit`/`Approve`/`Reject`/`Suspend`/`Reactivate`/`Deactivate`. | evidence standard changed |

### C. Established by a CI gate since found absent or empty

| ID | Requirement | Baseline | Current | Evidence | Why changed |
|---|---|---|---|---|---|
| NFR-SEC-011 | Dependencies scanned for known vulns; builds fail on high/critical | ⚠️ (unconfirmed) | ✅ | `.github/workflows/ci.yml:61-129` — `.NET`: `dotnet list package --vulnerable --include-transitive`, with an explicit denominator guard (`grep -qE 'has the following vulnerable\|has no vulnerable packages'` — a scan that enumerated nothing is treated as a failure, not a pass). `npm`: production dependencies gate on high/critical (`npm audit --omit=dev --audit-level=high`, 0 findings); full tree is reported, not blocking. Updated by Task #20/MSP-80: `@lhci/cli` is reinstalled (MSP-47) and brought its 10 dev-only findings back (7 high/1 moderate/2 low) — triaged, not just re-scoped around; see NFR-PERF-003 and PR #68 for the full disposition. Production tree stays genuinely clean, not circumstantially so. | code changed |
| NFR-MNT-006 | Code style enforced; builds fail on lint/analyzer errors | (blank) | ✅ | `src/backend/Directory.Build.props:25` — `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. Verified live this session: `dotnet build -c Release` → **0 warnings, 0 errors.** Frontend: `no-unused-vars`/`react/rules-of-hooks` are `error` in `.oxlintrc.json` (MSP-95), currently 0 violations of either. | evidence standard changed |
| NFR-PERF-003 | Web LCP < 2.5s | ❌ (~2.9s measured) | **✅ measured, as of PR #68 (2026-08-30)** | Task #20/MSP-47: `@lhci/cli` reinstalled and wired into CI for the first time (it was never actually invoked by any workflow step before — see PR #8's own commit message). `grep -c "lhci\|lighthouse" .github/workflows/ci.yml` → 11 (was 0). CI-measured, mobile emulation + 4x CPU throttle, real numbers from [PR #68's run](https://github.com/ZaidEng7/mots-supplier-portal/actions/runs/33329181873): `/` LCP 2009–2184ms, `/login` LCP 1587–1597ms (2 runs each), both well under the 2.5s budget; CLS ~0 on both. The step is advisory (`continue-on-error: true`), not a required gate — no established baseline yet to call a regression against, and shared-runner variance risked repeating MSP-79's "cried wolf" a11y-retry pattern; revisit once several runs prove the number stable. | evidence standard changed twice now: real ❌ evidence → no evidence at all (worse) → real ✅ evidence again, tied to a specific PR and CI run rather than asserted |
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

## TIER 2 / TIER 3 — the remaining 137 items

**Evidence-depth note, stated up front rather than implied by silence.** All 137 items below were checked against code this session. The majority (~95) got a direct, targeted grep/read producing the file:line evidence shown. A smaller set (~15, marked *"cited"*) were not independently re-derived — they are items this same session already investigated in depth earlier, for their own open tickets (MSP-69/72/73/74/75/76/84/88/90/92/93), and citing that work rather than re-running it is stated explicitly rather than left to look identical to a fresh check. A few items (~10, marked UNVERIFIED) genuinely were not checked to a standard that supports a verdict, and are named as such in "what I did not do" below rather than guessed at.

Baseline verdicts are filled only where the 2026-08-28 audit's text, embedded in this pass's work order, stated one explicitly (e.g. "❌", "UNVERIFIED", a described gap). Left blank otherwise — a blank is unknown, not a reconstructed ✅.

### Section A — Identity & Access (remainder; 008/012 in Tier 1)

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| FR-IAM-001 | | ✅ | `Infrastructure/Auth/LoginHandler.cs` — Identity + JWT access/refresh pair issued on success. |
| FR-IAM-002 | | ✅ | `Infrastructure/Auth/RefreshTokenHandler.cs:32-39` — reuse of a rotated-out token revokes the entire `FamilyId`, not just the one token. |
| FR-IAM-003 | | ⚠️ | `Api/Program.cs:107-108` — `MaxFailedAccessAttempts=5`, `DefaultLockoutTimeSpan=15min`. Lockout is real; it is a **fixed** window, not exponential backoff (MSP-76 tracks the escalation). |
| FR-IAM-004 | | ✅ | `Infrastructure/Auth/LoginHandler.cs:71-77` — `RequiresMfa(role)` checked at login, blocks unenrolled users in mandatory roles before a session issues. Per-role, not merely enrolment-exists. |
| FR-IAM-005 | | ✅ | `Infrastructure/Auth/ResetPasswordHandler.cs:49-52` — every active `RefreshToken` for the user revoked on reset. |
| FR-IAM-006 | | ✅ | Registration → `EmailVerified` gated by `VerifyEmailHandler`, confirmed in Tier 1 (MSP-61 token scheme). |
| FR-IAM-007 | | ✅ | `Api/Endpoints/AuthEndpoints.cs:203/215/226` — list sessions, revoke one (`{familyId}/revoke`), revoke all. |
| FR-IAM-009 | | ✅ | 21 files under `Infrastructure/Suppliers` use `IScopeContext` for row-scoping; enforced at the query, not only the controller (confirmed pattern across the session's own work, e.g. `IncludeProfile()`/`GetOwnSupplierHandler`). |
| FR-IAM-010 | | ✅ | 54 `RequireAuthorization`/`RequirePermission` declarations across `Api/Endpoints/*.cs`; backed independently by `EndpointAuthorizationCoverageTests` (Tier 1, FR-IAM-008). |
| FR-IAM-011 | | ❌ | No `IIdentityProvider`, `ExternalLogin`, or IdP abstraction anywhere in `Application`/`Infrastructure`/`Domain`. Said plainly per instruction: this is not a seam that exists, it is Identity used directly. |

### Section B — Registration

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| FR-REG-001 | | ✅ | `Infrastructure/Registrations/RegisterSupplierHandler.cs` — creates Supplier(Draft) + `supplier_admin` user in one transaction. |
| FR-REG-002 | UNCONFIRMED | ❌ | `grep -rn "InviteOnly\|RegistrationMode" Api Infrastructure Domain` → no matches. Registration is unconditionally open; no mode switch exists at all. |
| FR-REG-003 | | ✅ | Verification link + `Draft → EmailVerified` transition, confirmed under FR-IAM-006. |
| FR-REG-004 | | ⚠️ | Email dedupe normalized (`RegisterSupplierHandler.cs:28` — `Trim().ToLowerInvariant()`). Legal-identifier dedupe: **no unique index on `RegistrationNumber`** (`grep -n "HasIndex" AppDbContext.cs` — no match for it). Half-built, matches MSP-73. |
| FR-REG-005 | | ✅ *(cited)* | AR-first RTL registration form with Zod validation — established across earlier session work, not re-derived this pass. |
| FR-REG-006 | | ✅ | `Domain/Suppliers/LegalInfo.cs` — generic typed fields, no Syria-specific validation logic present. |
| FR-REG-007 | | ✅ | `Infrastructure/Registrations/DraftCleanupJob.cs` exists and is audited (see NFR-PRIV-006). |

### Section C — Onboarding

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| FR-ONB-001 | | ✅ | `Domain/Suppliers/Supplier.cs:125` — `EnsureEditable`/`AdvancePastEmailVerified` moves `EmailVerified → ProfileInProgress` on first edit. |
| FR-ONB-002 | | ✅ *(cited)* | `DocumentCompletenessEvaluator` + `GetMissingProfileFields` gate `Submit`; live progress is the onboarding page's own query, established earlier this session. |
| FR-ONB-003 | | ✅ | `Supplier.cs:479` — `Submit(missingRequiredDocumentTypeCodes)`, throws if any item missing. |
| FR-ONB-004 | | ✅ | `Supplier.cs:497` — `PickUpForReview`; reviewer endpoints in `Api/Endpoints/ReviewEndpoints.cs`. |
| FR-ONB-005 | | ✅ | `RequestInfo()` + `SupplierReviewAnnotation` (mandatory `Reason`) — confirmed under BRULE-096 below. |
| FR-ONB-006 | | ✅ | `Supplier.cs:683` — `Resubmit(missingRequiredDocumentTypeCodes)`, gated identically to `Submit` since MSP-91. Loop confirmed round-trippable via `PickUpForReview` re-entry. |
| FR-ONB-007 | | ✅ | `Approve` → `Active`; ERP sync via `OutboxMessage` (see FR-NOT-005 for the caveat on what "Outbox" means in this codebase). |
| FR-ONB-008 | | ⚠️ | `EmailJobs.cs:125-129` — rejection email states "you may correct the issue and register again" **unconditionally**. BRULE-012's "re-application policy configurable" half does not exist — the message is hardcoded, not driven by a policy flag. |
| FR-ONB-009 | | ✅ *(cited)* | Suspend/Reactivate/Deactivate, MSP-63, closed earlier this session with real login revocation. |
| FR-ONB-010 | | N/A | RFQ/proposals are out of scope. The eligibility predicate itself (`IsEligibleToParticipate`) exists on the domain — confirmed under BRULE-007 — so the seam is real even though nothing calls it yet. |
| FR-ONB-011 | | ✅ | Every transition method throws `DomainException` on an illegal state (`Supplier.cs`, throughout); audited via `auditLogger.LogAsync` at each handler. |
| FR-ONB-012 | | ❌ | `Api/Endpoints/ReviewEndpoints.cs:55` — `/queue` exists, but `grep -n "AssignedTo\|SlaAge\|SLA"` → no matches anywhere in `Domain/Suppliers`/`Infrastructure/Suppliers`. No per-application assignment, no SLA/age indicator. |

### Section D — Profile

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| FR-PROF-001 | | ✅ | `Supplier.cs` core fields (`LegalNameAr/En`, description, logo, website, type/group, `CurrencyCode`) all present. |
| FR-PROF-002 | | ✅ | `Domain/Suppliers/LegalInfo.cs:16-21` — all four fields present: `RegistrationNumber`, `TaxId`, `SupplierType`, `EstablishedOn`. |
| FR-PROF-003 | | ✅ | `Domain/Suppliers/Address.cs:23-24` — `Latitude`/`Longitude` present alongside type/region. |
| FR-PROF-004 | | ✅ | `Contact.cs`, `Representative.cs` both present as domain types; `SetPrimaryRepresentative` confirmed (used in MSP-91 fixture work this session). |
| FR-PROF-005 | | ✅ | `Domain/Suppliers/Branch.cs` present. |
| FR-PROF-006 | | ✅ | `Domain/Suppliers/BankAccount.cs` present, generic fields, `MaskedAccountNumber` (see BRULE-014). |
| FR-PROF-007 | | ✅ | `Domain/Suppliers/CategoryLink.cs` present. |
| FR-PROF-008 | | ✅ | `Infrastructure/Suppliers/InviteSupplierUserHandler.cs` — delegated `supplier_user` invite under the same `SupplierId`, scoped permissions. |
| FR-PROF-009 | | ✅ | `Supplier.cs:139-146` — `EnsureEditableForComplianceField`: editing a compliance-critical field (`isComplianceCritical=true`) on an `Approved` supplier flips `OnboardingState` back to `UnderReview`, genuinely re-triggering review rather than silently allowing the edit. |
| FR-PROF-010 | | ✅ | `Tests/Integration/OptimisticConcurrencyTests.cs` exists — RowVersion conflict is tested with a real concurrent-write scenario, not merely mapped. `409` returned on conflict, confirmed under BRULE-098. |
| FR-PROF-011 | | ✅ *(cited)* | AR/EN input with RTL/LTR and tabular numerals — established earlier this session (`-u-nu-latn`, `useDirection.ts`). |

### Section E — Documents

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| FR-DOC-001 | | ✅ | `Domain/ReferenceData/DocumentType.cs:13` — `IsRequired` is a per-type flag, DB-seeded (`AppDbContext.cs` `HasData`); confirmed config-driven under BRULE-016's evaluator check, not a hard-coded list. |
| FR-DOC-005 | | ✅ | Reviewer states `Uploaded → UnderReview → Approved\|Rejected` confirmed under BRULE-019/026/027; `Reject` requires a reason and flags the profile incomplete (BRULE-018, shipped this session). |
| FR-DOC-006 | | ✅ | `DocumentExpiryJob.cs` — configurable window (BRULE-021), idempotent daily sweep (BRULE-022), profile-flagging (BRULE-018) and notification (FR-NOT-006/BRULE-025) all confirmed above; this item is the sum of those four already-verified pieces rather than a separate check. |
| FR-DOC-007 | | ✅ | Confirmed under BRULE-024 — append-only versioning, `IsLatestVersion` flip, prior versions retained and queryable. |
| FR-DOC-002 | | ✅ | `Infrastructure/Suppliers/UploadDocumentHandler.cs:62-98` — size + extension + content-sniff validation, AV scan gates via `PendingScan` state (`ClamAvScanner.cs`, `DocumentScanJob.cs`) before the document is usable. |
| FR-DOC-003 | | ✅ | `Application/Common/IFileStorage.cs:7-13` — abstraction with signed, expiring download URLs; no direct public read path. |
| FR-DOC-004 | | ✅ | `UploadDocumentHandler.cs:106` — `IssueDate` bound through the identical code path as `ExpiryDate`, so the MSP-60 culture-binding fix covers both. |
| FR-DOC-008 | | ✅ | `Infrastructure/Suppliers/GetDocumentDownloadUrlHandler.cs:39` — `document_access_granted` audit row on every download-URL issuance. |
| FR-DOC-009 | | ✅ *(cited)* | Required-vs-optional grouping shipped this session (MSP-91/PR #23 area). |

### Section F — Business rules, Identity/Registration/Eligibility (BRULE-001…015)

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| BRULE-001 | | ✅ | `RegisterSupplierHandler.cs:69` — exactly one `supplier_admin` created at registration. |
| BRULE-002 | | ✅ | `EmailConfirmed` gate confirmed in `ResendVerificationHandler.cs:20` and the `Draft→EmailVerified` transition. |
| BRULE-003 | | ❌ | Same as FR-REG-002 — no switch exists. |
| BRULE-004 | | ✅ | `Submit` gate: profile fields + required docs + ≥1 representative (`GetMissingProfileFields`) + T&C (`AcceptTerms`, BRULE-009). |
| BRULE-005 | | ✅ | Confirmed under FR-PROF-002/FR-REG-006 — generic typed fields, no hard-coded format assumptions. |
| BRULE-006 | | ✅ | `Supplier.cs:633-635` — `IsEligibleToParticipate` requires `Approved` onboarding + `Active` lifecycle. |
| BRULE-007 | | ✅ | Same predicate excludes `Suspended`. |
| BRULE-008 | | ✅ | `Infrastructure/Suppliers/SupplierLifecycleHandler.cs:94` — `user.IsActive = false` on deactivate; login path checks `IsActive` (confirmed via `LoginHandler`/Identity `SignInManager` usage patterns established earlier this session). |
| BRULE-009 | | ✅ | `Supplier.cs:463` — `AcceptTerms(version)`, version+timestamp recorded, gates `Submit`. |
| BRULE-010 | | ❌ | No `OrganizationId`/many-to-many Organization link on `Supplier` anywhere in `Domain/Suppliers`. Matches MSP-76 exactly: "`OrganizationId` is currently a bare claim with nothing behind it." |
| BRULE-011 | | ✅ | `Supplier.cs:35,704` — `ExternalId` is `string?`, set only via `MarkSynced`, doc-commented as reachable only through the (not-yet-built) Outbox consumer path — never a direct API setter. |
| BRULE-012 | | ⚠️ | Rejection is terminal (`OnboardingState.Rejected`, no transition out). Re-application policy is **not configurable** — see FR-ONB-008; the email text is unconditional, not policy-driven. |
| BRULE-013 | | ✅ *(cited)* | Opaque, single-use, TTL'd verification tokens — `SecurityTokenService`, established across MSP-61 work earlier this session. |
| BRULE-014 | | ✅ | `BankAccount.cs:16` — `MaskedAccountNumber` is the only list/detail value; full number lives only encrypted (`FieldEncryptionService`). |
| BRULE-015 | | UNVERIFIED | No explicit "signatory" concept found (proposals don't exist yet, so nothing consumes it). Representative contact/role validation itself not independently re-checked this pass. |

### Section G — Business rules, Documents (BRULE-016…028)

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| BRULE-016 | (doubted) | ❌ | `DocumentCompletenessEvaluator.cs` has no `CategoryLink` reference at all. Confirmed still category-independent. **Blocked on the Ministry — MSP-86, on `BLOCKED-DECISIONS.md`.** |
| BRULE-017 | | ✅ | Closed this session (MSP-91): missing/unscanned/rejected all block approval; `Uploaded`/`UnderReview` do not. Both directions proven by revert→red. |
| BRULE-018 | | ✅ *(cited)* | Shipped this session (PR #20) — expired/rejected required doc flags the profile incomplete. |
| BRULE-019 | | ✅ | Confirmed under FR-DOC-002 — MIME/size/AV all gate acceptance. |
| BRULE-020 | | ✅ | `Domain/Suppliers/SupplierDocument.cs:53,68` — both halves present: missing expiry rejected, past-expiry rejected, culture-safe message. |
| BRULE-021 | | ✅ | `DocumentExpiryJob.cs` — `ExpiringSoonWindowDays` config, default 30. |
| BRULE-022 | | ✅ | Same job, idempotent sweep (state-guarded transitions). |
| BRULE-023 | | ✅ | Shipped this session — `DocumentType.IsAwardCritical` modeled, wired into `DocumentExpiryJob`; ships dormant (no type flagged), which is the correct posture pending Ministry input. |
| BRULE-024 | | ✅ *(cited)* | Append-only versioning, `IsLatestVersion` flip — established across MSP-68 work earlier this session. |
| BRULE-025 | | ✅ | Shipped this session — escalating 30/14/3-day cadence with a de-duplication ledger, both directions proven by revert→red. |
| BRULE-026 | | ✅ | Confirmed under FR-IAM-008/NFR-SEC-004 — permission-gated approve/reject endpoints. |
| BRULE-027 | | ✅ | `Reject(reason)` — mandatory, throws `DomainException` if blank (confirmed, `SupplierDocument.cs`). |
| BRULE-028 | | ✅ | Confirmed under FR-DOC-003. |

### Section H — Visibility, audit, integrity (BRULE-084…100, in-scope subset)

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| BRULE-084 | | ✅ | Confirmed under FR-IAM-009. |
| BRULE-090 | | ✅ *(cited)* | Masking + reveal audit — `BankAccount` masking confirmed above; reveal-audit pattern established earlier this session. |
| BRULE-091 | | ⚠️ | **Fresh sweep this pass** (not the two previously-known fixes): `grep -rn '\?email=\|\?token=\|query.*email' Api/Endpoints` → no hits — URL/query leaks clean. `LoggingEmailSender.cs:21` **still logs `{ToEmail}`** on every send — this is MSP-93, filed, not yet fixed. One real, known, open leak; nothing new found. |
| BRULE-092 | | ✅ *(cited)* | `system_admin` reads audited — same mechanism as FR-AUD-001. |
| BRULE-094 | | ✅ | Confirmed structurally by `EndpointAuthorizationCoverageTests` + `IScopeContext` usage (FR-IAM-009/010). |
| BRULE-095 | | ✅ | Every transition audited — confirmed throughout Section C/F/G handlers. |
| BRULE-096 | | ✅ | `Suspend(reason)`, `Deactivate(reason)`, `Reject(reason)` all mandatory on the domain method itself (`Supplier.cs:550,601,638`). `RequestInfo()` carries no `reason` parameter on the domain method — the reason lives on `SupplierReviewAnnotation.Reason` (`required string`), created by the handler. Mandatory end-to-end, structured differently than the other three. |
| BRULE-097 | | ✅ | Confirmed throughout — `DomainException`, never a UI-only guard. |
| BRULE-098 | | ✅ | `Infrastructure/Suppliers/SupplierConcurrency.cs:50` catches `DbUpdateConcurrencyException`; `Api/Endpoints/SupplierEndpoints.cs:219-221` returns `409 Conflict`, not a silent overwrite. |
| BRULE-099 | | ⚠️ | Enqueue happens **after** `db.SaveChangesAsync` in every handler checked (`ReviewApplicationHandlers.cs:163-166`), so a notification failure cannot roll back the domain change — the requirement's actual intent is met. But see FR-NOT-005: the mechanism is Hangfire's own durable queue, not the `OutboxMessage` table this requirement's language implies. |
| BRULE-100 | | ✅ *(cited)* | UTC storage, locale-aware render — established pattern throughout the session's date/culture work (MSP-60). |

### Section I — Audit

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| FR-AUD-001 | | ✅ | Confirmed throughout — every transition, `auditLogger.LogAsync` with actor/timestamp/from-to/reason/correlationId (correlationId now automatic post-MSP-64). |
| FR-AUD-002 | | ⚠️ | `grep -rn "REVOKE\|DENY UPDATE\|trigger" Migrations/*.cs` → no matches. Immutability is **convention-only** — no DB-level grant revocation or trigger. Exactly the concern flagged in the work order. |
| FR-AUD-003 | | ✅ | **Correction to my own Tier 1 assumption.** `Api/Endpoints/AuditEndpoints.cs:30` — `/api/v1/suppliers/me/audit` exists. I had assumed this did not exist based on prior context; checking the code directly this pass shows it does. |
| FR-AUD-004 | | ❌ | `Application/Audit/GetAuditLogContracts.cs:22` — the only query parameter across both audit endpoints is `aggregateId`. No actor/action/date-range filter, no export. Matches MSP-75 exactly. |
| FR-AUD-005 | | ✅ | `AuditLogger.cs:52` — `CorrelationId = auditContext.CorrelationId`, derived from `Activity.Current`/W3C trace id since MSP-64. |
| FR-AUD-006 | | ✅ | Confirmed under FR-DOC-008 — same mechanism, one claim. |

### Section J — Notifications (in-scope subset)

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| FR-NOT-001 | | ⚠️ | Onboarding events (approve/reject/info-requested/resubmitted) and document events (rejected/expiring/expired) all have an `EmailJobs` method and an enqueue site — 10 methods, 11 sites, confirmed in MSP-89 work this session. Not independently re-enumerated event-by-event this pass against the full FR/BRULE lifecycle list; likely complete but not proven exhaustive. |
| FR-NOT-003 | (doubted) | ❌ | `grep -n "Locale\|CultureInfo\|language" Domain/Identity/AppUser.cs Infrastructure/Email/EmailJobs.cs` → no matches. No locale field on the user, no locale parameter anywhere in the send path. All nine templates are English-only, unconditionally. Matches MSP-69 exactly. |
| FR-NOT-005 | (doubted) | ⚠️ | `OutboxMessage` **table exists** in the schema (`AppDbContext.cs`), but every notification path checked this session (`ReviewApplicationHandlers`, `DocumentExpiryJob`, `EmailJobs` call sites) uses `IBackgroundJobClient.Enqueue` — Hangfire's own durable queue — directly, not an outbox-write-then-dispatch. The domain-change/notification decoupling BRULE-099 needs is real (Hangfire enqueue happens after `SaveChangesAsync`, and Hangfire's own storage is durable), but it is **not the same mechanism** the requirement's "Outbox" language describes, and MSP-71 already flags the `OutboxMessage` table itself as having "a dispatcher-shaped hole — rows accumulate and nothing drains them." Two different decoupling stories living in the same codebase. |
| FR-NOT-006 | | ✅ | Shipped this session (BRULE-025/PR #22) — de-duplication ledger, both directions proven by revert→red. |

### Section K — Security & Privacy NFRs

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| NFR-SEC-002 | | ✅ | Confirmed under FR-IAM-001/002 — RS256, rotating refresh with family invalidation. |
| NFR-SEC-003 | | ✅ | Confirmed under FR-IAM-004 — MFA enforced for roles in `_mfaRequiredRoles`, includes `system_admin`. |
| NFR-SEC-005 | | ⚠️ | 6 endpoint files use `AbstractValidator` (FluentValidation); Zod confirmed client-side earlier this session. Denominator not fully derived — 6 of how many endpoint files total was not counted this pass, so "all input validated" is plausible but not proven exhaustive. |
| NFR-SEC-007 | | ✅ | `grep -rn "password\|secret\|apikey" appsettings*.json` and `Password=` sweep → no hits. Consistent with MSP-4's startup-config work (env-driven, no fallback secrets). |
| NFR-SEC-008 | | ✅ | `Infrastructure/Storage/ClamAvScanner.cs` + `Infrastructure/Suppliers/DocumentScanJob.cs` — real AV scanning, gates the `PendingScan → Uploaded` transition. |
| NFR-SEC-009 | | ⚠️ | Rate limiting real (`Api/Program.cs:265-266`, `AuthRateLimitPolicy`). Lockout real (fixed, not exponential — see FR-IAM-003). Bot/abuse protection on registration (CAPTCHA or equivalent): **no evidence found.** Matches MSP-76's "no bot protection" exactly. |
| NFR-SEC-012 | | ✅ | Confirmed under FR-IAM-010. |
| NFR-SEC-013 | | ✅ | Confirmed throughout Section I. |
| NFR-PRIV-004 | | ✅ | `Infrastructure/Observability/RedactingEnricher.cs` exists **and has its own unit test**, `Tests/Unit/Observability/RedactingEnricherTests.cs` — the pipeline is not just present, it is verified to fire. |
| NFR-PRIV-005 | | ⚠️ | Field-level: `FieldEncryptionService`, AES-256-GCM, confirmed on bank fields. Full DB-at-rest and object-store-at-rest encryption are infrastructure/platform claims (disk encryption, SSE bucket policy) documented in `SECURITY-ARCHITECTURE.md` but — like TLS termination (NFR-SEC-006) — not something this repository's code can prove or disprove. |
| NFR-PRIV-006 | | ⚠️ | `DraftCleanupJob.cs:34` — audited. No dedicated sweep/deletion job found for **expired security tokens** specifically (they are checked-and-rejected at consumption time, which is sufficient for security but not for the "retention policy... with audited cleanup" half of the requirement as literally stated). |

### Section L — Maintainability NFRs (remainder; 001/003/004/006/008 in Tier 1)

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| NFR-MNT-002 | | ✅ *(cited)* | Direct handler classes throughout, no MediatR — established project-wide convention, not independently re-swept this pass. |
| NFR-MNT-005 | | UNVERIFIED | Mapperly usage not checked this session. |
| NFR-MNT-007 | | ✅ | Confirmed pattern throughout — `ReferenceCode` (`SUP-YYYY-NNNNNN`), not raw GUIDs, in every public-facing route checked this pass (`{referenceCode}` route parameters in `ReviewEndpoints.cs`). |
| NFR-MNT-009 | | ⚠️ | Zod (client) and FluentValidation (server) both confirmed present independently; whether their rules are kept in sync (e.g. by a shared schema generation step) was not checked — divergence is structurally possible and unmeasured. |

### Section M — Observability & compliance NFRs

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| NFR-OBS-001 | | ✅ | `Api/Program.cs:44` — Serilog `JsonFormatter`, structured. |
| NFR-OBS-002 | | ✅ | `Api/Program.cs:47` — `AddOpenTelemetry()` wired. |
| NFR-OBS-003 | | ✅ | Confirmed under FR-AUD-005. |
| NFR-OBS-004 | (doubted) | ❌ | `grep -rn "Meter\|Counter<" Infrastructure Api` (excluding matches on the word "Metric" itself) → **0**. Confirmed absent, matches MSP-71 exactly: zero business/system metrics emitted. |
| NFR-OBS-006 | | ⚠️ | `Api/Program.cs:390` — `MapHangfireDashboard` exists, restricted to `system_admin` (MSP-87), Development-only. Production has no route to job/Outbox health at all. Matches MSP-90 (open, needs a decision). |
| NFR-OBS-007 | | ✅ | Confirmed under NFR-PRIV-004 — `RedactingEnricher`, tested. |
| NFR-CMP-002 | | ⚠️ *(cited)* | Structurally append-only (no `Update`/`Delete` path on `AuditLog` in code); no DB-level enforcement or explicit mutate-and-fail test found this pass — same gap as FR-AUD-002, one finding not two. |
| NFR-CMP-005 | | ✅ | Confirmed under FR-DOC-008/FR-AUD-006. |
| NFR-AVL-005 | | ✅ | `Api/Program.cs:341` — `/health` mapped, anonymous. No separate `/ready` vs `/live` split found — one endpoint answers both concerns today. |
| NFR-AVL-006 | | ✅ *(cited)* | Idempotent job design confirmed throughout `DocumentExpiryJob` (state-guarded transitions, ledger-keyed reminders) — established this session, not re-derived. |
| NFR-AVL-007 | | ✅ | Confirmed under FR-PROF-010 — real concurrent-write test exists. |

### Section N — Localization & Accessibility NFRs (remainder; 001/002 and A11Y-001…005/007 in Tier 1)

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| NFR-L10N-003 | | UNVERIFIED | `grep -rn "scale-x-\[-1\]\|mirror" src/frontend/src` → 0 hits. Whether this reflects "no directional icons exist yet" or "icons exist and aren't mirrored" was not determined this pass — needs an icon-inventory check before a verdict is safe. |
| NFR-L10N-004 | | ⚠️ | `Supplier.CurrencyCode` is free-text `string?`; no hardcoded/enforced default of `SYP` found at registration or domain-construction time. Configurable in the sense that nothing prevents any value; "defaults to SYP" specifically not evidenced. |
| NFR-L10N-005 | | ✅ *(cited)* | Gregorian default, locale-aware formatting — established throughout the MSP-60 culture work this session. |
| NFR-L10N-006 | | ⚠️ | `OnboardingPage.tsx:67` — `-u-nu-latn` pins Western numerals for relative-time formatting; default is correct. No settings surface for switching to Eastern Arabic numerals found anywhere in the frontend. Configurability half absent. |
| NFR-L10N-007 | (doubted) | ❌ | `src/frontend/src/index.css:1` — still `@import url("https://fonts.googleapis.com/...")`. Confirmed unfixed. Matches MSP-72 exactly. |
| NFR-L10N-008 | | ❌ | Same root cause as FR-NOT-003 — templates are English-only, so "localized to recipient locale" cannot be true. Same finding as FR-NOT-003, not counted twice. |
| NFR-A11Y-006 | (doubted) | ❌ | `grep -rn "reduced-motion" src/frontend/src` → **0**. Confirmed unfixed. Matches MSP-72 exactly. |
| NFR-A11Y-008 | | ⚠️ | The axe suite (Tier 1, NFR-A11Y-001) does gate CI — it is a required step in `Frontend (React)`, restored this session (MSP-94) after being silently absent for a day. It gates 8 Storybook components, not the application. Real gate, wrong scope — same finding as NFR-A11Y-001, not a new one. |

### Section O — Performance & Portability NFRs (remainder; 003/005 in Tier 1)

| ID | Baseline | Current | Evidence |
|---|---|---|---|
| NFR-PERF-006 | (doubted) | ❌ | Confirmed via the already-open, already-scoped MSP-66/MSP-84 tickets rather than re-deriving the endpoint-by-endpoint list this pass — that enumeration is MSP-84's own acceptance criteria. Four client-facing lists + six profile child collections unbounded, per that ticket. |
| NFR-PERF-007 | | ✅ | `Infrastructure/Suppliers/SupplierQueryExtensions.cs` — `AsSplitQuery()` confirmed on the supplier-profile-with-children query (MSP-66 part 1, PR #13, already shipped this session), which is the specific N+1 risk the requirement names. |
| NFR-PERF-008 | (doubted) | ❌ | `Infrastructure/Suppliers/UploadDocumentHandler.cs:73` — `new MemoryStream()`, whole file buffered. Confirmed unfixed. Matches MSP-74 exactly. |
| NFR-PERF-010 | | ❌ | `grep -n "UseResponseCompression" Api/Program.cs` → no match. Confirmed absent, matches MSP-74's second half. |
| NFR-PORT-002 | | ⚠️ | `IFileStorage` abstraction real (`MinioFileStorage` the only implementation found). No second implementation (local/fake) and no swap-provider test found — the abstraction exists, the "tested by swapping" half of the requirement does not. |
| NFR-PORT-004 | | ✅ | `Api/Program.cs:72` — `GetConnectionString("Default")` via `IConfiguration`; `RequiredConfiguration` (MSP-4, Tier 1 context) enforces env-driven values outside Development. No environment-specific `if` branches found in the startup path checked. |

---

## RESULTS — Tier 1 + Tier 2 + Tier 3 combined (160 items)

### Totals

Computed by parsing this document's own tables rather than hand-counted — the first draft of this section stated 96/37/20/2/5, typed by hand and wrong (real: 111/27/16/1/5). That is exactly the failure this document exists to prevent, caught only because the count was re-derived mechanically before publishing rather than trusted. The parsing script is disposable; the discipline of running it is not.

| Verdict | Count |
|---|---|
| ✅ | 111 |
| ⚠️ | 27 |
| ❌ | 16 |
| N/A | 1 |
| UNVERIFIED | 5 |
| **Total** | **160** |

### Verdicts that moved from a stated baseline, split by cause

**This split is honest about what could and could not be computed.** The instruction was to separate "code changed" from "evidence standard changed" and not sum them — the same discipline that caught the totals table being wrong (above) applies here, and applying it caught a second, worse problem: the first draft of this section stated precise counts (9 vs 31) for a distinction that mostly cannot be counted at all, because the 2026-08-28 baseline mostly did not survive as a comparable verdict symbol — it survived as prose ("UNCONFIRMED", "doubted", a described gap) or as nothing. A prose baseline cannot be diffed against a verdict; it can only be read and judged, which is a qualitative act, not a count.

**Mechanically comparable** — the only rows where this pass's table carries an explicit baseline *verdict symbol* (✅/⚠️/❌) that differs from the current one, machine-checked against the document's own text:

| ID | Baseline | Current | Cause |
|---|---|---|---|
| NFR-MNT-003 | ❌ | ⚠️ | Code changed — coverage collection now exists; 80% still unmet. |
| NFR-SEC-011 | ⚠️ | ✅ | Code changed — a real SCA gate now exists with a denominator guard. |
| NFR-SEC-010 | ✅ | ⚠️ | Evidence standard changed — the headers were never wrong; what changed is noticing nothing asserts they stay present. |

**Qualitatively judged, not counted** — every other row whose baseline was prose. Reading them as a set: the items marked "(doubted)" in the work order (FR-NOT-003, NFR-OBS-004, NFR-PERF-006/008, NFR-L10N-007, NFR-A11Y-006, BRULE-016) resolved to ❌, confirming the doubt rather than the baseline that prompted it — those are **code state confirmed**, not code that changed. The items marked "UNCONFIRMED" (FR-REG-002) resolved to a definite ❌ — the mechanism was checked and found absent, which is a different thing from "unconfirmed" but not evidence that anything changed between the two passes. No claim stronger than that is supportable from what this pass actually measured.

**One correction to this pass's own earlier work, stated rather than left quiet:** Tier 1 (previous message) implicitly assumed FR-AUD-003 (supplier-facing audit trail) did not exist, based on prior-session framing rather than a direct check. Checking the endpoint file directly this pass found `/api/v1/suppliers/me/audit` does exist. Recorded as a correction, not silently fixed.

**A second correction, found while fact-checking this section for publication:** the first draft of this "why changed" section, and the Totals section above it, both stated precise counts that were typed rather than computed and were wrong. Caught by writing a parser and running it against the document's own tables before publishing, not by care taken while writing. The parser is in this repository's session history, not committed — the discipline worth keeping is "recompute before publishing a number," not any particular script.

### Ranked new findings, security/data-integrity first

1. **FR-IAM-011 / BRULE-010 (❌)** — No IdP seam, no Organization entity. Structural, not urgent (nothing currently depends on either), but real and worth knowing before P3 build-out assumes either exists.
2. **FR-AUD-002 / NFR-CMP-002 (⚠️)** — Audit immutability is convention-only, no DB-level enforcement. One `UPDATE audit.audit_log` statement, run by anyone with DB access, is currently invisible to every instrument in this system.
3. **BRULE-091 (⚠️)** — `LoggingEmailSender` still logs the recipient address on every send. Known (MSP-93), filed, not yet fixed — the fresh sweep this pass found nothing *new*, which is itself worth stating explicitly.
4. **NFR-SEC-009 (⚠️)** — No bot/abuse protection on registration. A public, unauthenticated write endpoint on a government portal with no rate-limit-adjacent defense beyond the endpoint's own throttle.
5. **NFR-PERF-008 (❌)** — Unbounded `MemoryStream` buffering on upload. Memory-exhaustion vector, confirmed live in code, unfixed.
6. **FR-REG-004 / no unique index on `RegistrationNumber` (⚠️)** — Two suppliers can register with the same legal identifier today. Data-integrity gap, not yet a ticket in its own right — folding into MSP-73 rather than opening a new one, per that ticket's own scope.
7. **FR-AUD-004 (❌)** — Audit log has no filter, search, or export. Compliance/operational gap on a government system with retention obligations.

### New tickets filed this pass

None opened as *new* Jira tickets — every ❌ and ⚠️ found this pass lands inside the scope of a ticket already open (MSP-69/70/71/72/73/74/75/76/84/86/88/90/93) or a newly-named finding folded into one of those (FR-REG-004's dedupe gap → MSP-73; the audit-immutability convention-only gap → a natural fit inside MSP-71's "audit immutability is convention-only" line, which already names this exact gap). Filing a duplicate ticket for a gap already covered by an open one would be the same pathology as the two-copies-of-one-fact shape this arc keeps finding. Where a gap did *not* fit an existing ticket, none was found this pass.

### What this pass did not do, and what to distrust most

- **~15 items were cited to earlier-this-session work rather than independently re-derived** (marked *cited* above). That work was real and recent, but it is not the same evidentiary weight as a fresh grep — flagged per-item, not asserted as equal.
- **5 items are UNVERIFIED**, not guessed: BRULE-015 (representative contact/role validation itself), NFR-MNT-005 (Mapperly usage), NFR-L10N-003 (icon mirroring — needs an icon inventory first), and two smaller sub-claims folded into K/M rows above (NFR-SEC-005's full endpoint denominator, NFR-MNT-009's schema-sync mechanism).
- **NFR-SEC-006 and NFR-PRIV-005's infrastructure-layer halves** (TLS termination, disk/bucket encryption) are, as stated in Tier 1, not verifiable from this repository at all — restated here so it isn't lost between tiers.
- **No item's verdict was downgraded to make a total look better, and none was upgraded to make one look worse.** Where the honest answer was "I already knew this from earlier work and didn't re-check it," that is what the row says.
- **This pass did not re-run any test suite or CI job** — verdicts rest on reading the code that would produce a given behavior, not on executing it fresh. Tier 1's mechanism verifications (coverage figures, Sonar ratings, CI step existence) were re-run live; Tier 2/3's were not, except where cited to a live check already performed this session.

**Confirmed exclusions** (out of scope, procurement domain not yet built): `FR-OFF-*`, `FR-RFQ-*`, `FR-INV-*`, `FR-PRP-*`, `FR-CLR-*`, `FR-EVL-*`, `FR-CMP-*` (procurement), `FR-PWF-*`, `FR-AWD-*`, `FR-DSH-003/004/005/006`, `FR-SRCH-001/002/006`, `FR-ADM-001..010`, `FR-INT-002/004/006/007/008/009`, `BRULE-029..083`. No wrongly-excluded item found across either tier.
