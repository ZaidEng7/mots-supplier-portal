# Risk Register — MOTS Supplier Portal

> **Status:** Baseline v1 · **Date:** 2026-08-26 · **Owner:** Principal Architect / Delivery Lead
> Companion to [ASSUMPTIONS.md](./ASSUMPTIONS.md), [OPEN-QUESTIONS.md](./OPEN-QUESTIONS.md), the
> canonical [Foundational Decisions](../architecture/00-foundational-decisions.md), and the
> [Discovery Report](./DISCOVERY-REPORT.md).

## Scoring model

- **Likelihood (L):** 1 Rare · 2 Unlikely · 3 Possible · 4 Likely · 5 Almost certain.
- **Impact (I):** 1 Negligible · 2 Minor · 3 Moderate · 4 Major · 5 Severe.
- **Score = L × I** (1–25). **Bands:** 1–6 Low · 8–12 Medium · 15–19 High · 20–25 Critical.
- **Categories:** delivery · technical · security · adoption · integration · UX · compliance · data-migration.
- **Status:** Open · Mitigating · Monitoring · Closed.

## Heat summary (current)

| Band | Count | IDs |
|---|---|---|
| Critical (20–25) | 1 | RISK-004 |
| High (15–19) | 7 | RISK-001, RISK-002, RISK-003, RISK-007, RISK-010, RISK-013, RISK-016 |
| Medium (8–12) | 9 | RISK-005, RISK-006, RISK-008, RISK-009, RISK-011, RISK-012, RISK-015, RISK-018, RISK-019 |
| Low (1–6) | 3 | RISK-014, RISK-017, RISK-020 |

---

## Integration risks (ERP / ERPNext)

| ID | Risk | Category | L | I | Score | Mitigation | Contingency | Owner | Status |
|---|---|---|---|---|---|---|---|---|---|
| RISK-001 | ERPNext integration contract (field mappings, PO write-back trigger, sync direction) is still provisional; late changes force rework of the ACL/adapters and mapping tables. | integration | 4 | 4 | 16 (High) | Isolate all ERP knowledge behind the ACL + adapters; keep `ExternalId` string-based and nullable; collect a **superset** of ERPNext supplier fields; version the mapping contract; async-by-default so the portal never depends on ERP shape at runtime. Resolve [OQ-020](./OPEN-QUESTIONS.md) early. | Ship v1 fully ERP-less; back-fill `ExternalId` and run a reconciliation job when the contract is agreed. | Integration Lead | Mitigating |
| RISK-002 | Portal and ERP diverge (duplicate suppliers, stale master data, conflicting edits) because sync is eventually consistent and both sides can edit. | integration | 4 | 4 | 16 (High) | Define single source of truth per field (canonical §1); idempotent adapters keyed by `ExternalId`/correlation ID; `RowVersion` concurrency; reconciliation + drift-detection job; conflict rules documented in `docs/integration/`. | Manual reconciliation console for admins; freeze ERP-owned fields as read-only in the portal post-integration. | Integration Lead | Open |
| RISK-003 | ERP unavailable or slow during award, blocking or delaying critical procurement flows. | integration | 3 | 5 | 15 (High) | Outbox + Hangfire retries + dead-letter; award completes in the portal independent of ERP; PO emission is async ([ASM-071](./ASSUMPTIONS.md)); circuit-breaker on adapters. | Dead-letter queue with alerting and manual replay; PO created in ERP when it recovers; no user-facing block. | Integration Lead | Mitigating |
| RISK-018 | A flow turns out to need **synchronous** ERP confirmation (e.g. real-time supplier-master or credit check), breaking async-by-default. | integration | 2 | 4 | 8 (Medium) | Confirm via [OQ-019](./OPEN-QUESTIONS.md); keep a request/response adapter path available as a documented exception with timeouts + fallback. | Degrade to async with a "pending ERP confirmation" state and manual override. | Principal Architect | Open |

## Technical & data-migration risks

| ID | Risk | Category | L | I | Score | Mitigation | Contingency | Owner | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| RISK-005 | Illegal state transitions or race conditions corrupt procurement integrity (e.g. double award, submit after close). | technical | 3 | 4 | 12 (Medium) | State machines enforced in the **domain**, not just UI (canonical §5); `RowVersion` optimistic concurrency; server-side deadline checks ([ASM-043](./ASSUMPTIONS.md)); integration tests via Testcontainers over the real Postgres; NetArchTest guards layering. | Audit-driven detection + admin remediation tooling; compensating actions with full audit trail. | Principal Architect | Mitigating |
| RISK-006 | Greenfield stack on very new versions (.NET 10, React 19, Tailwind v4, TanStack Router) surfaces library gaps, breaking changes, or thin ecosystem support. | technical | 3 | 3 | 9 (Medium) | All chosen for stability/LTS with rationale (canonical §2); pin versions; thin abstractions over volatile libs; CI + Renovate-style upgrade discipline; Storybook to catch DS regressions early. | Fall back to a prior stable minor; swap a specific library behind its abstraction if unsupported. | Principal Architect | Monitoring |
| RISK-016 | Although greenfield today, a future need to import legacy supplier lists (e.g. from ERPNext's existing `portal_users`/suppliers) creates a data-migration surface with dirty/duplicate data. | data-migration | 3 | 5 | 15 (High) | Design onboarding to accept a superset of ERPNext supplier fields; build an idempotent, dry-run-capable import keyed on `ExternalId`; explicit dedup on legal/tax identifiers (once formats confirmed, [OQ-012](./OPEN-QUESTIONS.md)); staging import with validation report. | Phased import with manual review queue; suppliers self-verify migrated profiles before activation. | Integration Lead / Data | Open |
| RISK-019 | Loss of documents or audit records due to storage misconfiguration or missing backups. | data-migration | 2 | 5 | 10 (Medium) | `IFileStorage` abstraction with S3-compatible prod storage; Postgres backups + PITR (canonical §9); append-only audit; restore drills. | Restore from PITR/backup; object-store versioning enabled; documented RPO/RTO. | Platform / SRE | Open |

## Security & compliance risks

| ID | Risk | Category | L | I | Score | Mitigation | Contingency | Owner | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| RISK-004 | Cross-tenant / cross-scope data leak — a supplier sees another supplier's proposal, or one buying entity sees another's RFQ — in a shared-instance model. | security | 4 | 5 | 20 (Critical) | Row-level scoping enforced at the API for every query (canonical §6); default-deny policies; authorization integration tests per persona; UI checks are affordance-only, never trusted; scoping asserted in a shared query pipeline, not per-endpoint ad hoc. | Immediate scope-audit + hotfix path; incident response + notification plan; option to move a sensitive buying entity to isolated tenancy ([OQ-003](./OPEN-QUESTIONS.md)). | Security Lead | Mitigating |
| RISK-007 | Confidential commercial data (proposal prices, evaluator scores) exposed to the Ministry or to unauthorized roles because visibility rules are still open. | security / compliance | 4 | 4 | 16 (High) | Default Ministry view is aggregate-only, commercial values hidden ([ASM-060](./ASSUMPTIONS.md)); evaluator scores hidden until consolidation ([ASM-050](./ASSUMPTIONS.md)); field-level authorization; resolve [OQ-001](./OPEN-QUESTIONS.md) with Legal before governance slice ships. | Toggle stricter visibility centrally; redact retroactively; audit access to sensitive fields. | Security / MOT | Open |
| RISK-008 | Uploaded documents carry malware or malicious content reaching reviewers. | security | 3 | 3 | 9 (Medium) | Server-side type/size validation; storage isolation; AV/content scanning pending confirmation ([OQ-014](./OPEN-QUESTIONS.md)); serve documents with safe content-disposition, never inline-execute. | Quarantine-on-detection; disable inline preview; async scan with hold-back before reviewer visibility. | Security | Open |
| RISK-009 | Weak authentication on high-privilege actions (award approval, admin) enables account takeover / fraud. | security | 2 | 5 | 10 (Medium) | ASP.NET Core Identity, JWT + rotating refresh; MFA available and recommended for back-office/admin ([ASM-081](./ASSUMPTIONS.md)); OWASP ASVS L2 target; audit on all state changes; rate limiting + lockout. | Enforce MFA per-role immediately ([OQ-018](./OPEN-QUESTIONS.md)); forced password reset; session revocation. | Security | Mitigating |
| RISK-010 | Inventing or mis-implementing Syrian legal/tax/registration rules creates non-compliant onboarding or invalid awards. | compliance | 4 | 4 | 16 (High) | Strict policy: **no invented rules**; legal/tax fields captured generically and tagged `[REQUIRES BUSINESS CONFIRMATION]` ([ASM-020](./ASSUMPTIONS.md), [ASM-031](./ASSUMPTIONS.md)); open questions raised to Legal/MOT ([OQ-012](./OPEN-QUESTIONS.md), [OQ-015](./OPEN-QUESTIONS.md)); validators added only after confirmation. | Rapidly add validators/reference data once rules are provided; retro-validate existing records with a remediation queue. | Legal / MOT | Mitigating |
| RISK-015 | Audit trail proves incomplete or disputable (missing actions, no tamper-evidence) undermining procurement transparency. | compliance | 2 | 4 | 8 (Medium) | Append-only audit on every permission-guarded transition with actor/timestamp/from→to/reason/correlationId (canonical §5); indefinite retention v1 ([ASM-085](./ASSUMPTIONS.md)); coverage asserted in tests. | Add hash-chaining/tamper-evidence and defined retention if mandated ([OQ-010](./OPEN-QUESTIONS.md)). | Security / Compliance | Monitoring |

## UX, localization & accessibility risks

| ID | Risk | Category | L | I | Score | Mitigation | Contingency | Owner | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| RISK-013 | Arabic/RTL quality falls short of the premium bar — mirrored layouts, mixed LTR/RTL (numbers, codes, emails), truncation, icon direction, bidi bugs — the #1 requirement missed. | UX | 4 | 4 | 16 (High) | RTL designed-in via CSS logical properties, not bolted on (canonical §7); Arabic-first default; directional icon mirroring; IBM Plex Sans Arabic; bidi-aware components; Storybook RTL snapshots; Arabic-first review gate on every slice; native-Arabic reviewer. | Dedicated RTL hardening sprint; component-level bidi fixes behind the DS. | UX Lead | Mitigating |
| RISK-014 | Numeral system / date-calendar expectations (Western vs Eastern digits; Gregorian vs Hijri) mismatch user/legal expectations on official documents. | UX / localization | 2 | 3 | 6 (Low) | Single i18n formatting helper; Western digits default but configurable ([ASM-001](./ASSUMPTIONS.md)); Gregorian default, Hijri future ([ASM-002](./ASSUMPTIONS.md)); resolve [OQ-016](./OPEN-QUESTIONS.md)/[OQ-017](./OPEN-QUESTIONS.md). | Flip config flags; add dual-calendar/Eastern-digit rendering; re-QA printed/exported docs. | Product / UX | Monitoring |
| RISK-017 | Accessibility target (WCAG 2.2 AA) slips under delivery pressure, especially for complex RFQ/evaluation tables and forms in both directions. | UX / accessibility | 3 | 2 | 6 (Low) | Radix primitives for a11y; axe-core in CI; Playwright + RTL/keyboard tests; a11y acceptance criteria per slice; headless TanStack Table styled for keyboard/RTL. | Remediation backlog with a11y gate before GA; targeted audit of high-traffic screens. | UX Lead | Monitoring |

## Delivery & adoption risks

| ID | Risk | Category | L | I | Score | Mitigation | Contingency | Owner | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| RISK-011 | Scope creep from unresolved open questions (approval chains, two-envelope evaluation, tenancy) expands the domain model mid-build. | delivery | 4 | 3 | 12 (Medium) | Vertical-slice delivery with explicit "done" gates; every ambiguity tracked in [OPEN-QUESTIONS.md](./OPEN-QUESTIONS.md) with an interim decision so work proceeds; blocking questions ([OQ-004](./OPEN-QUESTIONS.md), [OQ-009](./OPEN-QUESTIONS.md)) resolved before their slice; configurable-first structures absorb variability. | Re-baseline the roadmap; timebox spikes; defer non-P1 questions to later phases. | Delivery Lead | Mitigating |
| RISK-012 | Blocking open questions ([OQ-004](./OPEN-QUESTIONS.md) approval hierarchy, [OQ-009](./OPEN-QUESTIONS.md) two-envelope) answered late, stalling the evaluation/award slices. | delivery | 3 | 4 | 12 (Medium) | Flag as P1/Blocking with named owners; schedule decision sessions ahead of the dependent slice; build the surrounding slice with the interim decision so only the pivot point waits. | Sequence other slices first; deliver evaluation/award behind a feature flag until confirmed. | Delivery Lead / Procurement | Open |
| RISK-020 | Team unfamiliarity with the deliberately non-mainstream choices (no MediatR, Mapperly, TanStack Router, bespoke DS) slows ramp-up. | delivery | 3 | 2 | 6 (Low) | ADRs record rationale; thin dispatcher/abstractions keep patterns simple; Storybook + templates; pairing and reference slices to establish patterns early. | Targeted enablement; lean on the first vertical slice as the canonical example. | Delivery Lead | Monitoring |
| RISK-021 | Low supplier adoption — Arabic-first suppliers churn during onboarding due to friction, email deliverability, or unclear document requirements. | adoption | 3 | 4 | 12 (Medium) | Premium Arabic-first onboarding UX with draft safety and clear document checklists; email verification with resend; consider SMS OTP if deliverability is weak ([OQ-011](./OPEN-QUESTIONS.md)); progress indicators and info-requested loop rather than hard rejection. | Assisted onboarding / back-office data entry; SMS channel; simplify required fields to the confirmed legal minimum. | Product Owner | Open |

---

## Governance & review

- Register reviewed at each phase gate; scores re-assessed when a linked assumption or open question
  is resolved. Critical/High risks are reviewed at every delivery checkpoint.
- New risks are added with the next free `RISK-###`; IDs are never reused.
- Each risk links to its driving assumption(s)/open question(s) so mitigation and business decisions
  stay synchronized.

## Change log

| Date | Change | By |
|---|---|---|
| 2026-08-26 | Initial register covering integration, technical, security, compliance, UX, delivery, adoption, and data-migration risks. | Principal Architect |
