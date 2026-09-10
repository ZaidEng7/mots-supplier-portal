# Scope — MOTS Supplier Portal, Rams re-audit

**Date:** 2026-09-10
**Baseline:** `DESIGN-IS-2026-09-09/` — scored **12/30**, verdict **REDESIGN** of the composition,
states and copy layers.

## What is being audited

The running application, not the repository. `http://localhost:5173` against the live API on
`:5080` and the seeded PostgreSQL database — **31 suppliers, 25 tenders across six states, 106
invitations, 25 bids**. This matters: every previous capture in this repository was taken against
the Playwright fixtures, which answer each list with one row. A list with one row cannot show
whether a list works.

Evidence is the 27 screenshots in `shots/`, captured by `src/frontend/tests/e2e/capture-live.spec.ts`
by signing in for real, plus the source those screens are built from.

### Surface

**Buyer (officer@mots.local)** — dashboard, tender list, procurement dashboard, review queue,
review dashboard, supplier directory, reports, ministry overview, ministry awards, offering search,
search, notifications.

**Tender workspace** — all six views of one tender: tender, suppliers, bids, comparison, award,
settings.

**Supplier (supplier@mots.local)** — dashboard, tenders, proposals, onboarding wizard, profile,
documents, offerings, team, settings.

## Primary users and their tasks

- **A Ministry procurement officer**, in the product all day. Primary task: move a tender from draft
  to award — publish it, watch bids arrive, get it evaluated, recommend a winner. Secondary: work the
  supplier review queue.
- **A supplier company**, a few times a year, under deadline. Primary task: complete a legal
  application and get approved; then find a tender they were invited to and bid on it.

## Constraints

React 19, TanStack Router and Query, Tailwind 4, Radix, recharts. Full Arabic RTL with i18next; both
languages ship together. ASP.NET Core and EF Core behind it. WCAG 2.2 AA is a stated floor, enforced
by an axe sweep over all 70 routes in both languages and by a token-layer contrast guard.

`docs/` is read-only to design work, so anything that contradicts the specification is reported
rather than changed.

## What has shipped since the baseline

Six phases: the token layer and its contrast guard; both shells rebuilt as a grouped rail with a
reachability guard; the seventeen shared components restyled with no screen edited; four screen
families (11 lists, 6 workspace views, 9 forms, 8 dashboards); the long tail of 41 screens; and
verification — 280 captures in both themes plus a theme-reachability guard.

## Known gap carried into this audit

The dark theme is defined, guarded pair-by-pair, and unreachable: nothing in the application applies
it. Recorded in `src/frontend/src/styles/themeReachability.test.ts`. It bears on principle #9.
