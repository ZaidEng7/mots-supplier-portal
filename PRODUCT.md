# Product

> **Draft, 2026-09-09.** Every fact below is drawn from the repository, `docs/` and
> `DECISIONS-TAKEN.md` (D-1…D-68). Items marked **[inferred]** were not confirmed by the product
> owner and are the ones to correct first.

## Platform

web

## Users

Four personas, in two groups that do not share a situation:

- **Supplier admin** — an outside company, usually small, applying to trade with the Ministry. Visits
  rarely and under deadline: register the company, get approved, keep documents current, and submit a
  priced bid before a closing date. Arabic-first. **Confirmed 2026-09-09: mostly desktop.** They apply
  and bid from an office computer. The mobile tab bar and the 320px reflow guard stay as a courtesy and a
  floor, not as the design target.
- **Onboarding reviewer** — Ministry staff. Decides applications from a queue with an ageing indicator
  (at-risk and overdue thresholds are already modelled), and afterwards keeps a supplier's documents
  current: expiry, renewal, suspension under BRULE-023 and reinstatement under D-67.
- **Procurement officer / manager** — buying-body staff. Authors a tender, binds an evaluation template,
  invites suppliers, answers clarifications, closes submission, runs evaluation to a recommendation, and
  routes an award for approval. Eleven state transitions between "create" and "award executed".
- **Evaluator** — scores each bid against the criteria **without knowing whose bid it is** (A-8). Sees
  bidder identities exactly twice: at the conflict-of-interest declaration before scoring opens, and after
  consolidation.

A fifth, **ministry_viewer**, reads across every buying body but writes nothing (D-66) and is outside the
current design scope.

## Product purpose

A supplier portal and tender workflow for the Ministry of Transport: register and vet suppliers, publish
tenders to the ones qualified to bid, take sealed bids, evaluate them blind, and award with an auditable
trail. Success is that a tender can be run end to end by the people who own each step, and that every
decision it records can be defended afterwards.

## Positioning

**[inferred]** Not a generic procurement SaaS: the rules it enforces are this Ministry's, written down and
answerable: sealed bids, bidder anonymity during scoring, segregation of duties between recommender and
approver, an append-only audit log enforced by a database trigger, and a disclosure boundary that defaults
to off. The mechanism a neighbouring product could not truthfully copy is that its business rules are
*recorded decisions* (D-1…D-68) rather than configuration.

## Operating context

- **Bilingual Arabic / English, full RTL.** Arabic is the audience's first language. Every screen ships
  both; an administrator can reword any string without a release (SCR-716).
- **Deadlines are load-bearing.** A submission window closes on a schedule; a document expires on a date;
  a supplier suspended by an expiry cannot bid until a replacement is approved.
- **Documents are the currency of trust.** Commercial registration and tax certificate are award-critical
  (D-58): expiry suspends the supplier automatically.
- **Work is queued, not ad hoc.** Reviewers work a queue with ageing; officers work a tender through
  states; evaluators work an assignment.
- **Two shells, two densities.** Back-office staff live in the product all day; a supplier visits a few
  times a year under time pressure. The current design treats them as one, and the audit found the cost.

## Capabilities and constraints

- The stack is React 19, TanStack Router + Query, Tailwind 4, Radix primitives, i18next, recharts (declared,
  not yet imported anywhere), Vite. Backend ASP.NET Core / EF Core / PostgreSQL.
- **142 screens delivered** across 70 routes. The product is in a **demonstration environment** with seeded data,
  not production, and no real supplier's bid has ever been held in it.
- Concurrency is explicit. Guarded writes require `If-Match`; a stale version is refused, not merged.
- **Accessibility floor: WCAG 2.2 AA in both languages** (`docs/ux/ACCESSIBILITY.md`). `axe` runs over all
  70 routes per build, in both languages.
- **`docs/` is the specification and is read-only** to design work. `docs/ux/DESIGN-SYSTEM.md` is the
  authority on tokens; where a tool disagrees with it, the spec wins.
- **Terminology is procurement-legal**, some of it opaque to suppliers: RFQ, addendum, clarification,
  envelope, consolidation, recusal, award-critical, Incoterm. The audit flagged 23 such items.
- Two things are open and not ours to decide: the ERP adapter (another engineer), and the M9 testing pass (ASVS L2,
  a WCAG audit by a person, and the two load-dependent measurements).

## Brand commitments

- The token layer at `src/frontend/src/styles/tokens.css` (evergreen-teal brand, warm-stone neutrals,
  Inter with IBM Plex Sans Arabic) **is binding**. It is enforced by `tokenConformance.test.ts`, and the
  audit scored it as the product's strongest dimension (#7 long-lasting, 2/3).
- The back-office chrome is **dark in both themes**, deliberately, so staff can tell at a glance which side
  of the product they are in.
- Government context: the product speaks for a Ministry. It does not sell, does not exaggerate, and does
  not use urgency as a device. The audit found **no dark patterns** and that is a commitment, not an
  accident.

## Evidence on hand

- `DOCUMENTATION.md` — the whole product in one file: what it does, how it is built, what is open.
- `DECISIONS-TAKEN.md` — the recorded decisions, several of which constrain the interface directly.
- `docs/ux/` — the specification set the interface is built against.
- Absences that must not be invented: no real suppliers, no real bids, no production traffic, no
  usability sessions with actual Ministry staff or actual suppliers, no load-test environment.

## Product principles

1. **Say what is true, including when it is inconvenient.** A withheld value renders as withheld, never as
   zero; a failed fetch says so rather than showing an empty list. Both were defects; both are now
   instruments.
2. **A rule that fires must have a way back.** BRULE-023 suspends automatically, so D-67 reinstates
   automatically once the condition is objectively gone.
3. **The audit trail is the product.** Every consequential action names a person, a time and a reason.
4. **Arabic is not a translation layer.** It is half the product, and the half the audience reads first.
5. **Instruments over intentions.** Where a rule matters, there is a sweep that fails when it is broken,
   and the sweep asserts its own denominator before it asserts the rule.

## Accessibility and inclusion

WCAG 2.2 AA in **both** languages is the stated floor. As of 2026-09-09 the automated scan genuinely covers
both (the Arabic half had been scanning the English UI until `#133`). Known gaps, from the audit: no skip
link, seven public screens render outside every landmark, and two hand-rolled `aria-modal` overlays manage
no focus. A human audit with a screen reader is deferred under D-68 and open against M9.
