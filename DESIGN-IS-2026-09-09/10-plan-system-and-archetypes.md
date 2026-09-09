# Reaching all 65 screens — the plan, phase by phase

The decision this implements: **do not redesign 65 screens one at a time.** Four screens exceed 500 lines
and 38 are under 200, most of them the same shape — a heading, filters, a table, a paginator. Those do not
have a composition problem; they have a consistency problem, and 38 separate answers to it would drift
apart by construction. So: one system pass, four archetypes, three remaining large screens, and the charts.

Measured on this branch, not estimated:

| | Count |
|---|---|
| Route screens | 65 |
| Screens using `PageHeading` | 6 |
| Screens hand-rolling their own `<h1>` | 51 |
| Sizes `<h1>` renders at | **3** — `--text-h1` on 5, `--text-h2` on 45, `--text-h3` on 7 |
| Screens with a table or list card | 33 |
| Screens with form fields | 24 |
| Screens with KPI or dashboard cards | 10 |
| Screens over 500 lines | 4 |

---

## Phase 2 — the system pass

**Why first.** It lands on every screen at once, and the archetypes below are built on top of it. Doing
archetypes first would mean redoing them.

### 2.1 · One page title

`PageHeading` already exists and renders an `<h1>` at `--text-h2` (24px). The density rules
(`design-system/RECONCILIATION.md`) say a page title is `--text-h1` (30px), one size, once per page. So the
component changes, and every screen already using it moves for free.

- Change the size token in `PageHeading`, and take an optional `actions` slot, because a page title with
  its primary action beside it is the shape the buyer workspace already needed.
- **Gate:** the 6 screens using it look right in both locales.

### 2.2 · Fifty-one hand-rolled titles become one component

Mechanical, one screen at a time, existing tests as the assertion. Where a screen's `<h1>` carries a
subtitle or a status chip, they move into the component's slots rather than staying loose.

- **Gate, and the part that makes it stick:** a guard test that fails when a route file contains a bare
  `<h1>`. Without it this is a one-off tidy that decays, which is the failure mode the whole audit was
  about. The test asserts its own denominator (how many route files it swept) and carries a
  revert-to-red control.

### 2.3 · The two density sets

`RECONCILIATION.md` already decided these and nothing applies them: back office body **14px**, supplier
body **16px**, with their own spacing subsets. Applied at the shell level so a screen inherits its
audience's density rather than declaring it.

- **Gate:** re-run the audit's own measurement. It found 13px as the most common size on all twelve
  screens measured. The number has to move, or the pass did not happen.

### 2.4 · What Phase 2 does not do

It does not touch composition, and it does not touch the token file. `tokenConformance.test.ts` must pass
with no new exemption.

---

## Phase 6C — four archetypes

Each gets a comp first (the build path we agreed), then is applied to its instances. Four design decisions
instead of fifty-five.

| Archetype | Instances | The question it answers |
|---|---|---|
| **List screen** | 33 | Where do filters live, how does an empty list differ from a failed one, where does the primary action sit, how does the table behave at 320px |
| **Form / wizard step** | 24 | Field grouping and rhythm, where validation appears, what "required" looks like, how a step says what is left |
| **Detail screen** | the rest | Heading band, grouped regions, rail — already answered once on the buyer tender screen, generalised here |
| **Dashboard** | 10 | What a KPI tile is, what it does when its number is zero or unknown, how "needs your attention" differs from ordinary content |

**Order:** list first (largest blast radius, simplest shape), then form, then dashboard. Detail is last
because the buyer tender screen is its reference and it is already built.

**Gate per archetype:** the comp is agreed before code; when applied, every existing test for those screens
passes untouched, and the a11y and reflow projects stay green.

---

## Phase 6D — the three remaining large screens

They differ enough that an archetype will not carry them.

| Screen | Lines | Why it is its own job |
|---|---|---|
| `OnboardingPage` | 755 | The supplier's whole application in one file. It has a comp already (`comps/onboarding.html`), which answered the gate card and the step spine |
| `admin/OperationsPage` | 515 | An operations console — jobs, health, system state. No comp yet |
| `ReviewApplicationPage` | 507 | The reviewer's decision screen. The one place a Ministry officer approves or rejects a company |

The buyer tender detail is already done.

---

## Phase 6E — the charts

Separate because nothing in the product renders one yet: `recharts` is in the manifest with **no importer**.

The comp is built (`comps/charts.html`) and it carries a finding that shapes the work: run against the real
tokens, the brand teal cannot carry categorical identity — its whole family sits at chroma 0.053–0.094
against a ~0.10 floor. The comp's answer is to avoid categorical colour: single-hue bars, status colours
only for status, identity carried by direct labels and a table view.

**Gate:** the palette validator passes in both light and dark, and every chart has a table view.

---

## Phase 7 — verify

`webapp-testing` for behaviour, then **re-run `design-is`**. The scorecard is the acceptance test. If a
re-audit still reads 12/30, this work changed how the product looks and not what it is.

---

## Order, and what blocks what

```
Phase 2 (system pass)  ──┬─→ 6C list ──→ 6C form ──→ 6C dashboard ──→ 6C detail ──┐
                         │                                                        ├─→ Phase 7
                         └─→ 6D onboarding ──→ 6D operations ──→ 6D review ───────┤
6E charts (independent) ─────────────────────────────────────────────────────────┘
```

Phase 2 blocks everything else, because every archetype and every large screen inherits its heading and its
density. The charts block nothing and are blocked by nothing.

## What could go wrong, named in advance

1. **Phase 2 decays.** A tidy without a guard is a tidy that comes undone. Hence 2.2's test.
2. **An archetype gets applied where it does not fit**, and a screen is made worse to be consistent. The
   rule: if a screen resists the archetype, that is evidence about the archetype, not about the screen.
3. **The density change breaks reflow.** Bigger supplier text at 320px is where a horizontal scrollbar
   appears. The `reports-reflow` project already checks this on some routes; it should cover more before
   2.3 lands.
4. **The measurement is never re-run**, and Phase 7 becomes a formality. The audit's numbers are in
   `01-evidence.md` §D and are re-runnable.
