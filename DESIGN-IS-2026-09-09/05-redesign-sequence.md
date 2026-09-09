# 05 · The redesign sequence

Working checklist for the REDESIGN verdict in `03-verdict.md` (12/30). One phase at a time, each with a
gate that has to hold before the next starts. **Direction owner: `impeccable` (Option A), chosen
2026-09-09.**

The constraint that shapes everything below, and the one most easily lost: **the palette is not the
problem.** Principle #7 (long-lasting) scored 2/3 — the joint-highest mark in the audit — because the
visual language has no trend markers. The 1s were composition, states, copy and duplication. Every phase
below therefore preserves the token layer and redesigns what sits on top of it.

---

## Phase 0 — the brief · **done**

`00-scope.md` → `04-handoff-prompt.md` are written. Nothing downstream re-derives the problem; each phase
quotes from them.

**Gate:** ✅ passed — the audit is committed at `77dc915`.

---

## Phase 1 — direction · `impeccable`

Owns the whole arc: direction contract → comp → build → finish review against that contract, with its own
agents (`impeccable-documenter`, `impeccable-finish-reviewer`, `impeccable-asset-producer`,
`impeccable-manual-edit-applier`).

**Before it runs, the direction contract must pin four things**, or the pipeline will enforce its own
quality bar over the parts that already work:

1. **Preserve `src/frontend/src/styles/tokens.css` verbatim** — colour, type, radius, elevation, motion,
   z-index — and the test that enforces it (`tokenConformance.test.ts`). No new palette, no new type scale.
2. **Preserve the domain vocabulary, every state machine, and all 68 route paths.** This is a composition
   redesign, not an IA rewrite of the product's model.
3. **Two audiences, not one.** The back office is dense professional work; the supplier side is an outside
   company that visits rarely. They may not converge on one density.
4. **Bilingual AR/EN with RTL is a first-class constraint**, not a localisation pass at the end.

**Deliverable:** a direction contract plus a comp of the buyer workspace and one supplier screen.
**Gate:** the comp visibly answers "what do I do next?" on the buyer's screen, and the token diff is empty.

---

## Phase 2 — density and layout rules · `ui-ux-pro-max`

Two passes, because the audit's single worst aesthetic finding — 13px as the default body size across
every screen — comes from treating two audiences as one.

```bash
# back office: dense, professional, table-heavy
python "…/ui-ux-pro-max/scripts/search.py" "government procurement back office dense tables" \
  --design-system --density 8 --variance 4 --motion 3 \
  -p "MOTS Back Office" --persist --output-dir /Users/zaid/Projects/Supplier

# supplier side: infrequent visitor, form-heavy, needs air
python "…/ui-ux-pro-max/scripts/search.py" "supplier onboarding portal forms guidance" \
  --design-system --density 4 --variance 3 --motion 3 \
  -p "MOTS Supplier" --persist --output-dir /Users/zaid/Projects/Supplier
```

**Deliverable:** two `design-system/<slug>/MASTER.md` files, reconciled against `docs/ux/DESIGN-SYSTEM.md`
— where the tool disagrees with the spec, **the spec wins** and the disagreement is written down.
**Gate:** a stated body size per audience, and a stated page-title size (the product currently renders
`<h1>` at three different sizes for one job).

---

## Phase 3 — charts · `dataviz` · **load before the first line of chart code**

`recharts@3.10.1` is a dependency **with no importer anywhere in the product**. Four dashboards render
KPIs as bare numbers and the Ministry award analytics renders spend as three tables.

Candidates, in the order they earn their place:
- Award spend by month, by category, by buying body (SCR-603 — already three tables of the same shape).
- Review-queue ageing against the at-risk and overdue thresholds (the tones already exist).
- Evaluation score distribution per criterion, once an evaluation is consolidated.
- Category coverage: approved vs active suppliers per category (SCR-604 — the gap is the point).

**Gate:** every chart passes `dataviz`'s own colour validator, reads correctly in both themes, and states
what it does **not** show. No chart that a table would answer better.

---

## Phase 4 — component states · `emil-design-eng`

Principle #8 (thorough) scored 1. Feed it the audit's §A6, §F4, §F5 and the states that remain unbuilt
after `#133`:

- Hover on `Input` (spec §6.2 asks for it; nothing implements it).
- Focus management on the two hand-rolled `aria-modal` overlays — `SessionExpiredOverlay`,
  `FirstRunLocale` — which trap nothing today.
- The missing skip link (`docs/ux/ACCESSIBILITY.md:52`), and landmarks on the seven public screens that
  render outside all of them.
- Motion curves: the four transitions now use the scale; nothing else has any considered motion.

**Gate:** the six states — empty, loading, error, success, focus, disabled — exist on every primitive, and
`read-only` on the two that need it.

---

## Phase 5 — copy · `humanizer` → `structural-humanizer`

Copy is a design surface and it scored into both #4 and #6. The material is already enumerated:

- 23 jargon items (§C4) — "RFQs", "Addenda", "Envelope", "Recuse", "Consolidate", a raw GUID rendered as
  "Bound template", "Threshold" contradicting the evaluator brief's own "Minimum".
- 5 label→behaviour mismatches (§C2) — each needs the *label* fixed or the *behaviour* fixed, and which
  one is a product decision, not a copy decision.
- Five of six error strings are interchangeable ("Could not load X"), so a reader learns *that* it failed
  and never *why*.

**Gate:** every string in scope is authored in both languages and logged in `ARABIC-REVIEW.md` on D-65's
terms — the newer Arabic still ships as *accepted for the demonstration build without a line-by-line read*
until a native reviewer reads it.

---

## Phase 6 — build · `superpowers:writing-plans` → `executing-plans`

Implementer: `engineering-skills:senior-frontend`. The plan must carry a **migration path** — 142 screens
are already delivered into a demonstration environment, so a half-migrated product has to stay coherent —
and **cutover criteria** saying when the old composition is deleted.

**Gate:** every existing test still passes, and the guards added by this batch stay green:
`tokenConformance`, `asyncStateCoverage`, `dynamicKeyCoverage`, the a11y locale guard, the route
denominator.

---

## Phase 7 — verify · `webapp-testing`, then re-run `design-is`

**Gate, and the only one that matters:** the score moves. If a re-run of the Rams audit still reads 12/30,
the redesign changed how it looks and not what it is.

---

## Mockups, between 1 and 6

`design` — the Claude Design canvas — for the buyer workspace as artboards before any code. That screen is
1,256 lines, 78 interactive elements and eleven equal cards; it is the cheapest thing in the product to
get wrong twice. `imagegen-frontend-web` + `image-to-code` are the alternative if rendered references suit
better than editable artboards.

## Charts, specifically

`dataviz` is the skill. **Not** `visualize` (in-chat diagrams) and **not** `artifact-diagramming`
(published artifacts). The `magic` MCP can generate a chart-card shell; it does not decide what to chart.
