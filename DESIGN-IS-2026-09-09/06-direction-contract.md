# 06 · Direction contract

**Draft for review, 2026-09-09.** This is the document that governs the redesign. It is deliberately
short, because its job is to be *read* by whoever picks the work up — including a pipeline that would
otherwise apply its own defaults.

Owner of the direction: **`impeccable`**, Option A. Mode: **Operate**.

---

## 1. The word "redesign" means something narrower here than usual

Impeccable's own taxonomy: *"Refinement preserves; redesign replaces… Redesign… treats the old look as
evidence and anti-reference; choose a replacement world."*

**That is not this job.** The Rams audit returned REDESIGN of the *composition, states and copy* layers and
named the visual world as the thing to preserve — principle #7 (long-lasting) scored **2/3**, the
joint-highest mark on the board, because the palette and type carry no trend markers. In impeccable's
vocabulary this is a **large refinement in Operate mode**, and the commands that own it are `layout`,
`harden`, `clarify`, `onboard` and `adapt` — **not** `new-work`, and **not** a replacement DESIGN.md.

If any step of this work finds itself choosing a palette, a type family or a new brand direction, it has
left the contract.

## 2. Preserved, verbatim

1. **`src/frontend/src/styles/tokens.css`** — colour, type scale, radius, elevation, motion, z-index.
   Enforced by `src/frontend/src/styles/tokenConformance.test.ts`, which fails on a literal colour, a
   Tailwind utility where a token exists, or an undefined `var(--…)`. **The token diff at the end of this
   work must be empty**, other than tokens *added* for states that have none.
2. **Domain vocabulary and every state machine** — onboarding, RFQ, proposal, evaluation, award. The
   product's model is not in scope.
3. **All 68 route paths.** Composition changes; URLs do not.
4. **`docs/`** — read-only. `docs/ux/DESIGN-SYSTEM.md` is the authority. Where a tool disagrees with it,
   the spec wins and the disagreement is written down rather than silently resolved.
5. **The dark back-office chrome**, theme-invariant, as an intentional signal of which side of the product
   the user is on.
6. **The five existing instruments** — `tokenConformance`, `asyncStateCoverage`, `dynamicKeyCoverage`, the
   a11y route-denominator and locale guards. Green at the end, or the change is not done.

## 3. Two audiences, and they may not converge

| | Back office | Supplier |
|---|---|---|
| Who | Ministry and buying-body staff | An outside company |
| Frequency | All day, every day | A few times a year, under deadline |
| Density target | High — tables, panels, many labels | Low — air, guidance, one thing at a time |
| Failure that matters | A slow scan of dense data | Not knowing what is required, or missing a date |

The current product renders both at the same density and the same 13px body size. **One design language,
two densities** is the target; a single answer for both is a failure of this contract.

## 4. What success looks like, per principle that failed

The audit's ten scores are the acceptance criteria. Named here in the order they should move:

- **#8 thorough (1/3).** Every primitive carries default · hover · focus · active · disabled · loading ·
  error, and read-only where it applies. Impeccable's Operate guidance says the same thing: *"Don't ship
  with half of these."*
- **#6 honest (1/3).** Every label maps 1:1 to what the code does. Five known mismatches close, each by
  fixing the label **or** the behaviour — which one is a product decision, not a design decision.
- **#5 unobtrusive (1/3) and #10 as little design as possible (1/3).** The buyer's workspace stops being
  eleven cards of equal weight, seven of them empty. Shared components get used: `PageHeading` is
  currently used in 2 files and hand-rolled in 24.
- **#4 understandable (1/3).** A supplier meets no term they cannot act on. 23 jargon items are listed.
- **#3 aesthetic (1/3).** One body size per audience, one page-title size (today `<h1>` renders at three),
  no off-scale spacing.
- **#2 useful (1/3).** The buyer's exits — bids, comparison, award — are first-class navigation rather
  than `<a href>` full-page reloads inside a conditional card.

**#7 (2/3) and #9 (2/3) are not to be "improved".** They are the two that work. Leave them alone.

## 5. Constraints no visual decision may break

- **Bilingual AR/EN with full RTL**, first-class from the first sketch. Logical properties only —
  `padding-inline`, `margin-block`, `gap`; never `left`/`right`. Arabic gets the looser line-height the
  token layer already defines.
- **WCAG 2.2 AA in both languages.** Additions must not rely on colour alone, must keep the focus ring,
  and must not introduce a nested interactive element (the current `<a href>` wrapping a `<button>` is one,
  and axe does not catch it).
- **Motion carries state, never decoration.** 120–200ms from the existing scale. One looping animation
  exists in the product (the skeleton shimmer, while loading) and that is the ceiling.
- **`prefers-reduced-motion`** is honoured globally and must stay so.
- **No new dependency without asking.** Charts use `recharts`, which is already declared and unused.
- **No modal as a first thought.** Impeccable's Operate rule and the product's own history agree: two
  hand-rolled overlays already exist and neither traps focus.

## 5a. The ruling that governs every ambiguous case

**Asked and answered, 2026-09-09: this work changes how the product looks and reads. It does not change
what it does.**

So where a label and its behaviour disagree — and five pairs do — **the words change, not the code**. The
proposal screen will stop promising that recording a response returns the proposal for re-review, because
the code marks it Revised and an officer moves it; the fix is the sentence, not the state machine.

Two consequences, both deliberate:

- Any case where the behaviour looks genuinely wrong is **brought to the product owner as a question**, not
  fixed as a side effect of a design pass.
- Controls that exist for states the domain cannot produce (the clarification "Publish to all" is the known
  one) are **listed for a decision** before removal. Nothing that works today stops working.

## 6. Out of scope, explicitly

The domain model, the API contract, permissions, the audit trail, the ERP seam, the Ministry oversight
screens (D-66, shipped yesterday), the four M9 items deferred under D-68, and the two standing gates —
the disclosure sign-off and the line-by-line Arabic read.

## 7. How this work is verified

1. The five instruments stay green.
2. `node …/impeccable/scripts/detect.mjs --json <changed targets>` — once, when the UI is finished.
3. `webapp-testing` for behaviour.
4. **Re-run `design-is`.** The scorecard is the acceptance test: if a re-audit still reads 12/30, this
   work changed how the product looks and not what it is.

## 8. Open questions this contract cannot answer

Listed rather than guessed, per the audit's own rule about not inventing what belongs to somebody else:

1. **Positioning** — §Positioning in `PRODUCT.md` is still marked `[inferred]`. It does not block this
   work; correct it when somebody who owns the claim reads it.
2. ~~**Build path**~~ — **answered: comp-first.** A rendered composition is agreed before implementation,
   and the build is then judged against it. Recorded in `.impeccable/config.json`.
3. ~~**Supplier device reality**~~ — **answered: mostly desktop.** Suppliers apply and bid from an office
   computer; the supplier side gets moderate density rather than a mobile-first rework, and 320px stays a
   floor the product must not break rather than the shape it is designed around.
