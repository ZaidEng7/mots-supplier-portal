# Phase 7 · Scorecard

Same anchors, same rules as the original: tie-break downward, score the worst instance, equal weights,
integers 0–3. Original score beside each so the movement is visible.

---

**1. Good design is innovative — Score: 2/3** *(was 1)*
Evidence: the onboarding gate replaces a checklist of all eight requirements with only what is outstanding
plus the submit it gates; the buyer workspace replaces eleven equal cards with four named regions and a
rail carrying what happens next (§D).
Justification: "refreshes an existing pattern with a clear improvement" now describes two screens where
before nothing did. Nothing here is unlike its peers, so not 3.

**2. Good design makes a product useful — Score: 1/3** *(unchanged)*
Evidence: the buyer's exits to the bids, the comparison and the award are still **four raw `<a href>`
full-page reloads** inside the tender screen (§D). The direction contract named this under #2 and the work
was never done.
Justification: a full document reload out of and back into the workspace is "unnecessary detours",
verbatim. Both primary tasks still complete, so not 0. **This is the score the pass did not move.**

**3. Good design is aesthetic — Score: 2/3** *(was 1)*
Evidence: one page-title size across every screen, was three; commonest text size is 14px on every
back-office screen and 16px on every supplier one, was 13px on all twelve; every spacing value we control
is on the 4px grid; one failure-state presentation, was two (§A).
Justification: two inconsistencies remain — the buyer detail's sparse fixture reading 18px, and two
off-grid values that belong to recharts and the browser. That is "≤2 minor", not the 3's "no orphan
styles", because I have not swept every screen for orphans, only the twelve measured.

**4. Good design makes a product understandable — Score: 2/3** *(was 1)*
Evidence: the 23 jargon items closed or kept with reasons; the supplier acronym gone from nav, title and
help alike; the raw GUID gone; "Threshold" against "Minimum" resolved; two adjacent profile links
disambiguated; two identical "Save" buttons named (§C).
Justification: the supplier navigation is still a flat ungrouped list (§D), which is one structural thing
a first-time user has to learn rather than read. That is the anchor for 2, not 3.

**5. Good design is unobtrusive — Score: 2/3** *(was 1)*
Evidence: eleven equal cards became four named regions and a rail; the skip link removes fourteen chrome
stops before the first control; destructive controls are last by construction with a test asserting it
(§D).
Justification: "chrome visible but quiet" fits. The flat supplier nav on every screen keeps it off 3.

**6. Good design is honest — Score: 2/3** *(was 1)*
Evidence: of five label→behaviour mismatches, three fixed in copy, one fixed with an associated hint, one
withdrawn because the finding was wrong; disabled and read-only now render distinctly; terminal actions
ask and warn; no dark patterns in six categories re-checked (§C).
Justification: I did not re-sweep all ~900 strings, so I cannot assert the 3's "every claim maps 1:1".
Tie-broken down on that gap rather than up on the fixes.

**7. Good design is long-lasting — Score: 3/3** *(was 2)*
Evidence: the single dated marker — an emoji as the notification icon — is gone, replaced with the icon set
the rest of the product uses. Warm-stone neutrals, evergreen teal, system fonts, 1px borders, the faintest
shadow on the scale, all unchanged (§A).
Justification: "no dated trend markers" is now the count, which is the anchor for 3.

**8. Good design is thorough down to the last detail — Score: 2/3** *(was 1)*
Evidence: `Input` has disabled, read-only, hover, focus and error; `Select` takes `disabled`; `Button` has
an active press; both overlays are real focus traps; the skip link exists; every screen sits in a
landmark; `aria-required` reaches assistive technology on all 101 fields; every screen's failure state is
tested and its retry proven to retry (§E).
Justification: the submit error summary sitting behind the controls it describes (original §F6) was
neither checked nor fixed in this pass. One state rough is the anchor for 2, and claiming 3 on an
unverified item would be the kind of assertion this batch spent its time removing.

**9. Good design is environmentally friendly — Score: 2/3** *(unchanged)*
Evidence: initial JS 218 KB gzipped across 7 files; 77 chunks emitted and recharts code-split to its own
route; one looping animation, only while loading, `prefers-reduced-motion` honoured twice and
regression-tested; dark mode present (§B).
Justification: **17 routes are still statically imported**, unchanged from the original audit, so every
visitor still downloads the admin and ministry screens. Inside the 500 KB anchor for 2, nowhere near the
100 KB for 3.

**10. Good design is as little design as possible — Score: 2/3** *(was 1)*
Evidence: `PageHeading` went from 2 files against 51 hand-rolled copies to 54 screens and none; `Card` from
15 hand-rolled copies to none; 53 nested ternaries to none; the operations console from one function at
complexity 50 to five named panels. Four guards fail if any of it is hand-rolled again (§D, §F).
Justification: `ListCard`, `LoadMore` and `FilterBar` are still adopted by four screens each, and the buyer
tender file is still 1,351 lines. Past "≤2 removable elements" would need those closed too.

---

## Total: **20 / 30** *(was 12)*

| # | Principle | Was | Now |
|---|---|---:|---:|
| 1 | Innovative | 1 | 2 |
| 2 | Useful | 1 | **1** |
| 3 | Aesthetic | 1 | 2 |
| 4 | Understandable | 1 | 2 |
| 5 | Unobtrusive | 1 | 2 |
| 6 | Honest | 1 | 2 |
| 7 | Long-lasting | 2 | **3** |
| 8 | Thorough | 1 | 2 |
| 9 | Environmentally friendly | 2 | 2 |
| 10 | As little design as possible | 1 | 2 |

No principle scored 0. One scored 3. **One did not move at all, and it is named above.**
