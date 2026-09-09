# Phase 7 · Evidence, re-gathered

The original audit's numbers, taken again the same way. Where it measured computed styles in a browser,
this measured computed styles in a browser (`tests/e2e/capture-measure.spec.ts`, run with `CAPTURE=1`);
where it swept source, this swept source.

**One correction to the instrument, recorded because it changed an answer.** The first run of the
re-measurement reported 16px as the commonest text size on four screens. That was wrong: `script`, `style`
and `title` carry text and no children, so a naive sweep counts them at the browser's default 16px, which
is nobody's design decision. The sweep now excludes them. A re-audit that flatters itself is worse than
none.

## A · Visual

| | Original | Now |
|---|---|---|
| Commonest text size | **13px on all twelve screens** | **14px on every back-office screen, 16px on every supplier screen** |
| Sizes `<h1>` renders at | **3** (`--text-h1` on 5 screens, `--text-h2` on 45, `--text-h3` on 7) | **1** — 30px on every screen measured |
| Spacing off the 4px grid | 6, 10, 80, 96 | **none that we control**. Two remain and neither is ours: recharts pads its own tooltip 10px (now overridden from the token scale) and the browser lays out a native `<option>` with a 7px gap |
| Failure-state presentations | two different ones for the same state | one (`ListState` delegates to `QueryError` everywhere) |

One screen reads 18px commonest — the buyer tender detail, where fourteen mostly-empty cards make their
`--text-h4` titles outnumber the body. A fixture artefact, recorded rather than rounded away.

## B · Weight

- Initial JS **218 KB gzipped across 7 files** (`dist/index.html`'s own module graph).
- `recharts` adds 100 KB gzipped and is code-split into its own route chunk; 77 chunks are emitted.
- **17 routes are still statically imported** (`router.tsx`), unchanged from the original audit.
- One looping animation, only while loading; `prefers-reduced-motion` honoured twice and regression-tested.
- Dark mode present in the token layer.

## C · Copy and honesty

- The five §C2 label→behaviour mismatches: **three fixed in copy**, **one fixed with an associated hint**,
  **one withdrawn as a wrong finding** — the "Publish to all" control describes a *legacy* state (rows
  answered before decision A-4), not an impossible one, and the backend has an integration test that
  promotes exactly such a row.
- §C4's 23 jargon items: closed, or kept with the reason written down (`07-copy-pass.md`). The clearest
  case: the supplier navigation said RFQs, the help text said tender, and the help answer translated
  between them inside one sentence. One word now.
- **No dark patterns**, six categories re-checked; zero hits for scarcity or urgency language.
- Two submit buttons on the onboarding screen both read "Save"; they are now named for what they save.

## D · Structure

| | Original | Now |
|---|---|---|
| `PageHeading` | used in 2 files, hand-rolled in 51 | **54 screens**, 0 hand-rolled, guarded |
| `Card` | 15 hand-rolled copies across 4 screens | **49 screens**, 0 hand-rolled, guarded |
| `ListState` | 4 screens | 10 screens, guarded against hand-rolled chains |
| Buyer tender screen | 11 cards of identical weight, 7 empty, DOM order | 4 named landmark regions and a rail |
| Nested ternaries | 53 | **0** |
| Operations console | one function, cognitive complexity 50 | five named panels |

**Still standing, and named because the contract named it:** the buyer's exits to the bids, the comparison
and the award are **four raw `<a href>` full-page reloads** inside the tender screen
(`RfqDetailPage.tsx`). §4 of the direction contract listed this under #2 useful. It was never done.

Also untouched: the supplier navigation is still a flat list of top-level links with no grouping (§D3).

## E · Accessibility

- Skip link present and first in tab order; every screen inside a landmark.
- Both overlays are Radix dialogs with real focus traps; dismissal deliberately refused on the expired-session one.
- `Input` has disabled, read-only, hover, focus and error; `Select` takes `disabled`; `Button` has an active press.
- `aria-required` now reaches assistive technology on all 101 field usages; previously nothing in the product set it.
- 228 Playwright tests including 137 axe scans across both locales, and 30 reflow checks at 320px.

## F · Guards added this batch

`tokenConformance`, `pageHeadingCoverage`, `densityConformance`, `cardCoverage`, `listStateCoverage`,
`asyncStateCoverage`, `dynamicKeyCoverage`, `keyParity`. Each asserts its own denominator and carries a
revert-to-red control.
