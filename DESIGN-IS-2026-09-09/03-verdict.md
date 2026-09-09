# 03 · Verdict

## REDESIGN

**The interaction and composition layer of this product scores 12/30 against Rams, and the failures are
systemic rather than screen-deep: the shared vocabulary is missing two states the spec requires, five
labels describe behaviour the code does not perform, and the components that would fix the duplication
already exist and are used in two files out of twenty-six.**

The Phase 3 rule is mechanical: total below 20 is REDESIGN, and 12 is not near the line. I am not going to
re-score to reach REFINE. Three things are worth saying plainly about what that verdict does and does not
mean, because the wrong reading of it would be expensive.

**It is not a verdict on the engineering.** 1,243 backend tests, 630 frontend tests and 137 axe scans pass
on this commit; the domain model, the API contract, the concurrency guards, the audit trail and the
permission system are not in scope here and nothing in this audit impugns them. The instruments this
repository has built — sweeps that assert their own denominator before their rule — are better than the
industry norm, and two of them were written the same day as this audit.

**It is a verdict on what a person meets.** A supplier's disabled form looks editable. A reviewer's screen
prints `onboarding.fields.defaultCurrency` where a field label belongs. A buyer's workspace is eleven
identical cards, seven of them empty, with the exits rendered as full-page reloads inside a conditional
block. The Arabic interface — for an Arabic-first government audience — has never been rendered by the
accessibility suite on any screen behind sign-in.

**It is scoped to the layer that failed.** The token layer (colour, type, radius, elevation, motion, z)
was fixed hours before this audit and is now enforced by a test; it is preserved wholesale. So is every
piece of domain vocabulary, every state machine, and every route path. What gets redesigned is
composition, navigation, component states and copy — the layer between the tokens and the domain.

---

## The five highest-leverage moves

1. **Give the input primitives their missing states, and make the read-only case real.**
   `Input` implements neither `disabled` nor `read-only` and sets colour inline, so it overrides the
   browser's own disabled rendering; `Select` accepts no `disabled` prop at all. A submitted application
   therefore presents an editable-looking form that silently refuses input, and three comboboxes that
   still change with nothing to save them.
   Evidence: `01-evidence.md` §A6, §F5 — `components/ui/Input.tsx:13-35`, `components/ui/Select.tsx`,
   `routes/OnboardingPage.tsx:552-574`, `docs/ux/DESIGN-SYSTEM.md` §6.2. (Principles #8, #6)

2. **Make the copy layer answerable to the code, the way the token layer now is.**
   Five labels describe behaviour that does not happen, one screen contradicts itself on the same
   interaction, and a raw translation key renders as a field label on the reviewer's screen. There are 53
   dynamic `t()` call sites and no guard against a missing key — the exact gap `tokenConformance` closed
   for tokens.
   Evidence: §A5, §C2 — `routes/ReviewApplicationPage.tsx:277`, `i18n/config.ts:2448`, `:3229`, `:2657`,
   `routes/back-office/RfqDetailPage.tsx:475-476`, `:980`. (Principles #6, #4)

3. **Rebuild the buyer's workspace around what happens next, not around eleven equal cards.**
   `RfqDetailPage.tsx` is 1,256 lines, 78 interactive elements, JSX depth 15, fifteen sections and eight of
   the eleven state transitions in the buyer's whole job — and the one card that could direct the officer
   renders "No next action is currently available." The exits to bids, comparison and award are `<a href>`
   full page reloads wrapping buttons, inside a conditional block.
   Evidence: §D1, §E1, §E5. (Principles #5, #2, #10)

4. **Use the component vocabulary that already exists, and delete what it replaces.**
   `PageHeading` is used in 2 files while its exact markup is hand-rolled in 24; `ListCard` 2 against 6;
   `LoadMore` 2 against 5; `FilterBar` 2 against 6. Three independent document-status rows, five file
   uploaders. This is the cheapest of the five moves and it is what makes #3's inconsistencies stop
   recurring.
   Evidence: §E3. (Principles #10, #3)

5. **Make the Arabic real in the instrument before trusting the instrument.**
   `?lng=ar` is overwritten by the account's stored language on every authenticated route
   (`router.tsx:105-111`, `fixtures.ts:254`), so both of each route's two axe scans run against the
   English, LTR UI — roughly 60 of 68 routes. The suite reports "137 scans in both languages" and
   `MOTS-PROGRESS.md` §6 repeats it. Fixing the fixture is a two-line change; what it will expose is not.
   Evidence: §A12, §F8. (Principles #8, #2)

---

## What to preserve, explicitly

The token layer and its conformance test; the domain vocabulary and state machines; the route paths;
`Dialog`'s focus handling; the isolated-widget failure model on the supplier dashboard; the
`asyncStateCoverage` and `tokenConformance` sweeps; and the restraint that earned #7 its 2 — no gradients,
no glass, no trend typography. The redesign is of composition, states and copy, not of the palette.
