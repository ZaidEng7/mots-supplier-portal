# Plan 6B — the buyer tender workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the buyer's tender screen a reading order, so an officer can tell what the tender is, what state it is in, and what to do next, without reading eleven cards of identical weight to find out.

**Architecture:** The screen keeps every capability it has. What changes is arrangement: a heading band that states identity and state, a grouped body, and a right rail carrying the one card that already knows what happens next. `RfqDetailPage.tsx` is 1,281 lines and 15 cards; the first task splits it into section components with no visual change at all, so every later task is a small diff against a file a reviewer can hold in their head.

**Tech Stack:** React 19, TanStack Router/Query, Tailwind 4, Radix, i18next (AR/EN, RTL), Vitest, Playwright + axe

**Spec:** `DESIGN-IS-2026-09-09/06-direction-contract.md`, the comp at `src/frontend/comps/rfq-detail.html`, and `01-evidence.md` §D1.

## Global Constraints

- **The token layer is preserved verbatim.** `tokens.css` is not edited; `tokenConformance.test.ts` enforces it.
- **No new dependencies without asking.** See the open question below — it decides task 2.
- **No capability is added or removed.** The comp drew a "Remind suppliers who have not bid" button; it was removed from the comp because the product has no such action. Arrangement only.
- **Every string is authored in both languages** and logged in `ARABIC-REVIEW.md` on D-65's terms.
- **`docs/` is read-only.**
- Verify with `npx tsc -b --noEmit` (the `-b` matters — plain `tsc --noEmit` reads a different config and misses unused locals), `npx vitest run`, `npm run lint`, `npx playwright test`.

---

## The open question this plan is blocked on

The comp groups the screen behind six tabs (Tender · Suppliers · Bids · Evaluation · Award · Settings). **There is no tab primitive in this project**, and `@radix-ui/react-tabs` is not installed. That leaves three routes, and the choice changes task 2 substantially:

| Option | What it costs | What it risks |
|---|---|---|
| **A. Add `@radix-ui/react-tabs`** | One dependency, from a family already used four times over (dialog, label, select, toast) | Nothing technically. It needs a yes, because "no new dependencies without asking" is a standing instruction |
| **B. Hand-roll the tabs** | No dependency | This repository has already paid for exactly this. Two overlays declared `role="dialog"` `aria-modal="true"` and trapped nothing, and the suite was green throughout — Phase 4 replaced them with Radix. A hand-rolled tablist needs roving tabindex, arrow-key semantics, `aria-controls`, and correct focus movement on activation, and gets none of them by declaring the roles |
| **C. No tabs — grouped sections with a sticky section nav** | No dependency, no hidden content | Does not match the comp. But it may be the better answer for a *workspace*: an officer scanning a tender is helped by the whole picture in one scroll, `Ctrl-F` keeps working, axe scans everything on one page load, and the audit's actual complaint was that eleven cards carry **identical weight**, not that they are all present. Weight is fixed by the band, the rail and the grouping — not by hiding |

**Recommendation: C, with A as the fallback if the Ministry wants tabs.** B should not be chosen.

Tasks 1 and 3 to 6 are identical under any option. Task 2 is written for C, with the A variant noted.

---

## File structure

| File | Responsibility |
|---|---|
| `src/routes/back-office/RfqDetailPage.tsx` | Route component: queries, mutations, and the layout that arranges the sections. Nothing else. |
| `src/routes/back-office/rfq/RfqHeaderBand.tsx` | Identity and state: title, reference, owner, state chips, the primary action for the current state |
| `src/routes/back-office/rfq/RfqNextAction.tsx` | The rail's first card — the workspace query's next action, as prose rather than as a form |
| `src/routes/back-office/rfq/RfqProgress.tsx` | The rail's stepper: drafted → reviewed → published → open → evaluation → award |
| `src/routes/back-office/rfq/RfqAtAGlance.tsx` | The rail's counts: invited, bids received, questions open, line items, changes issued |
| `src/routes/back-office/rfq/sections/*.tsx` | One file per existing card, moved verbatim in task 1 |

Splitting by section rather than by layer, because these change together: a section's markup, its mutation wiring and its strings are one unit of work.

---

### Task 1: Split the file, change nothing else

The riskiest task if it is done alongside a visual change, and the safest if it is done alone. Every one of the 55 existing tests must pass untouched, because nothing a user can see has moved.

**Files:**
- Create: `src/routes/back-office/rfq/sections/OwnershipSection.tsx` … one per card (15)
- Modify: `src/routes/back-office/RfqDetailPage.tsx`
- Test: `src/routes/back-office/RfqDetailPage.test.tsx` (unchanged — that is the point)

**Interfaces:**
- Produces: each section exports `export function <Name>Section(props)` taking exactly the values it reads today — the `rfq` DTO slice, the mutations it fires, and `t`. No section owns a query; the route keeps every `useQuery` and `useMutation` so the data flow is unchanged and reviewable.

- [ ] **Step 1: Record the baseline**

```bash
npx vitest run src/routes/back-office/RfqDetailPage.test.tsx
```
Expected: 55 passed. Write the number down; it is the assertion for this whole task.

- [ ] **Step 2: Move one card, the smallest first**

Start with the cancel card (`RfqDetailPage.tsx:1270`), which reads one flag and fires one mutation.

```tsx
// src/routes/back-office/rfq/sections/CancelSection.tsx
import { useTranslation } from 'react-i18next'
import { Button, Card } from '../../../../components/ui'

/** Irreversible, and last in the reading order on purpose — see RfqHeaderBand for why it is not a
 *  primary action. Moved verbatim from RfqDetailPage in the 6B split; behaviour unchanged. */
export function CancelSection({ onCancel, isPending }: { onCancel: () => void; isPending: boolean }) {
  const { t } = useTranslation()
  return (
    <Card title={t('rfq.cancelTitle')}>
      {/* … the markup exactly as it stood … */}
    </Card>
  )
}
```

- [ ] **Step 3: Run the tests after that one move**

```bash
npx vitest run src/routes/back-office/RfqDetailPage.test.tsx
```
Expected: still 55 passed. If it is not, the move was not verbatim — fix the move, do not adjust the test.

- [ ] **Step 4: Repeat for the remaining 14, running the suite after each**

Commit after every third or fourth. A split that breaks at card 11 should not cost the first ten.

- [ ] **Step 5: Check the route file shrank without gaining logic**

```bash
wc -l src/routes/back-office/RfqDetailPage.tsx
```
Expected: under 400 lines, and every remaining line is a query, a mutation, or layout.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "refactor(rfq): split the buyer tender screen into sections, changing nothing visible"
```

---

### Task 2: The grouping (written for option C)

Eleven cards of identical weight, seven of them empty, in DOM order. Grouped into four labelled regions with a section nav, so the reading order is a decision rather than an accident.

**Files:**
- Modify: `src/routes/back-office/RfqDetailPage.tsx`
- Modify: `src/frontend/src/i18n/config.ts`
- Test: `src/routes/back-office/RfqDetailPage.test.tsx`

Groups, from the comp and §D1:

| Group | Sections |
|---|---|
| **The tender** | Details · Line items · Requirements · Attachments · Evaluation template |
| **Suppliers** | Invitations · Clarifications |
| **Decisions** | Approvals · Evaluation |
| **Managing it** | Ownership · Deadline · Addenda · Return for edits · Cancel |

"Managing it" is last and holds every destructive or administrative control, so an officer reading top to bottom meets the tender before they meet the button that cancels it.

- [ ] **Step 1: Write the failing test**

```tsx
it('groups the sections, so the reading order is a decision rather than the DOM order', async () => {
  restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published') })
  renderPage(<RfqDetailPage />)

  // Landmarks, not headings alone: a region with an accessible name is what lets a screen-reader user
  // jump between them, which is the same affordance the section nav gives a sighted reader.
  const groups = await screen.findAllByRole('region')
  expect(groups.map((g) => g.getAttribute('aria-label'))).toEqual([
    'The tender', 'Suppliers', 'Decisions', 'Managing it',
  ])
})

it('puts every destructive control after the tender itself', async () => {
  restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published') })
  renderPage(<RfqDetailPage />)

  const cancel = await screen.findByRole('button', { name: 'Cancel RFQ' })
  const items = screen.getByRole('region', { name: 'The tender' })
  // Node.compareDocumentPosition: 4 means `cancel` follows `items` in document order.
  expect(items.compareDocumentPosition(cancel) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
})
```

- [ ] **Step 2: Run it and watch it fail**

```bash
npx vitest run src/routes/back-office/RfqDetailPage.test.tsx -t "groups the sections"
```
Expected: FAIL — `findAllByRole('region')` finds nothing.

- [ ] **Step 3: Add the strings, both languages**

```ts
// en.translation.rfq.groups
tender: 'The tender', suppliers: 'Suppliers', decisions: 'Decisions', managing: 'Managing it',
// ar.translation.rfq.groups
tender: 'المناقصة', suppliers: 'الموردون', decisions: 'القرارات', managing: 'إدارة المناقصة',
```

- [ ] **Step 4: Wrap each group**

```tsx
<section aria-label={t('rfq.groups.tender')} className="flex flex-col gap-4">
  <DetailsSection … />
  <ItemsSection … />
  …
</section>
```

`aria-label` gives the region its accessible name; `<section>` with a name is a landmark, which is what
makes the grouping real for a screen reader rather than only visible.

- [ ] **Step 5: Run the tests, then the whole suite**

```bash
npx vitest run && npx tsc -b --noEmit && npm run lint
```

- [ ] **Step 6: Commit**

**If option A (Radix tabs) is chosen instead:** the groups above become the tab list, the tab state lives
in a validated search param (`?section=suppliers`) so a tab is linkable and the back button works, and
`tests/e2e/app-a11y.spec.ts` gains one route entry per tab — axe cannot scan a panel that is not
rendered, so a six-tab screen scanned once is five-sixths unscanned. That last point is the strongest
practical argument for C.

---

### Task 3: The rail

`workspace.title` — the one card that already computes what to do next — renders "No next action is currently available." in the middle of a stack of eleven. It moves to a rail, first, in the brand colour, with the progress stepper and the counts beneath it.

**Files:**
- Create: `src/routes/back-office/rfq/RfqNextAction.tsx`, `RfqProgress.tsx`, `RfqAtAGlance.tsx`
- Modify: `src/routes/back-office/RfqDetailPage.tsx`
- Test: `src/routes/back-office/rfq/RfqProgress.test.tsx`

- [ ] **Step 1: Write the failing test for the stepper**

```tsx
it('marks the current state, everything before it as done, and everything after as still to come', () => {
  render(<RfqProgress state="SubmissionOpen" />)

  const steps = screen.getAllByRole('listitem')
  expect(steps.map((s) => s.getAttribute('data-state'))).toEqual([
    'done', 'done', 'done', 'current', 'todo', 'todo',
  ])
  // Never colour alone: the current step says so in text a screen reader reads.
  expect(within(steps[3]).getByText('Current step')).toBeInTheDocument()
})

it('a cancelled tender does not pretend it is still progressing', () => {
  render(<RfqProgress state="Cancelled" />)
  expect(screen.getByText('Cancelled')).toBeInTheDocument()
  expect(screen.queryAllByRole('listitem')).toHaveLength(0)
})
```

The second test is the denominator: a stepper that renders six hopeful steps for a cancelled tender is
the kind of instrument that says something false, which is what this whole pass has been about.

- [ ] **Step 2: Run it and watch it fail**
- [ ] **Step 3: Build the stepper from the state machine that already exists**

Read the states from the same source the status chip uses (`StatusChip`'s `LABELLED_MACHINES`), so the
stepper cannot drift from the states the product actually has.

- [ ] **Step 4: Move the workspace card into `RfqNextAction`, unchanged in behaviour**
- [ ] **Step 5: Build `RfqAtAGlance` from counts already in the DTO**

Invited, bids received, questions open, line items, changes issued — every one is `array.length` on data
the page already holds. No new query, and no number that is not already on screen somewhere.

- [ ] **Step 6: Two-column layout, and check it at 320px**

```bash
npx playwright test --project=reports-reflow
```
The rail stacks under the body below the layout breakpoint. `reports-reflow` proves the document does not
scroll sideways at 320px; add this route to its list.

- [ ] **Step 7: Commit**

---

### Task 4: The heading band

Today the page's own name, its state, and its actions are spread across the top of an undifferentiated stack, and **two state values are rendered side by side from two sources with nothing reconciling them** (§D1: the header chip read `summary.state` while the workspace card read `workspace.rfqState`).

**Files:**
- Create: `src/routes/back-office/rfq/RfqHeaderBand.tsx`
- Test: `src/routes/back-office/rfq/RfqHeaderBand.test.tsx`

- [ ] **Step 1: Write the failing test — the one that matters most here**

```tsx
it('renders one state, from one source', async () => {
  // §D1 found the header chip and the workspace card disagreeing in a capture. The fixture caused that
  // one, so it is not proof of a production defect — what it proves is that the screen reads two
  // independent state sources and reconciles nothing. This asserts there is now only one.
  restore = mockFetch({
    ...REFERENCE_ROUTES,
    '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published'),
    '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({ rfqState: 'Draft' }),
  })

  renderPage(<RfqDetailPage />)

  // Deliberately contradictory fixtures. Exactly one of them may reach the screen.
  expect(await screen.findAllByText('Published')).toHaveLength(1)
  expect(screen.queryByText('Draft')).not.toBeInTheDocument()
})
```

- [ ] **Step 2: Run it and watch it fail** — today both render.
- [ ] **Step 3: Take the state from the tender DTO only**

`workspace.rfqState` stops being rendered. The workspace query keeps driving the next action, which is
what it is for; it stops being a second opinion on state.

- [ ] **Step 4: Build the band** — title at `--text-h1`, once; reference, owner and organisation as
  caption metadata; the state chip; the primary action for the current state.
- [ ] **Step 5: Run the suite and commit**

---

### Task 5: The copy the new arrangement needs

Four group names, the stepper's six step names plus its cancelled state, the "current step" text, and the five At-a-glance labels. Every one in both languages, and logged.

- [ ] **Step 1: Add them all in one edit, both blocks**
- [ ] **Step 2: Append the table to `ARABIC-REVIEW.md`** under a "Plan 6B" heading, on D-65's terms
- [ ] **Step 3: Run `npx vitest run src/i18n` and commit**

---

### Task 6: Verify, and prove the score moved

- [ ] **Step 1: Full suite**

```bash
npx tsc -b --noEmit && npm run lint && npx vitest run && npx playwright test
```

- [ ] **Step 2: Re-measure §D1's own numbers against the rebuilt screen**

The audit measured: eleven cards of identical weight, seven empty, most common type size 13px, three
different sizes for `<h1>`, four spacing values off the 4px grid. Re-run that measurement. A redesign
whose own measurement does not move has not happened.

- [ ] **Step 3: Screenshot it at desktop and 320px, both locales**

Four captures, into `DESIGN-IS-2026-09-09/shots-after/`. The Arabic one is not a formality: the rail is
`inline-start`/`inline-end`, and RTL is where a hand-written `left`/`right` shows up.

- [ ] **Step 4: Commit, and open the PR**

---

## Migration path

Nothing migrates. This is one screen's arrangement, behind the same route, reading the same queries.

Under **option C** there is no URL change at all, so every existing deep link to `/back-office/rfqs/$referenceCode` lands exactly where it did.

Under **option A** the tab becomes a search param with a default, so an existing link without the param opens the first tab. That is the only compatibility concern in either option, and it is why the param needs a default rather than a required value.

## Cutover criteria

1. `tsc -b --noEmit`, `npm run lint`, `vitest run` and `playwright test` all green, with the a11y project still covering both locales.
2. `RfqDetailPage.tsx` is under 400 lines and holds no markup that is not layout.
3. `tokenConformance.test.ts` passes with no new exemption.
4. The 320px reflow project covers this route and passes.
5. `grep -c "workspace.rfqState" src/frontend/src` returns 0 outside the API layer — the second state source is gone.
6. The re-measurement in task 6 shows: one page-title size, 14px as the most common size, and no spacing value off the 4px grid.
7. Screenshots exist for desktop and 320px in both locales, and the Arabic one has the rail on the correct side.

## What this plan deliberately does not do

- It does not touch the supplier navigation or the supplier tender detail (plan 6C) or the charts (6D).
- It does not add the reminder action the comp invented.
- It does not change a single mutation, endpoint or permission.
