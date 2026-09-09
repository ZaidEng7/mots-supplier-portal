# 02 · Scorecard

Scored by the orchestrator against the Phase 2 anchors, on the evidence in `01-evidence.md`.
Rules applied: tie-break downward; score the worst instance, not the mean; equal weights; integers 0–3.

---

**1. Good design is innovative — Score: 1/3**
Evidence: no pattern here is unlike its peers — a top nav, cards, tables, a wizard, a queue (§D, §E3).
The genuinely uncommon work in this repository is engineering, not design: sweeps that assert their own
denominator before their rule (§F8, `tokenConformance`, `asyncStateCoverage`).
Justification: "imitates competitors with minor variation" describes it exactly; it is not a wholesale
copy of one competitor's flow (0), and nothing here refreshes a pattern with a clear improvement (2).

**2. Good design makes a product useful — Score: 1/3**
Evidence: the buyer's route to the bids, the comparison and the award is a conditional card in the middle
of a 1,256-line screen, entered through `<a href>` full page reloads (§E1, §E5); the supplier's submit
button lives on one wizard route while the data it requires is spread across three (§E2).
Justification: both primary tasks complete, so not 0; but "unnecessary detours" is literally what a full
document reload out of and back into the buyer's workspace is, which puts it below 2.

**3. Good design is aesthetic — Score: 1/3**
Evidence: **13px is the most common text size on all twelve screens measured** against a spec that says
"Body 14–16 (canonical)" (§A1); page titles render at three sizes for one job (§A2); four spacing values
sit off the declared 4px scale (§A3); two date formats appear on one screen (§A9); the same failure state
has two different presentations (§A7); labels wrap inside buttons (§D1, §D2).
Justification: a single visible system does exist and is now enforced by a test — so not 0 — but six
inconsistencies is past "≤2 minor" (2) and past "3–5" (1) at its own boundary; taken down, not up.

**4. Good design makes a product understandable — Score: 1/3**
Evidence: the reviewer's application screen prints the raw string `onboarding.fields.defaultCurrency` as a
field label (§A5); 23 jargon items including supplier-facing "RFQs", "Addenda", "Envelope", "Recuse", a
raw GUID rendered as "Bound template", and "Threshold" contradicting the evaluator brief's own "Minimum"
(§C4); "Complete Profile" and "Profile" adjacent in the nav (§D3); the one card that could say what to do
next says "No next action is currently available" (§D1).
Justification: primary actions are identifiable — Approve, Reject, Submit application, Save score — so not
0; but "2–3 controls unclear; jargon present" understates it rather than overstates it.

**5. Good design is unobtrusive — Score: 1/3**
Evidence: eleven cards of identical weight on the buyer's main screen, seven of them empty (§D1); five
identical "reason + confirm" rows on that one screen (§E3); a twelve-item flat top nav on every supplier
screen (§D3); fourteen chrome tab stops before the first page control (§F2).
Justification: there is no decoration to speak of — this is scaffolding, not ornament — but scaffolding
that competes with content is the same failure from the reader's side. Not 0, because content is present
and legible once found.

**6. Good design is honest — Score: 1/3**
Evidence: five label→behaviour mismatches (§C2), including a hint that promises a state change the code
does not make while the string beside it describes the truth; an "always sent" list naming two
notification families that do not exist; a control for a state the domain cannot produce; a hidden
approver nomination; and a `window.prompt` that silently does nothing while the copy promises an audit
entry. Plus disabled fields that render identically to editable ones (§A6).
Justification: **no dark patterns exist** — six categories checked, consent opt-in, destructive actions
warned and gated (§C3) — so this is nowhere near 0. But "2+ inflations" is met several times over.

**7. Good design is long-lasting — Score: 2/3**
Evidence: warm-stone neutrals and an evergreen teal, system fonts, 1px borders, the faintest shadow on the
scale — no gradient, no glass, no trend typography (§A4). One dated marker: an **emoji 🔔 as the
notification icon** (`NotificationBell.tsx:39-43`) in a product that otherwise uses a real icon set.
Justification: "1 dated marker" is the anchor for 2, and that is the count.

**8. Good design is thorough down to the last detail — Score: 1/3**
Evidence: the `Input` primitive implements **neither disabled nor read-only**, both required by
`DESIGN-SYSTEM.md` §6.2, and `--color-text-disabled` is defined in both themes and consumed by nothing
(§A6); `Select` accepts no `disabled` prop at all, so a read-only wizard leaves three comboboxes operable
with nothing to commit them (§F5); two overlays declare `aria-modal` and manage no focus (§F4); the spec's
required skip link does not exist (§F2); seven screens render outside every landmark (§F3); the submit
error summary sits behind the controls it describes (§F6); an `<a href>` wraps a `<button>` on the three
main workspace exits (§E5).
Justification: empty, loading, error, success and focus are all present and considered — the anchor's
"4+ states missing" (0) is not met. Disabled and read-only are both absent, and the focus-management gaps
compound them, which is more than "1 state missing or rough" (2).

**9. Good design is environmentally friendly — Score: 2/3**
Evidence: 252,841 B transferred on first load (856,574 B decoded), largest chunk 700 KB raw with the
build's own ">500 kB" warning, and **17 routes still statically imported** so every visitor downloads the
admin and ministry screens (§B1); two 60-second polls on every authenticated screen and one 2-second poll
that deliberately continues while the tab is hidden (§B6); TTI last measured 1.55–1.99 s, estimated
1.7–2.5 s today (§B3).
Justification: the transferred figure sits inside the anchor's 500 KB for a 2, and motion is genuinely
gated — one looping animation in the product, only while loading, with `prefers-reduced-motion` honoured
twice over and regression-tested (§B4). Well short of the 3's 100 KB.

**10. Good design is as little design as possible — Score: 1/3**
Evidence: twelve repeated patterns where the shared component **already exists and is used twice** —
`PageHeading` in 2 files against 24 hand-rolled copies of its exact markup, `ListCard` 2 against 6,
`LoadMore` 2 against 5, `FilterBar` 2 against 6 (§E3); three independent implementations of the
document-status row; five of the file upload (§E3).
Justification: past "3–5 removable elements" several times over. Not 0: the duplication is of structure
rather than decoration, and on a populated tender the empty cards do carry content.

---

## Total: **12 / 30**

| # | Principle | Score |
|---|---|---:|
| 1 | Innovative | 1 |
| 2 | Useful | 1 |
| 3 | Aesthetic | 1 |
| 4 | Understandable | 1 |
| 5 | Unobtrusive | 1 |
| 6 | Honest | 1 |
| 7 | Long-lasting | 2 |
| 8 | Thorough | 1 |
| 9 | Environmentally friendly | 2 |
| 10 | As little design as possible | 1 |

**No principle scored 0**, and none scored 3.
