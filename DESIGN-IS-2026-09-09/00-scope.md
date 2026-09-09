# 00 · Scope

**Audited:** the MOTS Supplier Portal at `main @ cff6b8e`, two surfaces:

1. **The supplier journey** — register → verify email → onboarding wizard (profile, documents, contacts,
   addresses, banking, offerings) → dashboard → tender list → tender detail → proposal → my proposals.
2. **The back-office reviewer and buyer surface** — review queue, application review, compliance
   directory, supplier directory, RFQ list and detail, evaluation (brief, scoring, comparison), award,
   approvals, procurement dashboard.

Deliberately out of scope: the four Ministry oversight screens, the platform-administration screens, and
the design system audited as a thing in itself. Roughly 34 of the product's 68 routes are in scope.

**Primary users and tasks**

| User | Primary task |
|---|---|
| Supplier admin (an outside company) | Register the company, get approved, and submit a priced bid before a deadline |
| Onboarding reviewer (Ministry staff) | Decide an application, and keep a supplier's documents current afterwards |
| Procurement officer (buying-body staff) | Author and publish a tender, then run it to an award |
| Evaluator | Score each bid against the criteria without knowing whose bid it is (A-8) |

**Constraints**

- Stack: React 19, TanStack Router/Query, Tailwind 4, Radix primitives, i18next.
- Bilingual **AR/EN with full RTL**; Arabic is the primary language of the audience.
- `docs/ux/DESIGN-SYSTEM.md`, `UX-WRITING.md`, `ACCESSIBILITY.md` and `RESPONSIVE-AND-RTL.md` are the
  written specification; they are read-only here and the product is measured against them.
- Accessibility floor: WCAG 2.2 AA, both languages. An `axe` scan runs over all 68 routes per build.
- Government procurement: the product is answerable for what it asserts, which makes principle #6
  (honesty) load-bearing rather than decorative.

**Input materials**

- Source at `src/frontend/src` (68 routes, ~15 shared components, one token layer).
- A dev server on :5173. **The API is not running**, so authenticated screens redirect to login when
  driven live; the e2e fixture harness (`tests/e2e/fixtures.ts`) mocks the backend and is the way to
  render them honestly. Anything not verifiable either way is marked INFERRED.

**Auditor's own conflict, stated:** most of this UI was written by me, including a design-token pass and
an async-state pass merged the same day (#130, #131). Scores are read off the Phase 2 anchors against
cited evidence, not off intent.
