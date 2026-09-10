# Scorecard — MOTS Supplier Portal, 2026-09-10

Anchors applied verbatim from the rubric. Tie-breaker: when uncertain between two scores, the lower
one. Worst instance, not the mean.

Baseline, 2026-09-09: **12/30**.

---

**1. Good design is innovative — Score: 2/3**
Evidence: E2 — no dated markers and no novel pattern either; the workspace rail (next action, where
this tender stands, at a glance) and the one-record-six-views head are refreshes of established
patterns, not new ones.
Justification: it refreshes existing patterns with a clear improvement over the stacked single column
it replaced, which is a 2; nothing here advances the form, which is what a 3 would need.

**2. Good design makes a product useful — Score: 2/3**
Evidence: E1 — officer draft→award completes in 14 committing actions across 4 URLs; supplier
register→submit in 8 actions and 28 fields. But the officer's own landing screen has **0 focusable
elements in `main`**, and the tender list carries no closing date, so "what closes this week" is
answered on a different screen.
Justification: both primary tasks complete, and the adjacent surface — a landing screen that supports
no task — adds steps rather than removing them, which is exactly the 2 anchor.

**3. Good design is aesthetic — Score: 2/3**
Evidence: E2 — one visible system with a real token layer, no gradients, no skeuomorph residue. Two
user-visible inconsistencies: the same date-range control built twice, rendering 10px shorter with a
2.9× weaker border on one screen; page titles in mixed casing ("Complete Your Supplier Profile"
against "Procurement dashboard").
Justification: two minor inconsistencies is the 2 anchor; the declared spacing scale having no
consumers is a token-layer defect a reader cannot see, so it does not pull this to 1.

**4. Good design makes a product understandable — Score: 1/3**
Evidence: E3 — "RFQ" 27 times in strings shown to a supplier whose own navigation says "Tenders";
"Addenda"; the raw code `tour_operations` rendered as a value. E1/live — the Assignee filter displays
the State filter's value ("Awaiting a decision", confirmed in the DOM); the pipeline tiles show an
unlabelled date beside each count.
Justification: jargon is present *and* at least two controls are unclear, which is the 1 anchor
exactly; a 2 would allow one control needing a tooltip.

**5. Good design is unobtrusive — Score: 2/3**
Evidence: E1 — chrome is a constant 16 focusable elements on every back-office screen and 100% of the
interactivity on two of them. E2 — the rail is a flat dark field, shadows are single soft layers,
nothing decorative competes.
Justification: chrome is visible but quiet, which is the 2 anchor; it is navigation rather than
decoration, so it does not compete with content in the way a 1 describes.

**6. Good design is honest — Score: 1/3**
Evidence: E3 — a supplier is asked to confirm they have read Terms & Conditions that do not exist and
are never shown, and the acceptance is recorded with a version, while the product's own About page
says the terms have not been issued. The withdrawal dialog says re-entry is impossible; the backend
creates a new draft. The read-only banner tells an **Approved** supplier their application is with a
reviewer.
Justification: one dark pattern is the 1 anchor; it is not one of the three flows the 0 anchor names
(forced continuity, hidden cost, fake scarcity), so it does not reach 0 — but it is the single most
serious finding in this audit.

**7. Good design is long-lasting — Score: 3/3**
Evidence: E2 — zero dated markers across nine screenshots and repository-wide greps. The only
gradient is a loading shimmer. No trend typography, no bevels, no inset shadows, flat fills.
Justification: nothing here reads as a particular year, which is the 3 anchor; there is not even one
dated marker to drop it to 2.

**8. Good design is thorough down to the last detail — Score: 2/3**
Evidence: E2/E5 — all six states exist. Focus is the rough one: `--focus-ring` is applied by four
shipped files, sidebar and in-page links fall back to the 1px UA outline, and activating the skip
link leaves `document.activeElement` on `BODY` — against a specification claiming "Visible focus
everywhere". Loading renders two different ways; empty does not distinguish "nothing yet" from
"nothing matched".
Justification: no state is missing, and one — focus — is genuinely rough, which is the 2 anchor.

**9. Good design is environmentally friendly — Score: 0/3**
Evidence: E4 — 219KB gzip initial JS, zero idle animations measured, `prefers-reduced-motion`
respected twice over. **Dark mode is not honoured:** the theme is fully built, shipped to every user
as bytes, and unreachable, recorded by the project's own `themeReachability.test.ts`.
Justification: the 0 anchor names "dark mode ignored" explicitly. A reader whose system asks for dark
is served light and downloads the dark palette anyway. Every other measure on this principle is
strong, and one rubric clause carries it.

**10. Good design is as little design as possible — Score: 1/3**
Evidence: E1 — on the tender list alone: an Owner column with 25 identical values, a screen-reader
caption repeating the card's own heading, an `<h1>` and an `<h2>` naming the same list, a footer Help
duplicating the top-bar Help, and a second Search control to the same destination. Elsewhere: a
Button nested in a Link giving two focus stops for one destination, cards that render zero controls
and empty text on any non-draft tender, a heading over a single card, three dead props.
Justification: five removable elements on one audited screen is the 1 anchor; the page is not
dominated by decoration, so it does not reach 0.

---

## Total: **16 / 30**

| # | Principle | 2026-09-09 | 2026-09-10 |
|---|---|---|---|
| 1 | Innovative | — | 2 |
| 2 | Useful | — | 2 |
| 3 | Aesthetic | — | 2 |
| 4 | Understandable | — | 1 |
| 5 | Unobtrusive | — | 2 |
| 6 | Honest | — | 1 |
| 7 | Long-lasting | — | 3 |
| 8 | Thorough | — | 2 |
| 9 | Environmentally friendly | — | 0 |
| 10 | As little design as possible | — | 1 |
| | **Total** | **12** | **16** |

Per-principle baseline scores are not restated because the 2026-09-09 artefacts record the total and
the verdict rather than a comparable breakdown under these anchors.
