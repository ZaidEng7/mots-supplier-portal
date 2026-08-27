# Product Backlog — MOTS Supplier Portal

> **Status:** Baseline v1 · **Owner:** Product + Principal Architect · **Date:** 2026-08-26
> **Canonical sources (must remain consistent):**
> [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md) ·
> [`DISCOVERY-REPORT.md`](../product/DISCOVERY-REPORT.md)
> **Related:** [`ROADMAP.md`](./ROADMAP.md) ·
> [`FUNCTIONAL-REQUIREMENTS.md`](../product/FUNCTIONAL-REQUIREMENTS.md) ·
> [`BUSINESS-PROCESSES.md`](../product/BUSINESS-PROCESSES.md) ·
> [`PERSONAS.md`](../product/PERSONAS.md) ·
> [`DOMAIN-MODEL.md`](../architecture/DOMAIN-MODEL.md) ·
> [`../ux/DESIGN-SYSTEM.md`](../ux/DESIGN-SYSTEM.md)

---

## How to read this backlog

- **Hierarchy:** `EPIC-##` → `FEAT-##.#` (feature) → `STORY-##.#.#` (user story) → technical (`T`) and
  QA (`Q`) tasks.
- **Personas** use canonical keys: `supplier_admin`, `supplier_user`, `onboarding_reviewer`,
  `procurement_officer`, `procurement_manager`, `evaluator`, `ministry_viewer`, `system_admin`, plus
  `system` (automated) and `anonymous` (public). See [`PERSONAS.md`](../product/PERSONAS.md).
- **Priority** = MoSCoW (**M**ust / **S**hould / **C**ould / **W**on't-now). **Complexity** = S/M/L/XL.
- Stories cite the **Functional Requirements** (`FR-…`) they satisfy and the **state machines** from
  the canonical brief §5. Illegal transitions are rejected by the **domain**, not just the UI.
- **Expansion depth (per task brief):** EPIC-01 → EPIC-09 (Identity … Proposals) are fully expanded to
  story + acceptance criteria + technical/QA tasks. EPIC-10 → EPIC-28 are expanded to feature +
  representative stories; their task-level detail is generated as they enter their roadmap phase.

### Global Definition of Done (applies to every story unless extended)

A story is **Done** when all of the following hold — this is additive to each story's own DoD:

1. **Vertical slice complete:** UI + API + Application + Domain + Infrastructure + tests all landed.
2. **Domain invariants:** illegal state transitions rejected by the aggregate (with a domain error),
   not merely hidden in the UI.
3. **AuthZ:** required `resource.action` permission(s) enforced at the API via policy handlers; row
   scoping (SupplierId/OrganizationId) verified with a negative test; UI hides unauthorized affordances.
4. **Audit:** every state change / sensitive action writes an **AuditLog** entry (actor, timestamp,
   from→to, reason, `correlationId`).
5. **Localization:** all strings i18next-keyed; correct in **`ar` (RTL)** and **`en` (LTR)**; tabular
   numerals for numeric/price fields; dates locale-aware.
6. **Accessibility:** passes axe-core; keyboard-navigable; visible focus; labelled controls; meets
   WCAG 2.2 AA.
7. **Responsive:** verified mobile → desktop for the relevant persona surface.
8. **Tests:** unit (domain/validation) + integration (Testcontainers Postgres, real DB) + component
   (RTL) + E2E (Playwright) for the primary happy path and key denied/error paths; `NetArchTest`
   architecture rules stay green.
9. **Observability:** structured Serilog log + OpenTelemetry span with propagated `correlationId`.
10. **Docs:** OpenAPI (native .NET + Scalar) updated; user-facing copy reviewed (AR/EN).

---

## Epic index

| Epic | Name | Primary phase | Fully expanded here |
|---|---|---|---|
| [EPIC-01](#epic-01--identity--access) | Identity & Access | P1 | ✅ |
| [EPIC-02](#epic-02--supplier-registration) | Supplier Registration | P1 | ✅ |
| [EPIC-03](#epic-03--onboarding) | Onboarding | P2 | ✅ |
| [EPIC-04](#epic-04--supplier-profile) | Supplier Profile | P2 | ✅ |
| [EPIC-05](#epic-05--documents) | Documents | P2 | ✅ |
| [EPIC-06](#epic-06--offerings) | Offerings | P3 | ✅ |
| [EPIC-07](#epic-07--rfq-authoring--lifecycle) | RFQ (authoring & lifecycle) | P4 | ✅ |
| [EPIC-08](#epic-08--invitations) | Invitations | P5 | ✅ |
| [EPIC-09](#epic-09--proposals) | Proposals | P6 | ✅ |
| [EPIC-10](#epic-10--clarifications) | Clarifications | P5 | feature + stories |
| [EPIC-11](#epic-11--evaluation) | Evaluation | P7 | feature + stories |
| [EPIC-12](#epic-12--comparison) | Comparison | P7 | feature + stories |
| [EPIC-13](#epic-13--procurement-workflow) | Procurement Workflow | P8 | feature + stories |
| [EPIC-14](#epic-14--award) | Award | P8 | feature + stories |
| [EPIC-15](#epic-15--notifications) | Notifications | P9 | feature + stories |
| [EPIC-16](#epic-16--supplier-dashboard) | Supplier Dashboard | P9 | feature + stories |
| [EPIC-17](#epic-17--procurement-dashboard) | Procurement Dashboard | P9 | feature + stories |
| [EPIC-18](#epic-18--ministry-dashboard) | Ministry Dashboard | P10 | feature + stories |
| [EPIC-19](#epic-19--reporting) | Reporting | P10 | feature + stories |
| [EPIC-20](#epic-20--search) | Search | P3/P10 | feature + stories |
| [EPIC-21](#epic-21--administration) | Administration | P3+ | feature + stories |
| [EPIC-22](#epic-22--audit--compliance) | Audit & Compliance | P1/P10 | feature + stories |
| [EPIC-23](#epic-23--erp-integration) | ERP Integration | P11 | feature + stories |
| [EPIC-24](#epic-24--security) | Security | P0/P12 | feature + stories |
| [EPIC-25](#epic-25--observability) | Observability | P0/P12 | feature + stories |
| [EPIC-26](#epic-26--performance) | Performance | P12 | feature + stories |
| [EPIC-27](#epic-27--localization) | Localization | P0/P12 | feature + stories |
| [EPIC-28](#epic-28--responsive--mobile) | Responsive / Mobile | P0/P12 | feature + stories |

---
---

## EPIC-01 — Identity & Access

**Goal.** Provide secure, standards-aligned authentication and fine-grained, policy-based authorization
that every other epic depends on: local ASP.NET Core Identity with JWT access + rotating refresh
tokens, MFA-readiness, self-service recovery, permission-claim RBAC (`resource.action`), and
server-side row scoping — swappable to an external IdP later without changing authorization semantics.

**FRs covered:** `FR-IAM-001..012`. **Traces to:** BR-08, BR-09, BR-19. **State machines:** none direct
(guards all). **Roadmap:** Phase 1 (seeded P0). **Domain:** User · Role · Permission.

### Features

| Feature | Name | Priority | Notes |
|---|---|---|---|
| FEAT-01.1 | Authentication (login, JWT access + rotating refresh) | M | `FR-IAM-001,002` |
| FEAT-01.2 | Credential policy & account protection (password policy, lockout) | M | `FR-IAM-003` |
| FEAT-01.3 | Email verification | M | `FR-IAM-006` (see EPIC-02) |
| FEAT-01.4 | Self-service password reset | M | `FR-IAM-005` |
| FEAT-01.5 | Multi-factor authentication (TOTP), policy-enforceable | S | `FR-IAM-004` |
| FEAT-01.6 | Session & token management (view/revoke sessions) | S | `FR-IAM-007` |
| FEAT-01.7 | Policy-based authorization on permission claims | M | `FR-IAM-008,010` |
| FEAT-01.8 | Row-scoping (Supplier/Organization/Ministry/global) | M | `FR-IAM-009` |
| FEAT-01.9 | External IdP swappability (Keycloak/Entra) | C | `FR-IAM-011` |
| FEAT-01.10 | Authentication audit events | M | `FR-IAM-012` |

---

#### FEAT-01.1 — Authentication (login, JWT access + rotating refresh)

**STORY-01.1.1 — Sign in with email and password**

> *As a* registered user (any persona), *I want* to sign in with my email and password, *so that* I
> receive a short-lived access token and a rotating refresh token and can use the portal securely.

- **Description.** Email+password authentication via ASP.NET Core Identity. On success issue a JWT
  **access token** (short TTL, carrying permission claims + scope claims: `sub`, `supplierId?`,
  `organizationId?`, roles, permissions) and a **rotating refresh token** (long TTL, stored hashed,
  bound to a token family). Refresh rotates on use; a reused/revoked refresh token invalidates the
  whole family and forces re-login.
- **Business value.** Foundational secure access; the token model powers all authz and enables IdP
  swap later (BR-09, BR-19).
- **Acceptance criteria.**
  - **AC1 — Given** valid credentials for an active account, **When** the user submits login, **Then**
    a `200` returns an access token + refresh token and the auth event is audited.
  - **AC2 — Given** invalid credentials, **When** login is attempted, **Then** a generic `401` (no
    user-enumeration) is returned and a failed-login audit event is recorded.
  - **AC3 — Given** a valid refresh token, **When** it is exchanged, **Then** a new access+refresh pair
    is issued and the old refresh token is invalidated (rotation).
  - **AC4 — Given** a previously-used (or revoked) refresh token, **When** it is presented, **Then** the
    entire token family is revoked, the request is rejected `401`, and a reuse-detection audit event is
    written.
  - **AC5 — Given** an unverified or disabled/suspended account, **When** login succeeds on credentials,
    **Then** access is refused with a clear localized reason and no usable session is issued.
  - **AC6 — Given** the login page, **When** rendered in `ar`, **Then** it is RTL, AR-first, accessible
    (labelled fields, visible focus), with inline Zod validation.
- **Dependencies.** EPIC-02 (a user exists), FEAT-01.10 (audit), design-system inputs (P0).
- **Priority.** M · **Complexity.** L
- **Definition of Done.** Global DoD + reuse-detection proven by integration test; tokens signed and
  validated; refresh tokens stored **hashed** (never plaintext); access token TTL/refresh TTL
  configurable; no user-enumeration on any auth error.

**Technical tasks.**
- `T-01.1.1a` Configure ASP.NET Core Identity (user store on PostgreSQL via EF Core 10; GUIDv7 keys).
- `T-01.1.1b` Implement JWT issuance (signing key management, claims: roles, permissions, scope ids).
- `T-01.1.1c` Implement refresh-token family model (hashed storage, rotation, reuse detection, revoke).
- `T-01.1.1d` `POST /api/v1/auth/login` + `POST /api/v1/auth/refresh` Minimal API endpoints + FluentValidation.
- `T-01.1.1e` `Login` / `RefreshToken` application handlers (direct dispatch, no MediatR).
- `T-01.1.1f` React login route (TanStack Router), RHF+Zod form, Zustand session store, TanStack Query mutation.
- `T-01.1.1g` Token storage strategy (access in memory, refresh via secure httpOnly cookie) + silent refresh.
- `T-01.1.1h` Emit auth `AuditLog` events with `correlationId`; OTel span.

**QA tasks.**
- `Q-01.1.1a` Unit: token family rotation + reuse-detection invalidation.
- `Q-01.1.1b` Integration (Testcontainers): login/refresh happy + reuse + disabled/unverified account.
- `Q-01.1.1c` Component: login form validation + RTL/AR rendering + axe.
- `Q-01.1.1d` E2E (Playwright): login → refresh → session persists across reload; wrong password path.
- `Q-01.1.1e` Security: assert no user-enumeration; refresh token not readable by JS.

---

#### FEAT-01.2 — Credential policy & account protection

**STORY-01.2.1 — Enforce password policy and lockout**

> *As a* security stakeholder, *I want* strong password rules, breached-password checks, and account
> lockout with backoff, *so that* accounts resist credential-based attacks.

- **Description.** Minimum length + complexity, breached-password check, Identity default hasher
  (server-side), and lockout after N failed attempts with exponential backoff. Policy values are
  configurable by admin (EPIC-21).
- **Business value.** Reduces account-takeover risk; supports OWASP ASVS L2 (BR-09, BR-19).
- **Acceptance criteria.**
  - **AC1 — Given** a weak or breached password, **When** set at registration/reset, **Then** it is
    rejected with a localized, specific (non-leaky) reason.
  - **AC2 — Given** N consecutive failed logins, **When** the threshold is crossed, **Then** the account
    is locked for a backoff window and an audit event is recorded.
  - **AC3 — Given** a locked account, **When** the window elapses (or an admin unlocks), **Then** login
    is possible again.
  - **AC4 — Given** password rules, **When** admin changes them, **Then** new rules apply to subsequent
    set/reset operations.
- **Dependencies.** FEAT-01.1, EPIC-21 (settings), EPIC-22 (audit).
- **Priority.** M · **Complexity.** M
- **Definition of Done.** Global DoD + lockout/backoff proven; passwords never logged; hashing verified.

**Technical tasks.**
- `T-01.2.1a` Configure Identity password options + lockout options (configurable via settings).
- `T-01.2.1b` Integrate breached-password check (k-anonymity range query or local list) behind an interface.
- `T-01.2.1c` Admin-configurable policy binding (EPIC-21 settings surface).

**QA tasks.**
- `Q-01.2.1a` Unit: password validator (weak/breached/valid).
- `Q-01.2.1b` Integration: lockout after N failures + backoff + unlock.
- `Q-01.2.1c` Security: confirm no password value appears in logs/traces.

---

#### FEAT-01.4 — Self-service password reset

**STORY-01.4.1 — Reset a forgotten password**

> *As a* user who forgot my password, *I want* to reset it via a secure email link, *so that* I can
> regain access without contacting support.

- **Description.** Time-limited, single-use email token; on reset, active sessions/refresh tokens are
  invalidated. No user-enumeration in the request-reset response.
- **Business value.** Self-service recovery; reduces support load; preserves security (BR-09).
- **Acceptance criteria.**
  - **AC1 — Given** any email input, **When** reset is requested, **Then** the response is identical
    whether or not the account exists (no enumeration); if it exists, a durable email job sends a link.
  - **AC2 — Given** a valid, unexpired, unused token, **When** a new compliant password is submitted,
    **Then** the password changes, all sessions are invalidated, and an audit event is written.
  - **AC3 — Given** an expired/used/tampered token, **When** submitted, **Then** it is rejected with a
    localized error and no change occurs.
- **Dependencies.** FEAT-01.1/01.2, EPIC-15 (email), EPIC-22 (audit).
- **Priority.** M · **Complexity.** M
- **Definition of Done.** Global DoD + single-use/expiry enforced; session invalidation proven.

**Technical tasks.**
- `T-01.4.1a` `POST /auth/forgot` + `POST /auth/reset` endpoints + validators.
- `T-01.4.1b` Token generation/verification (single-use, TTL) + session/refresh invalidation on reset.
- `T-01.4.1c` Localized AR/EN reset email template via durable job (EPIC-15).
- `T-01.4.1d` React forgot/reset routes with RHF+Zod, RTL.

**QA tasks.**
- `Q-01.4.1a` Integration: happy reset + expired/used token + enumeration-safe response.
- `Q-01.4.1b` E2E: forgot → email link → reset → old sessions rejected.

---

#### FEAT-01.5 — Multi-factor authentication (TOTP)

**STORY-01.5.1 — Enrol and verify TOTP, enforce per role**

> *As a* privileged user (e.g. `system_admin`, `procurement_manager`), *I want* to enable TOTP 2FA,
> *so that* my sensitive access has a second factor; *as an* admin *I want* to require MFA for a role.

- **Description.** Identity 2FA (TOTP): enrol (QR + secret), verify, recovery codes; policy can require
  MFA per role. When required and not enrolled, the user is forced into enrolment before privileged
  actions.
- **Business value.** Protects high-impact roles (award/approval/admin) (BR-09).
- **Acceptance criteria.**
  - **AC1 — Given** an enrolling user, **When** they scan and confirm a valid TOTP code, **Then** 2FA is
    enabled and recovery codes are issued once.
  - **AC2 — Given** a role with MFA required, **When** an un-enrolled member signs in, **Then** they are
    routed to mandatory enrolment before reaching privileged features.
  - **AC3 — Given** a 2FA-enabled user, **When** they log in, **Then** a valid second factor (or recovery
    code) is required; failures are rate-limited and audited.
- **Dependencies.** FEAT-01.1, FEAT-01.7 (policy), EPIC-21 (per-role MFA setting).
- **Priority.** S · **Complexity.** M
- **Definition of Done.** Global DoD + recovery-code flow; MFA state audited; enforcement by policy.

**Technical tasks.**
- `T-01.5.1a` Enable Identity TOTP; enrol/verify/disable endpoints; recovery codes.
- `T-01.5.1b` `RequireMfa` policy component keyed off role settings.
- `T-01.5.1c` React enrolment UI (QR, code entry, recovery codes) + login 2FA step, RTL.

**QA tasks.**
- `Q-01.5.1a` Integration: enrol/verify, required-role forced enrolment, recovery-code login.
- `Q-01.5.1b` Security: brute-force rate limiting on 2FA codes.

---

#### FEAT-01.6 — Session & token management

**STORY-01.6.1 — View and revoke active sessions**

> *As a* user, *I want* to see my active sessions and sign out one or all devices, *so that* I can
> contain a lost/compromised device.

- **Acceptance criteria.**
  - **AC1 — Given** multiple active refresh sessions, **When** I open session management, **Then** I see
    each with device/last-used metadata.
  - **AC2 — Given** a session, **When** I revoke it, **Then** its refresh family is invalidated and its
    next refresh fails `401`; the action is audited.
  - **AC3 — Given** "sign out all", **When** invoked, **Then** every refresh family except (optionally)
    the current is revoked.
- **Priority.** S · **Complexity.** M · **Dependencies.** FEAT-01.1.
- **DoD.** Global DoD + revocation is immediate and audited.

**Technical tasks.** `T` endpoints to list/revoke sessions; UI list; audit. **QA tasks.** `Q` integration
revoke → refresh fails; E2E sign-out-all.

---

#### FEAT-01.7 — Policy-based authorization on permission claims

**STORY-01.7.1 — Enforce `resource.action` permissions at the API**

> *As a* platform, *I want* every protected endpoint to declare and enforce required `resource.action`
> permission claims, *so that* access is least-privilege and never depends on the UI.

- **Description.** Authorization policies map to permission claims (e.g. `supplier.approve`,
  `rfq.publish`, `proposal.submit`, `evaluation.score`, `award.approve`, `admin.users.manage`,
  `audit.read`). Roles are named permission sets seeded per persona and admin-editable (EPIC-21). UI
  re-checks the same permissions solely to hide affordances.
- **Business value.** Fine-grained, auditable, least-privilege access (BR-08, BR-09).
- **Acceptance criteria.**
  - **AC1 — Given** an endpoint requiring `X.Y`, **When** a caller lacks it, **Then** `403` is returned
    and the attempt is audited; **When** the caller has it, the request proceeds.
  - **AC2 — Given** the UI, **When** a user lacks a permission, **Then** the corresponding control is
    hidden/disabled, but the API still enforces independently (verified by calling the API directly).
  - **AC3 — Given** seeded roles, **When** the system initializes, **Then** each persona has its default
    permission set; admins can edit roles without code changes.
- **Dependencies.** FEAT-01.1 (claims in token), EPIC-21 (role admin), EPIC-22 (audit).
- **Priority.** M · **Complexity.** L
- **Definition of Done.** Global DoD + a reusable policy/attribute convention; a negative (denied) test
  per protected endpoint is a merge requirement.

**Technical tasks.**
- `T-01.7.1a` Permission catalog (`resource.action`) as a typed constant set; seed default roles.
- `T-01.7.1b` Authorization policy provider + `RequirePermission("x.y")` endpoint filter for Minimal APIs.
- `T-01.7.1c` Claims transformation to load permissions into the principal.
- `T-01.7.1d` Frontend `usePermission()` hook + `<Can permission>` affordance-hiding component.

**QA tasks.**
- `Q-01.7.1a` Integration: allowed vs denied per representative endpoint.
- `Q-01.7.1b` Security: direct-API call bypassing UI still enforced.
- `Q-01.7.1c` Architecture (`NetArchTest`): every protected endpoint declares a permission.

---

#### FEAT-01.8 — Row-scoping

**STORY-01.8.1 — Enforce data row-scoping server-side**

> *As a* platform, *I want* every query and command scoped to the caller's Supplier/Organization (or
> Ministry read-only cross-org, or global admin), *so that* users can never read or mutate data outside
> their scope.

- **Description.** Suppliers see only their `SupplierId`; procurement/evaluators are scoped to their
  `OrganizationId`; ministry is read-only cross-organization; admin is global. Scope derives from token
  claims and is applied at the Application/Infrastructure layer (global query filters + explicit guards),
  never from client input.
- **Business value.** Confidentiality and tenant isolation (BR-09, BR-19).
- **Acceptance criteria.**
  - **AC1 — Given** a supplier user, **When** they request another supplier's resource by id, **Then**
    the API returns `404/403` (not the data) and audits the attempt.
  - **AC2 — Given** a procurement officer, **When** they list RFQs, **Then** only their organization's
    RFQs are returned.
  - **AC3 — Given** a ministry viewer, **When** they access any org's aggregate data, **Then** it is
    read-only and write attempts are refused.
  - **AC4 — Given** any list endpoint, **When** results are produced, **Then** scoping is applied in the
    query (not post-filtered in the UI).
- **Dependencies.** FEAT-01.1/01.7.
- **Priority.** M · **Complexity.** L
- **Definition of Done.** Global DoD + scope enforced in queries; cross-scope negative tests present for
  each scoped aggregate.

**Technical tasks.**
- `T-01.8.1a` Scope context service reading claims (SupplierId/OrganizationId/role).
- `T-01.8.1b` EF Core global query filters + explicit command guards; ministry read-only guard.
- `T-01.8.1c` Standard `NotFound`-over-`Forbid` policy to avoid resource-existence leakage where apt.

**QA tasks.**
- `Q-01.8.1a` Integration: cross-supplier and cross-org access denied; ministry write denied.
- `Q-01.8.1b` Architecture test: scoped aggregates cannot be queried without the scope filter.

---

#### FEAT-01.9 — External IdP swappability *(C)*

**STORY-01.9.1 — Abstract identity behind a provider seam.** Keep authorization semantics
(permission claims + scope) stable while allowing the authentication provider to become Keycloak/Entra.
AC: swapping the auth provider requires no change to policy/scoping code; token claims contract
documented. Priority C · Complexity M. Tasks: define `IIdentityProvider` seam; document claim contract;
config toggle. QA: contract test on claim shape.

---

#### FEAT-01.10 — Authentication audit events

**STORY-01.10.1 — Audit all authentication events.** Login success/failure, lockout, MFA
enrol/verify, token refresh/revoke, password reset are written to **AuditLog** with `correlationId`.
AC: each event type produces exactly one immutable audit entry with actor + outcome. Priority M ·
Complexity S. (Implemented alongside each auth story; verified centrally here.)

---
---

## EPIC-02 — Supplier Registration

**Goal.** Let a prospective supplier self-register into the registry (configurable open vs invite-only),
verify email, and enter onboarding — with duplicate prevention and an Arabic-first, accessible,
RTL-correct form. Creates a **Supplier** (`OnboardingState=Draft`) and its first `supplier_admin` user.

**FRs covered:** `FR-REG-001..007`, `FR-IAM-006`. **Traces to:** BR-01, BR-16. **State machine:**
onboarding `Draft → EmailVerified`. **Roadmap:** Phase 1. **Domain:** Supplier · User.

### Features

| Feature | Name | Priority | Notes |
|---|---|---|---|
| FEAT-02.1 | Self-registration (create Supplier + supplier_admin) | M | `FR-REG-001,005` |
| FEAT-02.2 | Email verification (`Draft → EmailVerified`) | M | `FR-REG-003`, `FR-IAM-006` |
| FEAT-02.3 | Duplicate prevention | M | `FR-REG-004` |
| FEAT-02.4 | Registration mode (open vs invite-only) | M | `FR-REG-002` `[ASSUMPTION]` |
| FEAT-02.5 | Generic legal-identifier capture | M | `FR-REG-006` `[ASSUMPTION]` |
| FEAT-02.6 | Draft retention/cleanup | S | `FR-REG-007` |

---

#### FEAT-02.1 — Self-registration

**STORY-02.1.1 — Register a new supplier organization**

> *As an* anonymous prospective supplier, *I want* to register my organization with my details and a
> password, *so that* I get a supplier account (`supplier_admin`) and can start onboarding.

- **Description.** A public, Arabic-first form collects organization name (AR/EN), primary
  representative name, email, phone, password. On submit, create a **Supplier** in
  `OnboardingState=Draft` and a `supplier_admin` **User**, then trigger email verification. Legal
  identifiers are captured generically (see FEAT-02.5) — **no invented Syrian validation rules**
  `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`.
- **Business value.** Front door to the trusted registry; converts prospects to onboarding suppliers
  (BR-01).
- **Acceptance criteria.**
  - **AC1 — Given** valid, unique details in open-registration mode, **When** the form is submitted,
    **Then** a Supplier (`Draft`) + `supplier_admin` user are created and a verification email is queued;
    the user sees a "check your email" confirmation.
  - **AC2 — Given** the form, **When** rendered in `ar`, **Then** it is RTL, AR-first with an EN toggle,
    with inline Zod validation and accessible error messaging.
  - **AC3 — Given** a weak password, **When** submitted, **Then** it is rejected per policy (FEAT-01.2)
    with a localized reason and no account is created.
  - **AC4 — Given** invite-only mode (FEAT-02.4), **When** a user without a valid invite registers,
    **Then** registration is refused with a clear message.
  - **AC5 — Given** a successful registration, **When** completed, **Then** an audit event records the
    creation with `correlationId` and the Supplier is `Draft`.
- **Dependencies.** EPIC-01 (identity/hashing), FEAT-02.3 (duplicates), FEAT-02.4 (mode), EPIC-15 (email).
- **Priority.** M · **Complexity.** L
- **Definition of Done.** Global DoD + Supplier persisted `Draft`; password hashed; no partial/orphan
  records on failure (transactional).

**Technical tasks.**
- `T-02.1.1a` `Supplier` aggregate skeleton with `OnboardingState` (Draft) + `RegisterSupplier` domain factory.
- `T-02.1.1b` `POST /api/v1/registrations` Minimal API + FluentValidation (name/email/phone/password).
- `T-02.1.1c` `RegisterSupplier` handler: create Supplier + supplier_admin user in one transaction.
- `T-02.1.1d` Trigger verification email (queue durable job) + audit event.
- `T-02.1.1e` React public registration route: RHF+Zod, AR/EN toggle, RTL, submit → confirmation screen.
- `T-02.1.1f` Zod schema shared shape aligned with server validation contract.

**QA tasks.**
- `Q-02.1.1a` Unit: `RegisterSupplier` factory sets Draft + invariants.
- `Q-02.1.1b` Integration: happy create; failure rolls back (no orphan user/supplier).
- `Q-02.1.1c` Component: form validation, AR RTL, axe.
- `Q-02.1.1d` E2E: register → confirmation screen; invite-only refusal path.

---

#### FEAT-02.2 — Email verification

**STORY-02.2.1 — Verify email to advance onboarding**

> *As a* newly registered `supplier_admin`, *I want* to verify my email via a link, *so that* my account
> is trusted and onboarding can progress past `EmailVerified`.

- **Description.** Issue a single-use, time-limited verification token at registration; clicking the
  link transitions `Draft → EmailVerified`. Onboarding cannot progress past `EmailVerified` until
  verified (`FR-IAM-006`). Support resend with rate limiting.
- **Business value.** Verified identity is a precondition of trust (BR-01).
- **Acceptance criteria.**
  - **AC1 — Given** a valid, unexpired, unused token, **When** the link is opened, **Then** the Supplier
    transitions `Draft → EmailVerified`, the event is audited, and the user is guided into onboarding.
  - **AC2 — Given** an expired/used/tampered token, **When** opened, **Then** a localized error is shown
    with a resend option.
  - **AC3 — Given** repeated resend requests, **When** they exceed the limit, **Then** they are
    rate-limited.
  - **AC4 — Given** an unverified supplier, **When** they attempt to move past `EmailVerified`, **Then**
    the domain refuses the transition.
- **Dependencies.** FEAT-02.1, EPIC-15 (email), EPIC-03 (next state).
- **Priority.** M · **Complexity.** M
- **Definition of Done.** Global DoD + single-use/TTL token; state transition audited; resend limited.

**Technical tasks.**
- `T-02.2.1a` Verification token issue/verify (single-use, TTL) tied to Supplier/User.
- `T-02.2.1b` `GET/POST /registrations/verify` endpoint → `Draft → EmailVerified` transition (domain).
- `T-02.2.1c` Resend endpoint + rate limit.
- `T-02.2.1d` React verify landing + resend UI, RTL.

**QA tasks.**
- `Q-02.2.1a` Integration: verify happy; expired/used token; resend rate limit; illegal advance refused.
- `Q-02.2.1b` E2E: register → verify → land in onboarding.

---

#### FEAT-02.3 — Duplicate prevention

**STORY-02.3.1 — Block duplicate suppliers**

> *As the* system, *I want* to block/flag registration when a supplier with the same legal identifier or
> email already exists (normalized), *so that* the registry stays clean.

- **Acceptance criteria.**
  - **AC1 — Given** an email or legal identifier already in use, **When** registration is submitted,
    **Then** it is blocked with a localized, non-enumerating message and an audit note.
  - **AC2 — Given** values differing only by case/whitespace, **When** compared, **Then** they are treated
    as duplicates (normalized comparison).
  - **AC3 — Given** a soft-conflict policy `[ASSUMPTION]`, **When** configured to flag-not-block, **Then**
    the record is created but flagged for reviewer attention.
- **Priority.** M · **Complexity.** M · **Dependencies.** FEAT-02.1.
- **DoD.** Global DoD + normalized uniqueness enforced at DB (unique index) and application layers.

**Technical tasks.** `T` normalized unique indexes (email, legal id); pre-check + DB-constraint handling;
audit. **QA tasks.** `Q` integration duplicate by case/whitespace; concurrent-registration race (unique
constraint wins).

---

#### FEAT-02.4 — Registration mode (open vs invite-only) *(M, [ASSUMPTION])*

**STORY-02.4.1 — Configure open vs invite-only registration.** Admin setting toggles open
self-registration (default) vs invite-only; invite-only requires a valid invitation token.
`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. AC: mode switch changes registration behavior without
deploy; invite tokens single-use/expiring. Priority M · Complexity M. Tasks: setting in EPIC-21; invite
token issuance; gate in registration handler. QA: both modes; invalid invite refused.

---

#### FEAT-02.5 — Generic legal-identifier capture *(M, [ASSUMPTION])*

**STORY-02.5.1 — Capture legal identifiers generically.** Registration/profile capture registration
number, tax id, etc. as generic strings with **no invented Syrian validation**; format hints are
configurable. `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. AC: fields optional/required per config;
no hard-coded national validation. Priority M · Complexity S.

---

#### FEAT-02.6 — Draft retention/cleanup *(S)*

**STORY-02.6.1 — Clean up abandoned draft registrations.** A scheduled (Hangfire) job removes/anonymizes
unverified `Draft` registrations after a configurable window, audited (`FR-ADM-011`). AC: only unverified
Drafts past the window are affected; action audited; window configurable. Priority S · Complexity S.

---
---

## EPIC-03 — Onboarding

**Goal.** Take a verified supplier through the full onboarding state machine to a trusted, `Active`
registry member: profile completeness gating, submission, reviewer approve/reject/request-info loops,
post-approval lifecycle, and an approval → ERP supplier-master sync event — all audited and
permission-guarded.

**FRs covered:** `FR-ONB-001..012`. **Traces to:** BR-01, BR-08, BR-11, BR-13, BR-15. **State machine:**
`EmailVerified → ProfileInProgress → Submitted → UnderReview → (InfoRequested → Resubmitted →
UnderReview)* → Approved | Rejected`; post-approval `Active ↔ Suspended → Deactivated`. **Roadmap:**
Phase 2. **Domain:** Supplier (`OnboardingState`).

### Features

| Feature | Name | Priority | Notes |
|---|---|---|---|
| FEAT-03.1 | Profile completeness checklist & submission | M | `FR-ONB-001,002,003` |
| FEAT-03.2 | Reviewer intake & decisioning (approve/reject/request-info) | M | `FR-ONB-004,005,007,008` |
| FEAT-03.3 | Info-request / resubmit loop | M | `FR-ONB-006` |
| FEAT-03.4 | Post-approval lifecycle (Active/Suspended/Deactivated) | M | `FR-ONB-009,010` |
| FEAT-03.5 | Approval → ERP supplier-master sync event (Outbox) | M | `FR-ONB-007`, `FR-INT-003` |
| FEAT-03.6 | Reviewer work queue (filter/assign/SLA) | S | `FR-ONB-012` |
| FEAT-03.7 | Onboarding transition audit | M | `FR-ONB-011` |

---

#### FEAT-03.1 — Profile completeness checklist & submission

**STORY-03.1.1 — Complete profile and submit application**

> *As a* `supplier_admin`, *I want* a live completeness checklist that gates submission, *so that* I only
> submit when all required profile sections and documents are satisfied.

- **Description.** Entering the flow transitions `EmailVerified → ProfileInProgress`. A checklist shows
  required profile sections (EPIC-04) and mandatory documents (EPIC-05) with live progress; submission is
  blocked until all required items are satisfied. Submit transitions `ProfileInProgress → Submitted` and
  makes the application read-only to the supplier except where info is requested.
- **Business value.** Fewer incomplete submissions; faster reviews (BR-01, BR-13).
- **Acceptance criteria.**
  - **AC1 — Given** a verified supplier entering onboarding, **When** they open the flow, **Then** state
    becomes `ProfileInProgress` and the checklist reflects live completeness.
  - **AC2 — Given** unmet required items, **When** submit is attempted, **Then** the domain refuses the
    `→ Submitted` transition and the UI shows exactly which items are missing.
  - **AC3 — Given** all required items complete, **When** submit is confirmed, **Then** state becomes
    `Submitted`, the application is read-only, the event is audited, and the reviewer queue is updated.
  - **AC4 — Given** the checklist, **When** rendered, **Then** it is localized, RTL-correct, and
    accessible with clear per-item status chips.
- **Dependencies.** EPIC-02 (EmailVerified), EPIC-04 (profile sections), EPIC-05 (documents).
- **Priority.** M · **Complexity.** L
- **Definition of Done.** Global DoD + completeness computed server-side (UI cannot bypass); read-only
  lock enforced by state.

**Technical tasks.**
- `T-03.1.1a` `OnboardingState` transitions `EmailVerified→ProfileInProgress→Submitted` on the Supplier aggregate.
- `T-03.1.1b` Server-side completeness evaluator (required sections + mandatory doc types satisfied).
- `T-03.1.1c` `POST /suppliers/{ref}/submit-application` (guarded) + validation of completeness.
- `T-03.1.1d` Read-only enforcement on supplier edits once `Submitted`.
- `T-03.1.1e` React onboarding wizard shell + live checklist (TanStack Query) + submit CTA.

**QA tasks.**
- `Q-03.1.1a` Unit: completeness rule; illegal submit refused.
- `Q-03.1.1b` Integration: submit with gaps refused; submit complete → Submitted + read-only.
- `Q-03.1.1c` E2E: complete profile+docs → submit → read-only view.

---

#### FEAT-03.2 — Reviewer intake & decisioning

**STORY-03.2.1 — Review a submission and decide**

> *As an* `onboarding_reviewer`, *I want* to pick up a submission and approve, reject, or request info,
> *so that* only compliant suppliers become `Active`.

- **Description.** Reviewer picks up (`Submitted → UnderReview`), sees all sections/documents, and can
  **approve** (`→ Approved`, moving to `Active` + enqueue ERP sync — FEAT-03.5), **reject**
  (`→ Rejected` with mandatory reason + re-application guidance), or **request info** (FEAT-03.3).
- **Business value.** Compliance gate for the registry (BR-01, BR-13).
- **Acceptance criteria.**
  - **AC1 — Given** a `Submitted` application, **When** a reviewer opens it, **Then** state becomes
    `UnderReview` and the reviewer sees full read access to sections/documents (audited access).
  - **AC2 — Given** `UnderReview`, **When** the reviewer approves, **Then** state becomes `Approved`,
    lifecycle becomes `Active`, an ERP supplier-master sync event is enqueued (Outbox), the supplier is
    notified, and all is audited.
  - **AC3 — Given** `UnderReview`, **When** the reviewer rejects with a reason, **Then** state becomes
    `Rejected`, the supplier is notified with the reason + re-application policy, and it is audited.
  - **AC4 — Given** any decision, **When** the actor lacks `supplier.approve`/review permission, **Then**
    it is refused `403`.
- **Dependencies.** FEAT-03.1, EPIC-01 (permissions), EPIC-05 (docs), EPIC-15 (notify), FEAT-03.5 (Outbox).
- **Priority.** M · **Complexity.** L
- **Definition of Done.** Global DoD + reasons mandatory on reject/request-info; ERP event on approve;
  document access audited.

**Technical tasks.**
- `T-03.2.1a` Reviewer transitions on aggregate (`Submitted→UnderReview→Approved|Rejected`).
- `T-03.2.1b` Endpoints: pick-up, approve, reject (guarded) + validators (reason required).
- `T-03.2.1c` On approve: set `Active`, enqueue Outbox supplier-master event; on reject: notify.
- `T-03.2.1d` Reviewer review screen (sections + documents viewer) — desktop-optimized, RTL.

**QA tasks.**
- `Q-03.2.1a` Integration: pickup→approve→Active + Outbox row; reject requires reason.
- `Q-03.2.1b` Security: non-reviewer denied; document view audited.
- `Q-03.2.1c` E2E: reviewer approves → supplier sees Active + notification.

---

#### FEAT-03.3 — Info-request / resubmit loop

**STORY-03.3.1 — Request changes and resubmit**

> *As an* `onboarding_reviewer`, *I want* to request specific changes with per-section/per-document
> annotations, *and as a* `supplier_admin` *I want* to address them and resubmit, *so that* gaps are
> resolved without rejection.

- **Description.** Reviewer requests changes (`UnderReview → InfoRequested`) with a structured reason and
  per-section/per-document annotations; supplier is notified, edits the flagged items, and resubmits
  (`InfoRequested → Resubmitted → UnderReview`). The loop may repeat and is fully audited.
- **Business value.** Reduces outright rejections; captures a defensible trail (BR-01, BR-08, BR-11).
- **Acceptance criteria.**
  - **AC1 — Given** `UnderReview`, **When** the reviewer requests info with annotations, **Then** state
    becomes `InfoRequested`, only flagged items become editable to the supplier, and the supplier is
    notified with the specifics.
  - **AC2 — Given** `InfoRequested`, **When** the supplier resubmits after addressing items, **Then**
    state becomes `Resubmitted → UnderReview` and the reviewer sees what changed.
  - **AC3 — Given** repeated loops, **When** they occur, **Then** each request/response is individually
    audited with actor, timestamp, and annotations.
- **Dependencies.** FEAT-03.2, EPIC-04/05 (editable items), EPIC-15 (notify).
- **Priority.** M · **Complexity.** L
- **Definition of Done.** Global DoD + partial-edit scoping to flagged items; full loop audit.

**Technical tasks.** `T` transitions + annotation model (per-section/per-document); scoped re-editability;
notify; "what changed" diff for reviewer. **QA tasks.** `Q` integration full loop repeated twice; only
flagged items editable; audit entries per loop; E2E supplier resubmit.

---

#### FEAT-03.4 — Post-approval lifecycle

**STORY-03.4.1 — Suspend, reactivate, and deactivate a supplier**

> *As an* `onboarding_reviewer`/`procurement_manager`/`system_admin`, *I want* to move an approved
> supplier through `Active ↔ Suspended → Deactivated`, *so that* non-compliant suppliers are controlled.

- **Description.** Reversible `Active ↔ Suspended` (with reason) and terminal `Suspended → Deactivated`,
  all permission-guarded and audited. Suspended/Deactivated suppliers cannot be invited to new RFQs or
  submit proposals; existing obligations handled per policy `[ASSUMPTION]`.
- **Business value.** Ongoing compliance control (BR-01, BR-08, BR-09).
- **Acceptance criteria.**
  - **AC1 — Given** an `Active` supplier, **When** suspended with a reason, **Then** state becomes
    `Suspended`, the supplier is blocked from new invitations/proposals, and it is audited.
  - **AC2 — Given** a `Suspended` supplier, **When** reactivated, **Then** state returns to `Active`.
  - **AC3 — Given** a `Suspended` supplier, **When** deactivated, **Then** state becomes terminal
    `Deactivated` and cannot be reactivated.
  - **AC4 — Given** a `Suspended`/`Deactivated` supplier, **When** a buyer attempts to invite them,
    **Then** the domain refuses.
- **Dependencies.** FEAT-03.2, EPIC-08 (invitation gating), EPIC-09 (proposal gating).
- **Priority.** M · **Complexity.** M
- **Definition of Done.** Global DoD + terminal-state irreversibility; invitation/proposal gating enforced.

**Technical tasks.** `T` lifecycle transitions + guards; block invite/propose for non-Active. **QA tasks.**
`Q` integration each transition + illegal reactivation of Deactivated refused; invite of suspended refused.

---

#### FEAT-03.5 — Approval → ERP supplier-master sync event

**STORY-03.5.1 — Enqueue supplier-master sync on approval**

> *As the* system, *I want* to enqueue an approved-supplier-master sync event transactionally on
> approval, *so that* ERPNext eventually receives the approved supplier without blocking the portal.

- **Description.** On `→ Approved`, write an Outbox message (same transaction as the state change)
  representing portal Supplier → ERPNext `Supplier`. Actual dispatch/mapping is EPIC-23; here the event is
  guaranteed to be recorded. Supplier carries `ExternalId?/SyncStatus/LastSyncedAt/RowVersion`.
- **Business value.** Reliable eventual ERP sync; portal never blocks on ERP (BR-02, BR-15).
- **Acceptance criteria.**
  - **AC1 — Given** an approval, **When** it commits, **Then** exactly one Outbox supplier-master event
    exists in the same transaction (atomic with the state change).
  - **AC2 — Given** ERP is down, **When** approval happens, **Then** approval still succeeds and the event
    remains pending (`SyncStatus=Pending`).
  - **AC3 — Given** the Supplier, **When** viewed by staff, **Then** ERP mapping fields are read-only.
- **Dependencies.** FEAT-03.2, EPIC-23 (dispatch), EPIC-25 (telemetry).
- **Priority.** M · **Complexity.** M
- **Definition of Done.** Global DoD + transactional Outbox guarantee (integration test proves atomicity).

**Technical tasks.** `T` Outbox table + transactional write in approve handler; `SyncStatus` on Supplier.
**QA tasks.** `Q` integration atomic write; rollback-on-failure leaves no Outbox row; ERP-down still approves.

---

#### FEAT-03.6 — Reviewer work queue *(S)*

**STORY-03.6.1 — Triaged review queue with SLA/aging.** Reviewer queue supports filter, assignment, and
SLA/age indicators for pending reviews. AC: queue shows age/SLA; assignable; filter by state; scoped.
Priority S · Complexity M. Tasks: queue query + assignment; UI with aging chips. QA: aging/SLA calc;
assignment.

---

#### FEAT-03.7 — Onboarding transition audit *(M)*

**STORY-03.7.1 — Audit every onboarding transition.** Every transition records actor, timestamp,
from→to, reason, `correlationId`; illegal transitions are rejected by the domain. AC: one immutable audit
entry per transition; illegal transition → domain error + no audit of success. Priority M · Complexity S.

---
---

## EPIC-04 — Supplier Profile

**Goal.** Let suppliers build and maintain a rich, ERP-mappable profile (legal info, addresses,
contacts/representatives, branches, bank accounts, category links), manage delegated users, and keep
Arabic/English content correct — feeding onboarding completeness and future ERP sync.

**FRs covered:** `FR-PROF-001..011`. **Traces to:** BR-09, BR-10, BR-13, BR-14, BR-15, BR-18. **Roadmap:**
Phase 2. **Domain:** Supplier — SupplierProfile, LegalInfo(VO), Address[], Contact[], Representative[],
Branch[], BankAccount[], CategoryLink[].

> **Build status (2026-08-27, verified against code, not self-reported):** the "✅" in the epic index
> table above means this epic's stories are **fully written out below** — it is a documentation-
> completeness marker, not an implementation-status one, and it is accurate: every story below is
> fully specified. Actual implementation is **partial**. `Supplier.cs` today has only
> `DisplayNameAr/En, RegistrationNumber, TaxId, AddressLine, City, Country, CurrencyCode` — a single
> flat address, no `Description`/`Logo`/`Website`/type/group, and none of `LegalInfo`, `Address[]`,
> `Contact[]`, `Branch[]`, `BankAccount[]`, `CategoryLink[]`, delegated `supplier_user` management, or
> `SyncStatus`/`LastSyncedAt` exist. See the per-feature status below; MSP-51..56 (Jira) scope the
> remaining work.

### Features

| Feature | Name | Priority | Notes | Built? |
|---|---|---|---|---|
| FEAT-04.1 | Core profile (names AR/EN, type/group, currency, logo) | M | `FR-PROF-001,011` | Partial — AR/EN names, currency, one flat address done; `Description`/`Logo`/`Website`/type/group not done (MSP-51) |
| FEAT-04.2 | LegalInfo VO (generic) | M | `FR-PROF-002` `[ASSUMPTION]` | Not built (MSP-51) |
| FEAT-04.3 | Addresses (HQ/billing/branch) | M | `FR-PROF-003` | Not built — today's one `AddressLine` field is not a multi-valued `Address[]` (MSP-52) |
| FEAT-04.4 | Contacts & Representatives (primary designate) | M | `FR-PROF-004` | Partial — one primary `Representative` from registration exists; no additional `Contact[]` (MSP-52) |
| FEAT-04.5 | Branches | S | `FR-PROF-005` | Not built (MSP-53) |
| FEAT-04.6 | Bank accounts (generic) | M | `FR-PROF-006` `[ASSUMPTION]` | Not built (MSP-53) |
| FEAT-04.7 | Category links to buyer Category tree | M | `FR-PROF-007` | Not built — blocked on Category reference data, which also doesn't exist yet (MSP-54) |
| FEAT-04.8 | Delegated `supplier_user` management | M | `FR-PROF-008` | Not built (MSP-55) |
| FEAT-04.9 | Post-approval compliance-critical edits re-trigger review/sync | S | `FR-PROF-009` `[ASSUMPTION]` | Not built (MSP-56) |
| FEAT-04.10 | ERP mapping fields (read-only to staff) | M | `FR-PROF-010` | Not built — only `ExternalId` exists; no `SyncStatus`/`LastSyncedAt` (MSP-56) |

---

#### FEAT-04.1 — Core profile

**STORY-04.1.1 — Maintain core supplier profile**

> *As a* `supplier_admin`/`supplier_user`, *I want* to maintain core profile fields in Arabic and
> English, *so that* buyers and reviewers see accurate, bilingual organization information.

- **Description.** Legal/trade name (AR+EN), description, logo, website, supplier type/group, default
  currency. AR/EN inputs render RTL/LTR correctly; numeric fields use tabular numerals.
- **Business value.** Accurate, bilingual identity used across onboarding, RFQ invitations, ERP sync
  (BR-14, BR-10).
- **Acceptance criteria.**
  - **AC1 — Given** the profile form, **When** a supplier edits and saves valid data, **Then** it
    persists, updates completeness, and is audited.
  - **AC2 — Given** AR and EN name fields, **When** entered, **Then** each renders in the correct
    direction and both are stored.
  - **AC3 — Given** a logo upload, **When** provided, **Then** it is validated (type/size) and stored via
    `IFileStorage`.
  - **AC4 — Given** invalid input, **When** saved, **Then** inline localized validation blocks it.
- **Dependencies.** EPIC-02, EPIC-05 (IFileStorage for logo), EPIC-21 (currency ref data).
- **Priority.** M · **Complexity.** M
- **Definition of Done.** Global DoD + AR/EN persisted; completeness recomputed; optimistic concurrency
  via `RowVersion`.

**Technical tasks.** `T` SupplierProfile entity + fields; `PUT /suppliers/{ref}/profile` + validators;
logo upload via IFileStorage; React profile section (RHF+Zod, AR/EN). **QA tasks.** `Q` integration
save/validate; component RTL + axe; concurrency conflict surfaced.

---

#### FEAT-04.2 — LegalInfo VO *(M, [ASSUMPTION])*

**STORY-04.2.1 — Manage legal information generically.** Registration number, tax id, incorporation
date, legal form captured as a generic value object — **no invented Syrian rules**
`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. AC: fields optional/required per config; changes
audited; no national-format validation hard-coded. Priority M · Complexity S. Tasks: LegalInfo VO;
config-driven requiredness. QA: validation is generic; audit on change.

---

#### FEAT-04.3 — Addresses

**STORY-04.3.1 — Manage multiple addresses.** HQ/billing/branch addresses with type, region (ref data),
geo-fields. AC: add/edit/remove; at least one required for submission `[ASSUMPTION]`; region from
reference data; RTL forms. Priority M · Complexity M. Tasks: Address entity + CRUD endpoints; region
lookup; UI list/editor. QA: integration CRUD; scoping; component RTL.

---

#### FEAT-04.4 — Contacts & Representatives

**STORY-04.4.1 — Manage contacts/representatives and designate primary.** Multiple contacts and
representatives with roles; exactly one primary representative. AC: designate primary (exactly one);
add/edit/remove; primary shown on onboarding/invitations. Priority M · Complexity M. Tasks: Contact/
Representative entities; primary-uniqueness invariant; UI. QA: invariant (single primary); CRUD.

---

#### FEAT-04.6 — Bank accounts *(M, [ASSUMPTION])*

**STORY-04.6.1 — Manage bank accounts generically.** Bank, IBAN/account no., currency, holder captured
generically for future ERP `accounts` mapping `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. AC:
add/edit/remove; sensitive fields access-controlled + audited; no invented validation. Priority M ·
Complexity M. Tasks: BankAccount entity; masked display; access control. QA: access control; audit on
view of sensitive fields.

---

#### FEAT-04.7 — Category links

**STORY-04.7.1 — Link supplier to buyer category tree.** Suppliers select the goods/services categories
they provide from the buyer **Category** tree (ref data). AC: multi-select from tree; drives invitation
suggestions (EPIC-08) and search (EPIC-20); localized labels. Priority M · Complexity M. Tasks:
CategoryLink; tree picker; index for search. QA: integration link/unlink; suggestions use links.

---

#### FEAT-04.8 — Delegated user management

**STORY-04.8.1 — Invite and manage delegated supplier users**

> *As a* `supplier_admin`, *I want* to invite and manage `supplier_user` accounts under my `SupplierId`
> with scoped permissions, *so that* my team can collaborate without over-granting access.

- **Description.** `supplier_admin` invites/disables `supplier_user` accounts scoped to the same
  `SupplierId`, with a limited permission subset. Invited users complete account setup (password) via a
  secure link.
- **Business value.** Team collaboration with least privilege (BR-09, BR-14).
- **Acceptance criteria.**
  - **AC1 — Given** a `supplier_admin`, **When** they invite a user by email, **Then** a scoped
    `supplier_user` invite is created and an invite email queued; the new user belongs only to that
    `SupplierId`.
  - **AC2 — Given** a delegated user, **When** they act, **Then** they are row-scoped to the supplier and
    cannot exceed granted permissions.
  - **AC3 — Given** a `supplier_admin`, **When** they disable a user, **Then** that user's access is
    immediately revoked and audited.
- **Dependencies.** EPIC-01 (identity/scoping), EPIC-15 (email).
- **Priority.** M · **Complexity.** M
- **Definition of Done.** Global DoD + scope confined to `SupplierId`; immediate revoke.

**Technical tasks.** `T` invite/disable endpoints; scoped role assignment; invite-accept flow. **QA
tasks.** `Q` integration invite→accept→scoped; disable revokes; cross-supplier escalation denied.

---

#### FEAT-04.9 — Post-approval compliance-critical edits *(S, [ASSUMPTION])*

**STORY-04.9.1 — Re-trigger review/sync on sensitive edits.** Post-approval edits to legal id, bank, or
category may re-trigger review and/or an ERP sync event `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`.
AC: editing a flagged field routes for re-review and/or enqueues sync; configurable. Priority S ·
Complexity M.

---

#### FEAT-04.10 — ERP mapping fields *(M)*

**STORY-04.10.1 — Surface ERP mapping fields read-only.** Supplier carries `ExternalId?`, `SyncStatus`,
`LastSyncedAt`, `RowVersion`; shown read-only to staff; concurrency optimistic via `RowVersion`. AC:
fields visible read-only to staff, hidden from suppliers; concurrency conflict prompts. Priority M ·
Complexity S.

---
---

## EPIC-05 — Documents

**Goal.** A managed document lifecycle from required → uploaded → reviewed → approved, with time-based
expiry, secure storage via `IFileStorage`, virus scanning, versioning, and fully audited access —
feeding onboarding completeness and compliance.

**FRs covered:** `FR-DOC-001..009`. **Traces to:** BR-08, BR-10, BR-11, BR-13, BR-16, BR-19. **State
machine:** `Required → Uploaded → UnderReview → Approved | Rejected(reason)`; time-based `Approved →
ExpiringSoon → Expired`. **Roadmap:** Phase 2. **Domain:** SupplierDocument / Document · DocumentType.

### Features

| Feature | Name | Priority | Notes |
|---|---|---|---|
| FEAT-05.1 | Required document set from DocumentType | M | `FR-DOC-001` |
| FEAT-05.2 | Secure upload with validation + virus scan | M | `FR-DOC-002,003` |
| FEAT-05.3 | Issue/expiry capture | M | `FR-DOC-004` `[ASSUMPTION]` |
| FEAT-05.4 | Reviewer document review (approve/reject) | M | `FR-DOC-005` |
| FEAT-05.5 | Expiry lifecycle job (ExpiringSoon/Expired) | M | `FR-DOC-006` |
| FEAT-05.6 | Versioned re-upload | M | `FR-DOC-007` |
| FEAT-05.7 | Authorized, audited download | M | `FR-DOC-008` |
| FEAT-05.8 | Localized document list (chips, countdowns) | S | `FR-DOC-009` |

---

#### FEAT-05.1 — Required document set

**STORY-05.1.1 — Derive required documents from configurable types**

> *As a* `supplier_admin`, *I want* the required document set derived from configurable **DocumentType**
> reference data, *so that* I always know exactly what to provide.

- **Acceptance criteria.**
  - **AC1 — Given** configured DocumentTypes marked required, **When** a supplier opens documents,
    **Then** each shows state `Required` until uploaded.
  - **AC2 — Given** admin changes to required types, **When** applied, **Then** supplier checklists reflect
    the change (for not-yet-submitted suppliers) `[ASSUMPTION]` on retroactivity.
  - **AC3 — Given** the list, **When** rendered, **Then** required vs optional are grouped and localized.
- **Priority.** M · **Complexity.** S · **Dependencies.** EPIC-21 (DocumentType ref data).
- **DoD.** Global DoD + required set computed from ref data.

**Technical tasks.** `T` DocumentType ref data link; required-set query. **QA tasks.** `Q` required set
reflects config; grouping.

---

#### FEAT-05.2 — Secure upload with validation + virus scan

**STORY-05.2.1 — Upload a document securely**

> *As a* `supplier_admin`/`supplier_user`, *I want* to upload a document against a DocumentType with
> validation and malware scanning, *so that* only safe, valid files enter the system.

- **Description.** Upload transitions `Required → Uploaded`. Client + server validate type/size/MIME; a
  virus/malware scan runs **before acceptance**; files persist via `IFileStorage` (local dev /
  S3-compatible prod) and are **never** served from a public bucket — access is authorized and
  time-limited.
- **Business value.** Compliance data captured safely (BR-13, BR-19).
- **Acceptance criteria.**
  - **AC1 — Given** a valid file, **When** uploaded, **Then** MIME/size/type pass, the scan is clean, the
    file is stored via `IFileStorage`, and the document becomes `Uploaded` (audited).
  - **AC2 — Given** an invalid type/oversize file, **When** uploaded, **Then** it is rejected with a
    localized reason and nothing is stored.
  - **AC3 — Given** an infected file, **When** scanned, **Then** it is quarantined/rejected, the upload
    fails, and the event is audited.
  - **AC4 — Given** stored files, **When** requested, **Then** they are only reachable via authorized,
    time-limited URLs (never a public bucket).
- **Dependencies.** `IFileStorage` (P0), scanner integration, EPIC-22 (audit).
- **Priority.** M · **Complexity.** L
- **Definition of Done.** Global DoD + scan-before-accept; no public-bucket exposure; storage-provider
  independence proven (local + S3 in tests).

**Technical tasks.**
- `T-05.2.1a` `IFileStorage` implementations (local disk dev, S3-compatible/MinIO prod).
- `T-05.2.1b` Upload endpoint with server MIME/size validation; streaming to storage.
- `T-05.2.1c` Virus-scan seam (scan before commit; quarantine on hit).
- `T-05.2.1d` Time-limited authorized access URL generation.
- `T-05.2.1e` React uploader (progress, validation, RTL) tied to DocumentType.

**QA tasks.**
- `Q-05.2.1a` Integration: valid upload → Uploaded; oversize/type rejected; infected quarantined.
- `Q-05.2.1b` Security: stored file not reachable without authorization; URL expiry enforced.
- `Q-05.2.1c` Storage: same behavior on local and S3 provider.

---

#### FEAT-05.3 — Issue/expiry capture *(M, [ASSUMPTION])*

**STORY-05.3.1 — Capture issue/expiry dates.** Optional issue/expiry dates per document drive lifecycle
timers; which types expire is `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. AC: dates optional per
type config; expiry feeds FEAT-05.5; locale-aware date input. Priority M · Complexity S.

---

#### FEAT-05.4 — Reviewer document review

**STORY-05.4.1 — Review and decide on a document**

> *As an* `onboarding_reviewer`, *I want* to review a document and approve or reject it with a reason,
> *so that* only valid documents count toward compliance.

- **Acceptance criteria.**
  - **AC1 — Given** an `Uploaded` document, **When** the reviewer opens it, **Then** state becomes
    `UnderReview` and the view/download is audited.
  - **AC2 — Given** `UnderReview`, **When** approved, **Then** state becomes `Approved` and completeness
    updates.
  - **AC3 — Given** `UnderReview`, **When** rejected, **Then** a reason is required, state becomes
    `Rejected`, and the profile is flagged incomplete + supplier notified.
- **Priority.** M · **Complexity.** M · **Dependencies.** FEAT-05.2, EPIC-15.
- **DoD.** Global DoD + reason mandatory on reject; completeness/flagging updated.

**Technical tasks.** `T` review transitions; reject-reason; completeness/flag recompute. **QA tasks.**
`Q` integration approve/reject; reject flags profile; access audited.

---

#### FEAT-05.5 — Expiry lifecycle job

**STORY-05.5.1 — Auto-move documents through expiry states**

> *As the* system, *I want* a scheduled job to move `Approved → ExpiringSoon → Expired`, *so that*
> compliance stays current and suppliers are warned in time.

- **Description.** A Hangfire recurring job transitions `Approved → ExpiringSoon` within a configurable
  window and `ExpiringSoon → Expired` at expiry, notifying the supplier and flagging the profile
  incomplete on expiry.
- **Business value.** Proactive compliance; no silent lapses (BR-11, BR-13).
- **Acceptance criteria.**
  - **AC1 — Given** an Approved doc nearing expiry (within window), **When** the job runs, **Then** it
    becomes `ExpiringSoon` and the supplier is notified (de-duplicated).
  - **AC2 — Given** an expired doc, **When** the job runs, **Then** it becomes `Expired`, the profile is
    flagged incomplete, and the supplier is notified.
  - **AC3 — Given** job-host restart, **When** it recovers, **Then** transitions are not skipped or
    duplicated (durable/idempotent).
- **Dependencies.** FEAT-05.3, Hangfire (P0), EPIC-15.
- **Priority.** M · **Complexity.** M
- **Definition of Done.** Global DoD + idempotent job; configurable window; audit on each transition.

**Technical tasks.** `T` recurring job; window config; idempotent transition + notify + flag. **QA
tasks.** `Q` integration (clock-controlled) → ExpiringSoon/Expired; idempotency on re-run; dedup notify.

---

#### FEAT-05.6 — Versioned re-upload

**STORY-05.6.1 — Re-upload a new version.** Suppliers re-upload a new version of an expired/rejected
document; version history retained and auditable. AC: new version supersedes but history preserved;
re-upload resets state to `Uploaded`; history auditable. Priority M · Complexity M. Tasks: version chain;
history view. QA: version chain + audit; latest drives completeness.

---

#### FEAT-05.7 — Authorized, audited download

**STORY-05.7.1 — Download only within scope, always audited.** Documents downloadable only by authorized
actors within scope; every view/download audited as an access event (`FR-AUD-006`). AC: out-of-scope
download denied; each access audited; time-limited URL. Priority M · Complexity M.

---

#### FEAT-05.8 — Localized document list *(S)*

**STORY-05.8.1 — Document list with chips and countdowns.** List shows state chips, expiry countdowns,
required-vs-optional grouping, localized + RTL. AC: countdowns locale-aware; chips reflect state; RTL.
Priority S · Complexity S.

---
---

## EPIC-06 — Offerings

**Goal.** Let suppliers publish a catalog of goods/services (offerings) mapped to the buyer Category
tree and UoM, with flexible JSONB attributes, visibility gated to Active suppliers, and discoverability
that informs RFQ invitations and search.

**FRs covered:** `FR-OFF-001..005`. **Traces to:** BR-04, BR-14, BR-16, BR-17. **Roadmap:** Phase 3.
**Domain:** Supplier → Offering[] (JSONB attributes).

### Features

| Feature | Name | Priority | Notes |
|---|---|---|---|
| FEAT-06.1 | Offering CRUD (AR/EN, category, UoM, price, attributes) | M | `FR-OFF-001,002` |
| FEAT-06.2 | Flexible per-category JSONB attributes | S | `FR-OFF-003` |
| FEAT-06.3 | Discoverability for procurement / invitation suggestions | S | `FR-OFF-004` |
| FEAT-06.4 | Lifecycle-gated visibility (Active only) | M | `FR-OFF-005` |

---

#### FEAT-06.1 — Offering CRUD

**STORY-06.1.1 — Create and manage offerings**

> *As a* `supplier_admin`/`supplier_user`, *I want* to create, edit, and deactivate offerings with
> bilingual names, category, UoM, indicative price, and attributes, *so that* buyers can discover what
> I provide.

- **Description.** Create/edit/deactivate an Offering (name AR/EN, description, category from the buyer
  tree, UnitOfMeasure, optional indicative price + currency, flexible attributes). Links to Category and
  UoM reference data.
- **Business value.** Feeds discovery, invitation suggestions, and buyer search (BR-14, BR-04, BR-17).
- **Acceptance criteria.**
  - **AC1 — Given** valid data, **When** an offering is created, **Then** it persists linked to a valid
    category + UoM, with AR/EN names, and is audited.
  - **AC2 — Given** an offering, **When** deactivated, **Then** it no longer surfaces to buyers but is
    retained for history.
  - **AC3 — Given** price + currency, **When** shown, **Then** numerals are tabular and currency
    localized.
  - **AC4 — Given** invalid category/UoM, **When** saved, **Then** it is rejected with a localized error.
- **Dependencies.** EPIC-04 (category links), EPIC-21 (Category/UoM/Currency ref data).
- **Priority.** M · **Complexity.** M
- **Definition of Done.** Global DoD + category/UoM referential integrity; deactivation preserves history.

**Technical tasks.** `T` Offering entity (JSONB attributes column); CRUD endpoints + validators; React
offering editor/list (AR/EN, RTL, tabular numerals). **QA tasks.** `Q` integration CRUD + referential
integrity; component RTL + axe; deactivation hides from buyer views.

---

#### FEAT-06.2 — Flexible JSONB attributes *(S)*

**STORY-06.2.1 — Typed flexible attributes per category.** Per-category attribute schemas stored as JSONB
and rendered with typed inputs (text/number/enum/date). AC: attribute schema by category; typed inputs;
validation per type; stored as JSONB. Priority S · Complexity M. Tasks: attribute-schema model; dynamic
form renderer. QA: type validation; JSONB round-trip.

---

#### FEAT-06.3 — Discoverability / invitation suggestions *(S)*

**STORY-06.3.1 — Surface offerings to procurement.** Offerings are discoverable in supplier search and
inform RFQ invitation suggestions (matching RFQ item categories). AC: search by offering/category returns
Active suppliers; suggestions rank by category/offering match. Priority S · Complexity M. (Consumed by
EPIC-08 FEAT-08.2 and EPIC-20.)

---

#### FEAT-06.4 — Lifecycle-gated visibility *(M)*

**STORY-06.4.1 — Only Active suppliers' offerings surface.** Offering visibility respects supplier
lifecycle; only `Active` suppliers' offerings surface to buyers. AC: suspended/deactivated/not-yet-active
supplier offerings hidden from buyer discovery; supplier still sees own. Priority M · Complexity S. QA:
negative test — suspended supplier offerings absent from buyer search/suggestions.

---
---

## EPIC-07 — RFQ (authoring & lifecycle)

**Goal.** Enable procurement to author RFQs (items, requirements, attachments, bound evaluation
template), pass internal review/approval, publish, and drive the full RFQ state machine with
state-gated editing, opaque public references, timeline automation, and cancel-with-reason — all
permission-guarded and audited.

**FRs covered:** `FR-RFQ-001..013`. **Traces to:** BR-03, BR-04, BR-06, BR-07, BR-08. **State machine:**
`Draft → InternalReview → Approved → Published → SubmissionOpen → SubmissionClosed → UnderEvaluation →
Clarification* → Shortlisting → Recommendation → AwardApproval → Awarded → Completed`; `Cancelled` from
any pre-Awarded state. **Roadmap:** Phase 4 (later stages advanced in P5–P8). **Domain:** RFQ — RfqItem[],
Requirement[], Attachment[], Invitation[], Clarification[], EvaluationTemplateRef, Timeline, RfqState.

### Features

| Feature | Name | Priority | Notes |
|---|---|---|---|
| FEAT-07.1 | RFQ authoring (header, items, requirements) | M | `FR-RFQ-001,002` |
| FEAT-07.2 | RFQ attachments | M | `FR-RFQ-003` |
| FEAT-07.3 | Bind evaluation template | M | `FR-RFQ-004` |
| FEAT-07.4 | Internal review & approval | M | `FR-RFQ-005` `[ASSUMPTION]` |
| FEAT-07.5 | Publish RFQ | M | `FR-RFQ-006` |
| FEAT-07.6 | Submission window automation | M | `FR-RFQ-007` |
| FEAT-07.7 | Lifecycle progression (post-submission stages) | M | `FR-RFQ-008,009` |
| FEAT-07.8 | Cancel RFQ with reason | M | `FR-RFQ-010` |
| FEAT-07.9 | Opaque public reference | M | `FR-RFQ-011` |
| FEAT-07.10 | State-gated editing | M | `FR-RFQ-012` |
| FEAT-07.11 | ERP mapping fields | S | `FR-RFQ-013` |

---

#### FEAT-07.1 — RFQ authoring

**STORY-07.1.1 — Author an RFQ with items and requirements**

> *As a* `procurement_officer`, *I want* to create an RFQ with a header, line items, and requirements,
> *so that* I can solicit structured proposals from suppliers.

- **Description.** Create `Draft` RFQ (title, description, buying `OrganizationId`, currency, timeline:
  publish, submission open/close, target award). Add **RfqItem[]** (description, category, quantity,
  UoM, optional target/budget) and **Requirement[]** (technical/compliance). Scoped to the officer's org.
- **Business value.** Structured solicitation is the basis of comparable proposals (BR-03).
- **Acceptance criteria.**
  - **AC1 — Given** valid header + timeline, **When** created, **Then** an RFQ exists in `Draft` scoped to
    the officer's org, with an opaque public reference (FEAT-07.9), audited.
  - **AC2 — Given** items/requirements, **When** added, **Then** each item references a valid category/UoM
    and totals/quantities validate.
  - **AC3 — Given** an inconsistent timeline (e.g. close before open), **When** saved, **Then** it is
    rejected with a localized error.
  - **AC4 — Given** a different org's officer, **When** they try to open this RFQ, **Then** it is denied.
- **Dependencies.** EPIC-21 (org/category/UoM/currency), EPIC-01 (scoping).
- **Priority.** M · **Complexity.** L
- **Definition of Done.** Global DoD + timeline invariants; org-scoped; public ref generated.

**Technical tasks.** `T` RFQ aggregate + RfqItem/Requirement/Timeline; create/update endpoints +
validators (timeline consistency); React authoring workspace (items grid, requirements), RTL. **QA
tasks.** `Q` unit timeline invariants; integration create + scoping; E2E author draft.

---

#### FEAT-07.2 — RFQ attachments

**STORY-07.2.1 — Attach specifications.** Attach RFQ documents/specifications via `IFileStorage`. AC:
upload validated (type/size/scan); listed; authorized access. Priority M · Complexity S. (Reuses EPIC-05
storage.) QA: upload/list/authorized access.

---

#### FEAT-07.3 — Bind evaluation template

**STORY-07.3.1 — Bind a weighted evaluation template.** Bind an **EvaluationTemplate**
(`EvaluationTemplateRef`) of weighted criteria (name, weight, max, threshold, scoring type). AC: template
selectable from reusable templates (EPIC-21/EPIC-11); reference frozen once RFQ leaves Draft; drives
evaluation (EPIC-11). Priority M · Complexity M. QA: binding; immutability after Draft.

---

#### FEAT-07.4 — Internal review & approval *(M, [ASSUMPTION])*

**STORY-07.4.1 — Review and approve an RFQ before publish**

> *As a* `procurement_officer`/`procurement_manager`, *I want* an internal review/approval step, *so
> that* RFQs are quality-checked before going out.

- **Description.** Submit for review (`Draft → InternalReview`); an approver approves
  (`InternalReview → Approved`) or returns to `Draft` with comments.
  `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` single approver, configurable hierarchy.
- **Acceptance criteria.**
  - **AC1 — Given** a complete Draft, **When** submitted for review, **Then** state becomes
    `InternalReview` and the approver is notified.
  - **AC2 — Given** `InternalReview`, **When** approved, **Then** state becomes `Approved`; **When**
    returned, **Then** state returns to `Draft` with comments visible and audited.
  - **AC3 — Given** the actor lacks review permission, **When** they attempt it, **Then** `403`.
- **Priority.** M · **Complexity.** M · **Dependencies.** FEAT-07.1, EPIC-01, EPIC-15.
- **DoD.** Global DoD + configurable approver; comments captured; audited.

**Technical tasks.** `T` review transitions + comments; configurable approver setting. **QA tasks.** `Q`
integration approve/return; permission denial.

---

#### FEAT-07.5 — Publish RFQ

**STORY-07.5.1 — Publish an approved RFQ**

> *As a* `procurement_manager`/`procurement_officer`, *I want* to publish an approved RFQ, *so that*
> invited suppliers can view it and respond.

- **Acceptance criteria.**
  - **AC1 — Given** an `Approved` RFQ, **When** published with `rfq.publish`, **Then** state becomes
    `Published`, the action is audited, and downstream invitation/timeline flows unlock.
  - **AC2 — Given** the actor lacks `rfq.publish`, **When** they attempt publish, **Then** `403`.
  - **AC3 — Given** a non-Approved RFQ, **When** publish is attempted, **Then** the domain refuses.
- **Priority.** M · **Complexity.** M · **Dependencies.** FEAT-07.4.
- **DoD.** Global DoD + permission-guarded; only from Approved.

**Technical tasks.** `T` publish transition + `rfq.publish` policy. **QA tasks.** `Q` integration publish
happy/denied/illegal-source.

---

#### FEAT-07.6 — Submission window automation

**STORY-07.6.1 — Open and close the submission window automatically**

> *As the* system, *I want* to open/close the submission window by timeline (with early-close), *so
> that* proposals are only accepted in-window.

- **Description.** Scheduled jobs drive `Published → SubmissionOpen → SubmissionClosed`; buyers may close
  early with a reason. Durable/idempotent (Hangfire).
- **Acceptance criteria.**
  - **AC1 — Given** the timeline, **When** open time passes, **Then** state becomes `SubmissionOpen`.
  - **AC2 — Given** close time passes (or early-close with reason), **Then** state becomes
    `SubmissionClosed` and late proposals are refused by the domain.
  - **AC3 — Given** job-host restart, **When** it recovers, **Then** transitions fire exactly once.
- **Priority.** M · **Complexity.** M · **Dependencies.** FEAT-07.5, Hangfire.
- **DoD.** Global DoD + idempotent scheduling; early-close reason audited.

**Technical tasks.** `T` scheduled transitions; early-close endpoint. **QA tasks.** `Q` clock-controlled
open/close; restart idempotency; late submission refused.

---

#### FEAT-07.7 — Lifecycle progression *(M)*

**STORY-07.7.1 — Progress through evaluation-to-completion stages.** Move `SubmissionClosed →
UnderEvaluation` then through `Clarification* → Shortlisting → Recommendation → AwardApproval → Awarded →
Completed`, each permission-guarded and audited (detailed behavior in EPIC-11/12/13/14). AC: each
transition guarded + audited; out-of-order refused. Priority M · Complexity M. (Advanced across P7–P8.)

---

#### FEAT-07.8 — Cancel RFQ with reason

**STORY-07.8.1 — Cancel an RFQ.** Cancel from any pre-`Awarded` state with a mandatory reason; invited
suppliers notified; state + reason audited. AC: cancel allowed only pre-Awarded; reason mandatory; all
invited suppliers notified. Priority M · Complexity M. QA: cancel from several states; post-Awarded
refused; notifications sent.

---

#### FEAT-07.9 — Opaque public reference *(M)*

**STORY-07.9.1 — Assign an opaque public reference.** RFQ gets an opaque public ref (e.g.
`RFQ-2026-000123`); internal GUIDv7 PKs never appear in URLs. AC: public ref stable + unique; URLs use ref
only. Priority M · Complexity S.

---

#### FEAT-07.10 — State-gated editing *(M)*

**STORY-07.10.1 — Constrain editing by state.** Full edit in `Draft`, restricted in `InternalReview`,
locked after `Published` except addenda (EPIC-10 FEAT). AC: edits refused per state by the domain; addenda
path allowed post-Published. Priority M · Complexity M.

---

#### FEAT-07.11 — ERP mapping fields *(S)*

**STORY-07.11.1 — Carry ERP mapping fields on RFQ.** RFQ carries `ExternalId?`, `SyncStatus`,
`LastSyncedAt`, `RowVersion`. AC: present + concurrency via RowVersion. Priority S · Complexity S.

---
---

## EPIC-08 — Invitations

**Goal.** Let buyers invite Active suppliers to a published RFQ with status tracking, candidate
suggestions, notifications, decline handling, and strict row-scoped visibility so only invited
suppliers can see RFQ detail and propose.

**FRs covered:** `FR-INV-001..007`. **Traces to:** BR-04, BR-08, BR-11, BR-12, BR-17. **Roadmap:** Phase
5. **Domain:** RFQ → Invitation[] (maps to ERPNext `Request for Quotation Supplier`).

### Features

| Feature | Name | Priority | Notes |
|---|---|---|---|
| FEAT-08.1 | Invite suppliers with status tracking | M | `FR-INV-001` |
| FEAT-08.2 | Candidate suggestions from categories/offerings | S | `FR-INV-002` |
| FEAT-08.3 | Invitation notifications with deep link | M | `FR-INV-003` |
| FEAT-08.4 | Decline invitation with reason | S | `FR-INV-004` |
| FEAT-08.5 | Late invite while SubmissionOpen | S | `FR-INV-005` |
| FEAT-08.6 | Invite-only visibility enforcement | M | `FR-INV-006` `[ASSUMPTION]` |
| FEAT-08.7 | Invitation status on buyer dashboard | S | `FR-INV-007` |

---

#### FEAT-08.1 — Invite suppliers

**STORY-08.1.1 — Invite Active suppliers to an RFQ**

> *As a* `procurement_officer`, *I want* to invite one or more Active suppliers to a published RFQ, *so
> that* they can view it and submit proposals.

- **Description.** Create an **Invitation** per supplier with status tracking (invited, viewed,
  responding, submitted, declined). Only `Active` suppliers are invitable (suspended/deactivated refused).
- **Business value.** Controls RFQ participation; basis of proposals (BR-04).
- **Acceptance criteria.**
  - **AC1 — Given** a published RFQ, **When** the officer invites Active suppliers, **Then** an Invitation
    per supplier is created with status `Invited`, each notified, and it is audited.
  - **AC2 — Given** a suspended/deactivated supplier, **When** invited, **Then** the domain refuses.
  - **AC3 — Given** invitation status changes (viewed/responding/submitted/declined), **When** they occur,
    **Then** the buyer sees them update.
- **Dependencies.** EPIC-07 (published RFQ), EPIC-03 (Active suppliers), EPIC-15 (notify).
- **Priority.** M · **Complexity.** M
- **Definition of Done.** Global DoD + Active-only invariant; status lifecycle tracked.

**Technical tasks.** `T` Invitation entity + status; invite endpoint (guarded, Active-only) + notify;
status update hooks. **QA tasks.** `Q` integration invite Active; suspended refused; status transitions;
E2E invite → supplier notified.

---

#### FEAT-08.2 — Candidate suggestions *(S)*

**STORY-08.2.1 — Suggest suppliers by category/offering match.** Suggestions drawn from supplier
categories/offerings matching RFQ items. AC: ranked suggestions; Active-only; officer can accept/adjust.
Priority S · Complexity M. (Consumes EPIC-06 FEAT-06.3.)

---

#### FEAT-08.3 — Invitation notifications *(M)*

**STORY-08.3.1 — Notify invited suppliers with a deep link.** Each invited supplier notified in-app +
email with RFQ summary, timeline, and a deep link. AC: localized notification; deep link opens RFQ detail
(if authorized); durable delivery. Priority M · Complexity S.

---

#### FEAT-08.4 — Decline invitation *(S)*

**STORY-08.4.1 — Decline an invitation.** Supplier may decline with optional reason; declination audited
and buyer-visible. AC: decline sets status `Declined`; reason optional; audited. Priority S · Complexity S.

---

#### FEAT-08.5 — Late invite *(S)*

**STORY-08.5.1 — Invite while SubmissionOpen.** Invitations addable while `SubmissionOpen` (late invite)
with adjusted deadline handling; not after `SubmissionClosed`. AC: late invite allowed only while open;
refused after close. Priority S · Complexity S.

---

#### FEAT-08.6 — Invite-only visibility *(M, [ASSUMPTION])*

**STORY-08.6.1 — Restrict RFQ visibility to invited suppliers.** Only invited suppliers can view RFQ
detail and submit; access row-scoped and enforced server-side. `[ASSUMPTION / REQUIRES BUSINESS
CONFIRMATION]` open vs invite-only visibility. AC: non-invited supplier gets `404/403` on RFQ detail;
enforced at API. Priority M · Complexity M. QA: negative test — non-invited denied.

---

#### FEAT-08.7 — Invitation status on dashboard *(S)*

**STORY-08.7.1 — Show invitation status to buyer.** Buyer RFQ dashboard shows who was invited, viewed,
responded. AC: live status table; scoped. Priority S · Complexity S. (Feeds EPIC-17.)

---
---

## EPIC-09 — Proposals

**Goal.** Let invited suppliers build and submit structured, revisable proposals with draft safety,
line pricing, commercial/technical responses, documents, submission guardrails, withdrawal, and strict
confidentiality — completing the RFQ→Invitation→Proposal triad.

**FRs covered:** `FR-PRP-001..013`. **Traces to:** BR-04, BR-05, BR-06, BR-07, BR-08, BR-09, BR-18,
BR-19. **State machine:** `Draft → Submitted → UnderReview → (ClarificationRequested → Revised →
UnderReview)* → Shortlisted | NotSelected → AwardOffered → Awarded | Declined`; supplier-initiated
`Withdrawn` while `SubmissionOpen`. **Roadmap:** Phase 6. **Domain:** Proposal (one per Supplier per RFQ)
— ProposalItem[], ProposalDocument[], CommercialTerms(VO), TechnicalResponse, Validity, ProposalState.

### Features

| Feature | Name | Priority | Notes |
|---|---|---|---|
| FEAT-09.1 | Start & author a proposal (line pricing) | M | `FR-PRP-001,002` |
| FEAT-09.2 | Commercial terms & technical response | M | `FR-PRP-003` |
| FEAT-09.3 | Proposal documents | M | `FR-PRP-004` |
| FEAT-09.4 | Draft safety (auto-save, private) | M | `FR-PRP-005` |
| FEAT-09.5 | Submit with validation & window guard | M | `FR-PRP-006,007` |
| FEAT-09.6 | Withdraw while open | S | `FR-PRP-008` |
| FEAT-09.7 | Evaluation-intake & outcome transitions | M | `FR-PRP-009,011` |
| FEAT-09.8 | Confidentiality & blindness enforcement | M | `FR-PRP-012` |
| FEAT-09.9 | ERP mapping fields | C | `FR-PRP-013` |

---

#### FEAT-09.1 — Start & author a proposal

**STORY-09.1.1 — Create one proposal per invited RFQ with line pricing**

> *As a* `supplier_admin`/`supplier_user`, *I want* to start exactly one proposal against an invited RFQ
> and price each line item, *so that* I can submit a comparable, structured bid.

- **Description.** Start a **Proposal** (`Draft`) against an invited RFQ; exactly one proposal per Supplier
  per RFQ. Enter **ProposalItem[]** (unit price, quantity, currency, auto line totals + grand total).
- **Business value.** Structured, comparable bids power evaluation and award (BR-04, BR-18).
- **Acceptance criteria.**
  - **AC1 — Given** an invited RFQ, **When** the supplier starts a proposal, **Then** a single `Draft`
    proposal is created (a second start returns the existing one — uniqueness enforced).
  - **AC2 — Given** RFQ line items, **When** the supplier enters unit price + quantity, **Then** line and
    grand totals compute automatically with tabular numerals and the chosen currency.
  - **AC3 — Given** a non-invited supplier, **When** they attempt to start a proposal, **Then** it is
    refused (row-scoping + invitation check).
  - **AC4 — Given** a closed submission window, **When** starting/authoring, **Then** authoring is limited
    per state (submit refused after close — FEAT-09.5).
- **Dependencies.** EPIC-08 (invitation), EPIC-07 (RFQ items), EPIC-01 (scoping).
- **Priority.** M · **Complexity.** L
- **Definition of Done.** Global DoD + one-proposal-per-supplier-per-RFQ invariant (DB unique +
  application); totals correct across currencies.

**Technical tasks.**
- `T-09.1.1a` Proposal aggregate + ProposalItem; unique `(SupplierId, RfqId)` constraint.
- `T-09.1.1b` Start/update endpoints (guarded, invited-only) + validators.
- `T-09.1.1c` Totals computation (line + grand) with currency handling.
- `T-09.1.1d` React proposal workspace: line-item pricing grid, live totals, RTL, tabular numerals.

**QA tasks.**
- `Q-09.1.1a` Unit: uniqueness invariant; totals math (multi-currency).
- `Q-09.1.1b` Integration: start once; second start returns same; non-invited refused.
- `Q-09.1.1c` Component: pricing grid RTL + axe; E2E author draft.

---

#### FEAT-09.2 — Commercial terms & technical response

**STORY-09.2.1 — Provide commercial terms and technical responses**

> *As a* supplier user, *I want* to capture commercial terms and answer technical requirements, *so
> that* my proposal is complete and evaluable.

- **Description.** **CommercialTerms** VO (payment terms, delivery/lead time, incoterm, validity period)
  and a **TechnicalResponse** against RFQ **Requirement[]**.
- **Acceptance criteria.**
  - **AC1 — Given** the proposal, **When** commercial terms are entered, **Then** they validate
    (e.g. validity ≥ min) and persist.
  - **AC2 — Given** RFQ requirements, **When** the supplier responds, **Then** each mandatory requirement
    has a response before submission is allowed (FEAT-09.5).
  - **AC3 — Given** incoterm/currency, **When** selected, **Then** they come from reference data.
- **Priority.** M · **Complexity.** M · **Dependencies.** FEAT-09.1, EPIC-07 requirements, EPIC-21 ref data.
- **DoD.** Global DoD + mandatory-response gating for submit.

**Technical tasks.** `T` CommercialTerms VO + TechnicalResponse entities; validators; UI sections. **QA
tasks.** `Q` integration validation; mandatory-response gating.

---

#### FEAT-09.3 — Proposal documents

**STORY-09.3.1 — Attach proposal documents.** Attach **ProposalDocument[]** (compliance/technical) via
`IFileStorage`. AC: validated + scanned upload; authorized access; listed. Priority M · Complexity S.
(Reuses EPIC-05 storage.) QA: upload/list/authorized access.

---

#### FEAT-09.4 — Draft safety

**STORY-09.4.1 — Never lose proposal work**

> *As a* supplier user, *I want* my draft to auto-save and stay private until I submit, *so that* I never
> lose work and competitors never see it.

- **Description.** Drafts auto-save/persist; drafts are private to the supplier until submitted.
- **Acceptance criteria.**
  - **AC1 — Given** edits, **When** the user pauses or navigates, **Then** the draft is auto-saved (no
    data loss on reload/session end).
  - **AC2 — Given** a `Draft`, **When** anyone outside the supplier tries to read it, **Then** it is
    inaccessible (including buyers) until submitted.
  - **AC3 — Given** a recovered session, **When** the user returns, **Then** the last saved state is
    restored.
- **Priority.** M · **Complexity.** M · **Dependencies.** FEAT-09.1.
- **DoD.** Global DoD + auto-save proven across reload; draft confidentiality enforced server-side.

**Technical tasks.** `T` autosave endpoint/debounce; draft privacy guard. **QA tasks.** `Q` persistence
across reload; buyer cannot read Draft (negative test).

---

#### FEAT-09.5 — Submit with validation & window guard

**STORY-09.5.1 — Submit a validated proposal within the window**

> *As a* `supplier_admin`/`supplier_user`, *I want* to submit my proposal only when complete and while
> the window is open, *so that* my bid is valid and on time.

- **Description.** Pre-submission validation ensures all required RFQ items are priced and mandatory
  technical responses/documents are present. Submit (`Draft → Submitted`) only while `SubmissionOpen`; on/
  after close the domain rejects it. Multi-currency proposals carry a display currency.
- **Business value.** Only valid, timely bids enter evaluation; fairness (BR-04, BR-18).
- **Acceptance criteria.**
  - **AC1 — Given** a complete proposal and an open window, **When** submitted, **Then** state becomes
    `Submitted`, the supplier is notified/confirmed, and it is audited.
  - **AC2 — Given** missing required items/responses/docs, **When** submit is attempted, **Then** it is
    refused with a precise, localized list of gaps.
  - **AC3 — Given** the window is closed, **When** submit is attempted, **Then** the domain refuses (late
    submission impossible), even if the UI is stale.
  - **AC4 — Given** multi-currency lines, **When** submitted, **Then** a display currency is recorded.
- **Dependencies.** FEAT-09.1/09.2/09.3, EPIC-07 window (FEAT-07.6).
- **Priority.** M · **Complexity.** L
- **Definition of Done.** Global DoD + server-side completeness + window enforcement (integration test
  proves late-submit refusal at the domain).

**Technical tasks.** `T` completeness validator; submit transition guarded by window; display-currency
capture; notify. **QA tasks.** `Q` integration submit complete/open; incomplete refused with gaps; closed
window refused; E2E submit before close.

---

#### FEAT-09.6 — Withdraw while open

**STORY-09.6.1 — Withdraw a submitted proposal.** Supplier may withdraw a submitted proposal while
`SubmissionOpen` (`→ Withdrawn`) with reason + audit. AC: withdraw only while open; reason captured;
audited; buyer sees withdrawn. Priority S · Complexity S. QA: withdraw while open; refused after close.

---

#### FEAT-09.7 — Evaluation-intake & outcome transitions *(M)*

**STORY-09.7.1 — Move proposals through review and outcomes.** Buyer moves proposal to `UnderReview` after
close (evaluation intake); outcome transitions `Shortlisted | NotSelected → AwardOffered → Awarded |
Declined` are driven by evaluation/award and permission-guarded (detail in EPIC-11/14). AC: transitions
guarded + audited; only valid sources. Priority M · Complexity M. (Advanced with P7–P8.)

---

#### FEAT-09.8 — Confidentiality & blindness enforcement *(M)*

**STORY-09.8.1 — Enforce proposal confidentiality and evaluation blindness.** Submitted proposal contents
are hidden from other suppliers at all times; buyer-side visibility respects evaluation blindness rules
(EPIC-11). AC: cross-supplier read denied at all times; evaluator view honors blindness config until
consolidated/finalized. Priority M · Complexity M. QA: negative tests — supplier B cannot read supplier
A; evaluator blindness enforced.

---

#### FEAT-09.9 — ERP mapping fields *(C)*

**STORY-09.9.1 — Carry ERP mapping fields on Proposal.** Proposal carries `ExternalId?`, `SyncStatus`,
`LastSyncedAt`, `RowVersion`. AC: present. Priority C · Complexity S.

---
---

## EPIC-10 — Clarifications

**Goal.** A fair, structured buyer↔supplier Q&A channel per RFQ with private/published answers, asker
anonymization, addenda, window bounding, and full audit/notification.

**FRs covered:** `FR-CLR-001..006`. **Traces to:** BR-03, BR-05, BR-08, BR-09, BR-11. **Roadmap:** Phase
5. **Domain:** RFQ → Clarification[].

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-10.1 | Post clarification questions | M | *As a `supplier_user`, I want to ask a question against a published RFQ in-window, so that I can bid accurately.* AC: post only in-window (else domain refuses); notified; audited. |
| FEAT-10.2 | Answer privately or publish to all | M | *As a `procurement_officer`, I want to answer privately or publish to all invited suppliers (default publish for fairness `[ASSUMPTION]`), so that information is fair.* AC: private→asker only; publish→all invited with asker anonymized; audited. |
| FEAT-10.3 | Published thread with anonymized asker | S | *As a `supplier_user`, I want to see published clarifications with askers anonymized, so that I benefit fairly without exposure.* AC: asker identity hidden; visible to all invited. |
| FEAT-10.4 | RFQ addendum | S | *As a `procurement_officer`, I want to issue an addendum (spec/timeline change) that notifies all invited suppliers, so that changes are transparent.* AC: addendum on timeline; all notified; audited. |
| FEAT-10.5 | Window bounding | M | *As the system, I want to reject posts outside the clarification window, so that fairness/timeliness hold.* AC: out-of-window post refused by domain. |
| FEAT-10.6 | Audit & notify | M | All clarification activity audited + notified. |

---

## EPIC-11 — Evaluation

**Goal.** Multi-evaluator, independent-then-consolidated evaluation against the RFQ's weighted criteria
with thresholds, lock-on-submit, permissioned override, and finalize — desktop/tablet optimized and
RTL-correct.

**FRs covered:** `FR-EVL-001..011`. **Traces to:** BR-06, BR-07, BR-08, BR-09, BR-10, BR-19. **State
machine:** `NotStarted → Assigned → InProgress → EvaluatorSubmitted → Consolidated → Finalized`.
`[ASSUMPTION]` evaluators score **independently (blind)**. **Roadmap:** Phase 7. **Domain:** Evaluation —
EvaluationAssignment[], EvaluatorScore[], ConsolidatedResult; EvaluationTemplate — Criterion[].

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-11.1 | Evaluation template authoring | M | *As a `system_admin`/`procurement_manager`, I want to define reusable weighted criteria (name, weight, max, threshold, scoring type), so that RFQs evaluate consistently.* AC: weights validate (e.g. sum rule `[ASSUMPTION]`); reusable across RFQs; audited. |
| FEAT-11.2 | Assign evaluators | M | *As a `procurement_manager`, I want to assign evaluators (`NotStarted→Assigned`), so that scoring can begin.* AC: one EvaluationAssignment per evaluator; guarded; notified. |
| FEAT-11.3 | Blind independent scoring | M | *As an `evaluator`, I want to score each proposal per criterion privately (`Assigned→InProgress`), so that my judgement is independent.* AC: cannot see peers' scores; optional per-criterion comments; identity/commercial shielding per config `[ASSUMPTION]`. |
| FEAT-11.4 | Weighted computation & thresholds | M | *As the system, I want to apply weights and enforce per-criterion thresholds, so that below-threshold criteria flag/disqualify per template.* AC: weighted score correct; threshold breach flags/disqualifies per rule. |
| FEAT-11.5 | Submit & lock (permissioned override) | M | *As an `evaluator`, I want my submission (`InProgress→EvaluatorSubmitted`) to lock my scores, reopenable only via permissioned override + audit.* AC: locked on submit; override requires permission + audit. |
| FEAT-11.6 | Consolidate & finalize | M | *As a `procurement_officer`/`procurement_manager`, I want consolidation (`EvaluatorSubmitted→Consolidated`) into a ranked ConsolidatedResult and finalize (`Consolidated→Finalized`), so that shortlisting/recommendation can proceed.* AC: aggregate/weighted-average + ranking; finalize unlocks RFQ next stage. |
| FEAT-11.7 | Non-responding evaluator handling | C | *As a `procurement_manager`, I want to reassign/exclude a non-responding evaluator under a quorum rule `[ASSUMPTION]`.* AC: quorum configurable; action audited. |
| FEAT-11.8 | Evaluator UX & scoring audit | M | Desktop/tablet, keyboard-navigable, RTL, progress-aware; all scoring/override/consolidation audited with `evaluation.score`. |

---

## EPIC-12 — Comparison

**Goal.** Side-by-side proposal comparison with best-per-line/threshold highlighting, multi-currency
normalization, blindness-respecting visibility, export for the award file, and responsive/RTL tables.

**FRs covered:** `FR-CMP-001..006`. **Traces to:** BR-07, BR-06, BR-09, BR-10, BR-18. **Roadmap:** Phase
7. **Domain:** derived view over Proposal + Evaluation.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-12.1 | Comparison matrix | M | *As a `procurement_officer`/`procurement_manager`, I want a side-by-side matrix (line prices, totals, terms, technical responses, scores/ranking), so that I can decide defensibly.* AC: all submitted proposals; scoped; RTL sticky headers. |
| FEAT-12.2 | Best/threshold highlighting | S | *As a `procurement_officer`, I want best-per-line, weighted-rank, and threshold pass/fail highlighted, so that outliers are obvious.* AC: correct min/best + pass/fail flags. |
| FEAT-12.3 | Multi-currency normalization | S | *As a `procurement_officer`, I want normalization to a display currency with the rate shown, so that bids are comparable. Rate source `[ASSUMPTION]`.* AC: normalized totals + rate provenance. |
| FEAT-12.4 | Blindness/permission respect | M | *As the system, I want the comparison to honor blindness until finalized and viewer scope, so that fairness/confidentiality hold.* AC: pre-finalize blindness; scoped. |
| FEAT-12.5 | Export for award file | C | *As a `procurement_officer`, I want to export (PDF/print) the comparison localized/RTL, so that the award file is complete.* AC: export matches on-screen; localized. |
| FEAT-12.6 | Responsive/RTL table | S | Sticky headers, tabular numerals, responsive, RTL-aware. |

---

## EPIC-13 — Procurement Workflow

**Goal.** Present the RFQ lifecycle as a guided, gated workspace binding RFQ, Invitations, Proposals,
Clarifications, and Evaluation, with stage-gate prerequisites, timeline automation, and concurrency
handling.

**FRs covered:** `FR-PWF-001..005`. **Traces to:** BR-03, BR-06, BR-07, BR-08, BR-11, BR-12, BR-20.
**Roadmap:** Phase 8. **Domain:** orchestration over RFQ + related aggregates.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-13.1 | Guided RFQ workspace | M | *As a `procurement_officer`, I want the lifecycle shown as guided stages with current `RfqState`, permitted next actions, and blockers, so that I always know what's next.* AC: stage view reflects state + permissions; blockers explained. |
| FEAT-13.2 | Stage-gate enforcement | M | *As the system, I want prerequisites enforced (no `UnderEvaluation` before `SubmissionClosed`; no recommendation before evaluation `Finalized`), so that the process is correct.* AC: out-of-order transitions refused by domain. |
| FEAT-13.3 | Action audit & notify | M | All workflow actions permission-guarded, audited, and produce notifications. |
| FEAT-13.4 | Timeline automation resilience | M | *As the system, I want scheduled timeline actions (open/close/clarification/reminders) durable across restarts.* AC: exactly-once/idempotent; survives restart. |
| FEAT-13.5 | Concurrency handling | S | *As the system, I want `RowVersion`-guarded edits with a localized conflict prompt, so that simultaneous edits don't clobber.* AC: conflict surfaced + resolvable. |

---

## EPIC-14 — Award

**Goal.** Complete the loop: recommendation → approval → award → non-winner handling → immutable award
file → Outbox award event to ERP (PO), never blocking on ERP.

**FRs covered:** `FR-AWD-001..008`. **Traces to:** BR-02, BR-07, BR-08, BR-09, BR-11, BR-15. **State
machine:** `Recommended → PendingApproval → Approved | Rejected → Awarded → (Outbox → ERP PO)`.
**Roadmap:** Phase 8. **Domain:** Award — Recommendation, Approval[], AwardDecision,
ExternalPurchaseOrderRef?.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-14.1 | Produce recommendation | M | *As a `procurement_officer`, I want to recommend the winning proposal(s) with justification grounded in evaluation/comparison (`Recommended`), so that the award is defensible.* AC: justification required; references evaluation. |
| FEAT-14.2 | Route for approval | M | *As a `procurement_officer`, I want to route the recommendation for approval (`Recommended→PendingApproval`). Single approver, configurable hierarchy `[ASSUMPTION]`.* AC: approver notified; guarded. |
| FEAT-14.3 | Approve/reject decision | M | *As a `procurement_manager`, I want to approve/reject with mandatory reason (`PendingApproval→Approved|Rejected`); reject returns to recommendation with feedback.* AC: reason required; audited. |
| FEAT-14.4 | Issue award & notify | M | *As a `procurement_manager`, I want to issue the award (`Approved→Awarded`), moving the winner to `AwardOffered/Awarded` and others to `NotSelected`, notifying all suppliers.* AC: all suppliers notified; states consistent. |
| FEAT-14.5 | Award → ERP PO (Outbox) | M | *As the system, I want awarding to enqueue an Outbox event translated to an ERP Purchase Order, storing `ExternalPurchaseOrderRef`, without blocking on ERP.* AC: transactional Outbox; ERP-down still awards. |
| FEAT-14.6 | RFQ closure | M | RFQ transitions `AwardApproval→Awarded→Completed` in step with Award. |
| FEAT-14.7 | Immutable award file | S | *As the system, I want the award outcome, justification, and comparison snapshot retained immutably, so that the decision is auditable.* AC: award file immutable + retrievable. |

---

## EPIC-15 — Notifications

**Goal.** Timely, relevant, multi-channel (in-app + email; SMS future) notifications with in-app history,
localized templates, preferences, de-duplicated reminders, and Outbox-decoupled generation.

**FRs covered:** `FR-NOT-001..007`. **Traces to:** BR-02, BR-10, BR-11, BR-13, BR-18, BR-19. **Roadmap:**
seeded P1, deepened P9. **Domain:** Notification.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-15.1 | Lifecycle event notifications | M | *As any user, I want notifications for relevant lifecycle events, so that I act promptly.* AC: events across onboarding/docs/RFQ/invite/proposal/clarify/eval/award emit notifications. |
| FEAT-15.2 | In-app notification center | M | *As any user, I want an in-app center with unread/read, grouping, deep links, history, RTL, so that I track what matters.* AC: unread badge; deep links; localized. |
| FEAT-15.3 | Localized email delivery | M | *As the system, I want AR/EN email templates matching recipient locale via durable retrying jobs.* AC: locale-correct; retried; durable. |
| FEAT-15.4 | Preferences | S | *As any user, I want per-category/channel preferences (opt-out non-critical only) `[ASSUMPTION]`.* AC: critical always sent; prefs honored. |
| FEAT-15.5 | Outbox-decoupled generation | M | *As the system, I want notification generation decoupled via Outbox, so channel outages never block domain actions.* AC: domain action commits regardless of channel health. |
| FEAT-15.6 | De-duplicated reminders | S | *As the system, I want scheduled, de-duplicated deadline reminders (submission/expiry/review).* AC: no duplicate reminder for the same event. |
| FEAT-15.7 | SMS channel (disabled) | C | Designed-for, disabled until provider confirmed `[ASSUMPTION]`. |

---

## EPIC-16 — Supplier Dashboard

**Goal.** A supplier home surfacing onboarding/profile completeness, document status/expiries, active
invitations, proposal statuses, and awards — row-scoped, responsive, RTL, accessible.

**FRs covered:** `FR-DSH-001,007,008`. **Traces to:** BR-09, BR-10, BR-12. **Roadmap:** seeded P2,
deepened P9. **Domain:** read models over Supplier/Proposal/Invitation/Document.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-16.1 | Completeness & document widget | M | *As a `supplier_admin`, I want to see onboarding/profile completeness and document status/expiries, so that I stay compliant.* AC: live completeness + expiry countdowns; scoped. |
| FEAT-16.2 | Invitations & proposals widget | M | *As a supplier user, I want active invitations and proposal statuses at a glance, so that I never miss a deadline.* AC: deep links; status chips; deadlines. |
| FEAT-16.3 | Awards widget | S | *As a `supplier_admin`, I want to see awards/outcomes, so that I know results.* AC: award outcomes shown. |
| FEAT-16.4 | Themed, responsive, scoped rendering | M | Recharts/bespoke SVG, tokens, responsive, RTL, axe; every widget row-scoped. |

---

## EPIC-17 — Procurement Dashboard

**Goal.** Operational home for procurement/evaluators: RFQ pipeline, submissions, evaluation progress,
pending approvals, upcoming deadlines, and reviewer queues.

**FRs covered:** `FR-DSH-002,003,004,007,008`. **Traces to:** BR-09, BR-10, BR-12. **Roadmap:** seeded P4,
deepened P9.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-17.1 | RFQ pipeline by state | M | *As a `procurement_officer`, I want the RFQ pipeline by state with submissions received, so that I manage flow.* AC: pipeline scoped to org; drill-down. |
| FEAT-17.2 | Evaluation progress & approvals | M | *As a `procurement_manager`, I want evaluation progress and pending approvals, so that I unblock decisions.* AC: progress + pending-approval list. |
| FEAT-17.3 | Evaluator dashboard | M | *As an `evaluator`, I want assigned evaluations, scoring progress, and deadlines.* AC: assignments + progress + deadlines. |
| FEAT-17.4 | Onboarding/compliance queue | M | *As an `onboarding_reviewer`, I want review queue, SLA/aging, info-requests, and doc-expiry watchlist.* AC: aging/SLA; watchlist. |
| FEAT-17.5 | Upcoming deadlines | S | Consolidated deadline view (submission/clarification/expiry). |

---

## EPIC-18 — Ministry Dashboard

**Goal.** Read-only, cross-organization governance oversight for the Ministry with aggregate metrics and
a policy flag for commercial-value visibility.

**FRs covered:** `FR-DSH-005`. **Traces to:** BR-09, BR-12. **Roadmap:** Phase 10.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-18.1 | Cross-org aggregate metrics | M | *As a `ministry_viewer`, I want read-only cross-org metrics (RFQ volumes, cycle times, participation, awards), so that I oversee the ecosystem.* AC: read-only; cross-org; aggregate. |
| FEAT-18.2 | Commercial-visibility policy flag | M | *As the platform, I want a flag governing whether Ministry sees commercial values or only aggregate/anonymized `[ASSUMPTION]`.* AC: default aggregate/anonymized; enforced server-side. |
| FEAT-18.3 | Governance drill-downs (read-only) | S | Scoped read-only drill-downs; write attempts refused. |

---

## EPIC-19 — Reporting

**Goal.** Parameterized, localized, exportable procurement/compliance/governance reports respecting
scope.

**FRs covered:** `FR-CMP-005`, `FR-AUD-004`, governance reports. **Traces to:** BR-07, BR-08, BR-12.
**Roadmap:** Phase 10.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-19.1 | Procurement reports | S | *As a `procurement_manager`, I want RFQ/cycle-time/award reports exportable (PDF/CSV), so that I report performance.* AC: parameterized; scoped; localized/RTL. |
| FEAT-19.2 | Compliance reports | S | *As an `onboarding_reviewer`, I want supplier/document compliance reports, so that I track registry health.* AC: expiry/rejection stats; scoped. |
| FEAT-19.3 | Governance reports | S | *As a `ministry_viewer`, I want governance reports honoring the commercial-visibility flag.* AC: aggregate/anonymized by default. |
| FEAT-19.4 | Export engine (PDF/CSV, RTL) | S | Localized, RTL-correct exports shared with comparison/audit exports. |

---

## EPIC-20 — Search

**Goal.** Scoped, server-side, faceted search/list across suppliers, RFQs, proposals, documents — never
leaking cross-scope data.

**FRs covered:** `FR-SRCH-001..006`. **Traces to:** BR-09, BR-10, BR-17. **Roadmap:** seeded P3, deepened
P10.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-20.1 | Supplier search (staff) | M | *As a `procurement_officer`/`onboarding_reviewer`, I want to search suppliers by name/category/offering/region/state within scope.* AC: scoped; faceted; paginated. |
| FEAT-20.2 | RFQ search | M | *As a `procurement_officer`/`ministry_viewer`, I want to search/filter RFQs by state/org/category/timeline within scope.* AC: scoped (ministry cross-org read-only). |
| FEAT-20.3 | Supplier self search | M | *As a `supplier_user`, I want to search my invitations/proposals/documents by state/RFQ.* AC: own-scope only. |
| FEAT-20.4 | Server-side list infra | M | *As any user, I want paginated/sorted/faceted lists (TanStack Table), RTL-aware and accessible.* AC: server-side paging/sort/filter. |
| FEAT-20.5 | Scope-safe results | M | Row-scoping applied in the query; negative tests prove no leakage. |
| FEAT-20.6 | Full-text search | C | Across documents/proposals where indexed `[ASSUMPTION]` on scope/indexing. |

---

## EPIC-21 — Administration

**Goal.** Admin control of users/roles/permissions, organizations, reference data, evaluation templates,
system settings, notification templates, and integration/job health.

**FRs covered:** `FR-ADM-001..011`. **Traces to:** BR-06, BR-08, BR-09, BR-11, BR-15, BR-16, BR-18,
BR-20. **Roadmap:** seeded P3, extended throughout.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-21.1 | User management | M | *As a `system_admin`, I want to create/invite/disable/reset users and assign roles within scope.* AC: scoped; audited. |
| FEAT-21.2 | Roles & permissions | M | *As a `system_admin`, I want to manage roles and their `resource.action` permission sets (seeded defaults, editable).* AC: edit without deploy; audited. |
| FEAT-21.3 | Organizations & OrgUnits | M | *As a `system_admin`, I want to manage buying entities (Hotel/MOT body/Ministry) and Supplier↔Organization many-to-many.* AC: M2M supported; scoped. |
| FEAT-21.4 | Reference data | M | *As a `system_admin`, I want to manage Category tree, DocumentType, Currency, UoM, Incoterm, Region.* AC: localized labels; audited; used everywhere. |
| FEAT-21.5 | Evaluation templates | M | *As a `system_admin`/`procurement_manager`, I want reusable evaluation templates.* AC: criteria/weights/thresholds; reusable. (Shared with EPIC-11.) |
| FEAT-21.6 | System settings | M | *As a `system_admin`, I want to configure registration mode, default currency, numeral system, doc-expiry windows, approval hierarchy `[ASSUMPTION]`.* AC: applied without deploy; audited. |
| FEAT-21.7 | Notification templates | S | Manage AR/EN templates and channel enablement. |
| FEAT-21.8 | Integration/Outbox health | S | View Outbox health, retry/dead-letter, ERP sync status. |
| FEAT-21.9 | Job health | S | Hangfire dashboard health + scheduled tasks. |
| FEAT-21.10 | Admin audit | M | All admin changes permission-guarded (`admin.*`) and audited. |
| FEAT-21.11 | Retention/cleanup | S | Configure/run retention (abandoned drafts, expired tokens) with audit. |

---

## EPIC-22 — Audit & Compliance

**Goal.** An append-only, immutable, scoped, searchable audit trail correlated to traces, covering every
state change and sensitive access.

**FRs covered:** `FR-AUD-001..006`. **Traces to:** BR-08, BR-12, BR-19, BR-20. **Roadmap:** seeded P1,
surfaced P10. **Domain:** AuditLog (append-only).

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-22.1 | Transition/action audit capture | M | *As the system, I want every state transition and sensitive action recorded (actor, timestamp, from→to, reason, `correlationId`).* AC: one immutable entry per event. |
| FEAT-22.2 | Immutability | M | *As the platform, I want the audit log append-only — no one (incl. admin) can edit/delete.* AC: no update/delete path; verified. |
| FEAT-22.3 | Scoped audit read | M | *As a `system_admin`/`procurement_manager`/`ministry_viewer`, I want to read in-scope audit via `audit.read`; suppliers see their own trail.* AC: scoped; supplier self-trail. |
| FEAT-22.4 | Filter/search/export | S | Filter by entity/actor/action/date; exportable for governance. |
| FEAT-22.5 | Trace correlation | S | Audit correlates to OTel `correlationId` for end-to-end investigation. |
| FEAT-22.6 | Access-event auditing | M | Document view/download and exports audited as access events. |

---

## EPIC-23 — ERP Integration

**Goal.** Resilient async ERPNext integration via ACL + transactional Outbox + adapters: supplier-master
sync on approval, award→PO on award, string `ExternalId` mapping, conflict handling, and full
ERP-outage resilience.

**FRs covered:** `FR-INT-001..009`. **Traces to:** BR-02, BR-08, BR-15, BR-20. **Roadmap:** Phase 11.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-23.1 | Transactional Outbox write | M | *As the system, I want integration events written transactionally with the state change.* AC: atomic; no event without commit. |
| FEAT-23.2 | Durable dispatcher | M | *As the system, I want a background dispatcher with backoff, retry, dead-letter.* AC: retries; dead-letters + alerts on repeated failure. |
| FEAT-23.3 | Supplier-master sync | M | *As the system, I want approved Supplier → ERPNext `Supplier` mapping (superset→subset) storing string `ExternalId`.* AC: `ExternalId` stored; never integer FK. |
| FEAT-23.4 | Award → Purchase Order | M | *As the system, I want the award event translated (ACL) to an ERPNext Purchase Order, storing `ExternalPurchaseOrderRef`.* AC: PO key stored; non-blocking. |
| FEAT-23.5 | Sync fields & conflicts | M | *As the system, I want `SyncStatus/LastSyncedAt/RowVersion` maintained; conflicts queued not overwritten.* AC: conflicts surfaced to admin. |
| FEAT-23.6 | ERP-outage resilience | M | *As the system, I want full portal function when ERP is down; pending syncs drain on recovery.* AC: end-to-end works ERP-down; drains later. |
| FEAT-23.7 | Inbound sync (optional) | C | Inbound reference/master via ACL preserving invariants `[ASSUMPTION]` on direction/scope. |
| FEAT-23.8 | Versioned adapters, fail-safe | S | Swappable/versioned adapters; contract mismatch dead-letters + alerts (not silent). |

---

## EPIC-24 — Security

**Goal.** Meet OWASP ASVS L2 targets across authn/z, data protection, input handling, file safety, and
operational security — verified, not assumed.

**Traces to:** BR-08, BR-09, BR-19. NFR: brief §9. **Roadmap:** seeded P0/P1, hardened P12.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-24.1 | ASVS L2 controls baseline | M | *As a security owner, I want ASVS L2 controls (session, authz, validation, crypto) in place, so that we meet the target.* AC: ASVS L2 checklist satisfied + evidenced. |
| FEAT-24.2 | Input & output safety | M | Validation everywhere (FluentValidation/Zod); output encoding; anti-CSRF; secure headers/CSP. |
| FEAT-24.3 | Secrets & crypto | M | Managed secrets; keys rotated; data-at-rest/in-transit encryption. |
| FEAT-24.4 | File-upload safety | M | MIME/size validation + malware scan + no public buckets (shared with EPIC-05). |
| FEAT-24.5 | AuthZ fuzzing & scope tests | M | Automated row-scoping/permission negative tests as a gate. |
| FEAT-24.6 | Dependency/secret scanning | S | CI SCA + secret scanning + SBOM. |
| FEAT-24.7 | Rate limiting & abuse | S | Login/reset/2FA rate limits; anti-automation. |
| FEAT-24.8 | Pen-test & remediation | S | Pre-launch pen test + tracked remediation (P12). |

---

## EPIC-25 — Observability

**Goal.** Structured logs, distributed traces, and metrics (Serilog + OpenTelemetry) with correlation,
plus health/alerting for jobs, Outbox, and integration.

**Traces to:** BR-20. NFR: brief §9. **Roadmap:** seeded P0, hardened P12.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-25.1 | Structured logging + correlation | M | *As an operator, I want JSON logs with `correlationId` across requests/jobs.* AC: correlation propagated request→job→audit. |
| FEAT-25.2 | Distributed tracing | M | *As an operator, I want OTel traces across API/DB/jobs/integration.* AC: end-to-end spans; exportable vendor-neutral. |
| FEAT-25.3 | Metrics & dashboards | S | Latency (p95), error rates, job/Outbox depth, business KPIs. |
| FEAT-25.4 | Health & alerting | S | Health checks; alerts on Outbox backlog, dead-letters, job failures, SLO breaches. |
| FEAT-25.5 | Runbooks | S | Operational runbooks for common incidents (P12). |

---

## EPIC-26 — Performance

**Goal.** Meet the brief §9 targets: API p95 < 300ms reads / < 800ms writes; LCP < 2.5s, INP < 200ms on
mid-range mobile; route-level code splitting.

**Traces to:** brief §9. **Roadmap:** verified per-slice, hardened P12.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-26.1 | API latency budgets | M | *As a platform, I want p95 read/write budgets enforced in CI/load tests.* AC: p95 within target under load. |
| FEAT-26.2 | Web vitals budgets | M | *As a user, I want LCP/INP within target on mid-range mobile.* AC: budgets tracked; regressions fail CI. |
| FEAT-26.3 | DB indexing & query review | S | Indexes for scoped list/search; N+1 elimination; JSONB query tuning. |
| FEAT-26.4 | Code splitting & caching | S | Route-level splitting; TanStack Query caching; asset/CDN strategy. |
| FEAT-26.5 | Load/soak testing | S | Pre-launch load + soak with pass thresholds (P12). |

---

## EPIC-27 — Localization

**Goal.** Arabic-first, RTL/LTR, i18next-keyed strings, SYP default + multi-currency, locale-aware
dates/numerals across the whole product.

**FRs covered:** cross-cutting (`FR-*` localization clauses). **Traces to:** BR-10, BR-18. **Roadmap:**
seeded P0, verified P12.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-27.1 | i18n framework & keys | M | *As a user, I want every string localized (AR default, EN secondary).* AC: no hard-coded strings; `dir` flips with language. |
| FEAT-27.2 | RTL correctness | M | *As an Arabic user, I want correct RTL layout, mirrored directional icons, logical properties.* AC: no LTR leakage; icons mirror. |
| FEAT-27.3 | Currency & numerals | M | *As a user, I want SYP default, multi-currency, configurable numerals (Western default `[ASSUMPTION]`), tabular figures.* AC: currency/numeral config; tabular numerals in tables/prices. |
| FEAT-27.4 | Dates/formatting | S | Gregorian default, locale-aware; Hijri optional/future `[ASSUMPTION]`. |
| FEAT-27.5 | Localization QA | S | Pseudo-localization + AR/EN visual review as a gate (P12). |

---

## EPIC-28 — Responsive / Mobile

**Goal.** Premium responsive experience: supplier surfaces mobile+desktop first-class; back-office
desktop-optimized (tablet for evaluators); all breakpoints RTL-correct and accessible.

**FRs covered:** cross-cutting responsive clauses. **Traces to:** BR-10. **Roadmap:** seeded P0, verified
P12.

### Features & representative stories

| Feature | Name | Priority | Representative story |
|---|---|---|---|
| FEAT-28.1 | Responsive layout system | M | *As a supplier on mobile, I want fully usable onboarding/proposal flows, so that I can work from a phone.* AC: mobile→desktop verified for supplier flows. |
| FEAT-28.2 | Back-office density | M | *As a `procurement_officer`, I want desktop-optimized dense workspaces; evaluator tablet-friendly.* AC: desktop layouts; evaluator tablet pass. |
| FEAT-28.3 | Responsive tables | S | Comparison/search tables responsive with sticky headers + horizontal scroll, RTL-aware. |
| FEAT-28.4 | Touch & input ergonomics | S | Touch targets, gestures, input types; no hover-only affordances. |
| FEAT-28.5 | Responsive QA | S | Cross-device/viewport review as a gate (P12). |

---

## Traceability & coverage

- **State machines** (brief §5) each have owning epics: onboarding (EPIC-03), documents (EPIC-05), RFQ
  (EPIC-07/13), proposal (EPIC-09), evaluation (EPIC-11), award (EPIC-14).
- **Aggregates** (brief §4) each map to an epic (User/Role/Permission→EPIC-01; Supplier→02/03/04/05/06;
  RFQ→07/08/10; Proposal→09; Evaluation/Template→11/12; Award→14; Notification→15; AuditLog→22;
  Outbox/ERP→23; reference data→21).
- **Every EPIC** advances at least one **BR** and its owning **FR** range; cross-cutting concerns
  (EPIC-22/24/25/26/27/28) are enforced as Global-DoD gates on every story, not deferred.
- **`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`** items are carried inline and mirror
  [`ASSUMPTIONS.md`](../product/ASSUMPTIONS.md) / [`OPEN-QUESTIONS.md`](../product/OPEN-QUESTIONS.md).

*End of BACKLOG. Sequencing and phase gates are in [`ROADMAP.md`](./ROADMAP.md).*
