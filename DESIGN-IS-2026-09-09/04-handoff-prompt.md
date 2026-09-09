# 04 · Handoff

One prompt, self-contained. The next session will not see the audit unless it is quoted in, so the verdict
and the moves are inlined below rather than referenced.

````
/make-plan Redesign the MOTS Supplier Portal's interaction and composition layer — the supplier journey
(register → onboarding wizard → tender → proposal) and the back-office reviewer/buyer surface (review
queue, application review, RFQ authoring and detail, evaluation, comparison, award). Current design failed
a Dieter Rams audit at 12/30, with 1/3 on principles #1 innovative, #2 useful, #3 aesthetic, #4
understandable, #5 unobtrusive, #6 honest, #8 thorough and #10 as-little-design-as-possible.

Verdict paragraph (quoted from the audit):
> The interaction and composition layer of this product scores 12/30 against Rams, and the failures are
> systemic rather than screen-deep: the shared vocabulary is missing two states the spec requires, five
> labels describe behaviour the code does not perform, and the components that would fix the duplication
> already exist and are used in two files out of twenty-six.

Why redesign and not refine: the total is 12 against a threshold of 20, and the failures are in the shared
layer — the input primitives, the copy layer, the page composition pattern and the navigation — so fixing
them screen by screen would mean fixing them twenty-six times.

What this is NOT a verdict on: the engineering. 1,243 backend tests, 630 frontend tests and 137 axe scans
pass on this commit. The domain model, API contract, concurrency guards, audit trail and permission system
are out of scope and are not in question.

PRESERVE from the current design:
- The whole token layer — colour, type, radius, elevation, motion and z-index — at
  src/frontend/src/styles/tokens.css, and the test that enforces it,
  src/frontend/src/styles/tokenConformance.test.ts. It was corrected on 2026-09-09 and it is the reason
  principle #7 (long-lasting) scored 2: warm-stone neutrals, evergreen teal, system fonts, 1px borders,
  the faintest shadow on the scale. No gradients, no glass, no trend typography. Do not restyle it.
- The domain vocabulary and every state machine (onboarding, RFQ, proposal, evaluation, award) and all 68
  route paths. This redesign changes composition, states and copy — not URLs and not the model.
- src/frontend/src/components/ui/Dialog.tsx — it traps focus, restores it to the opener and handles
  Escape, proven by tests/e2e/app-keyboard.spec.ts:21-58. It is the correct model for the two hand-rolled
  overlays to follow.
- The isolated-widget failure model on the supplier dashboard
  (src/frontend/src/routes/SupplierDashboardPage.tsx:21-24) — one widget failing must not blank the page.
- src/frontend/src/routes/asyncStateCoverage.test.ts and the four-state discipline it enforces.

DISCARD:
- The "eleven equal cards" page composition. Evidence: src/frontend/src/routes/back-office/RfqDetailPage.tsx
  — 1,256 lines, 78 interactive elements, JSX depth 15, fifteen sections, eight of the buyer's eleven state
  transitions, and seven of eleven cards empty on a fresh tender. Caused failure on principles #5 and #10.
- Hand-rolled copies of components that already exist: 24 copies of PageHeading's exact markup, 6 ternary
  chains where ListCard exists, 5 hand-rolled "load more" buttons, 6 hand-rolled filter rows, 3
  independent document-status rows, 5 file uploaders. Evidence: the shared components are in
  src/frontend/src/components/ui/ListScreen.tsx and are used in exactly 2 route files. Caused failure on
  principles #10 and #3.
- `<a href>` wrapping `<Button>` as the way out of a workspace:
  src/frontend/src/routes/back-office/RfqDetailPage.tsx:1051, :1054, :1058 — three full page reloads and a
  nested interactive element that axe does not flag. Caused failure on principles #8 and #2.

Top 5 moves from the audit (verbatim):
1. #8/#6 — Give the input primitives their missing states, and make the read-only case real. `Input`
   implements neither `disabled` nor `read-only` and sets colour inline, so it overrides the browser's own
   disabled rendering; `Select` accepts no `disabled` prop at all. A submitted application therefore
   presents an editable-looking form that silently refuses input, and three comboboxes that still change
   with nothing to save them. Evidence: src/frontend/src/components/ui/Input.tsx:13-35,
   src/frontend/src/components/ui/Select.tsx, src/frontend/src/routes/OnboardingPage.tsx:552-574,
   docs/ux/DESIGN-SYSTEM.md §6.2 (which requires both states), and --color-text-disabled defined in both
   themes at tokens.css:76,:193 and consumed by nothing.
2. #6/#4 — Make the copy layer answerable to the code, the way the token layer now is. Five labels
   describe behaviour that does not happen, one screen contradicts itself on the same interaction, and a
   raw translation key renders as a field label on the reviewer's screen. There are 53 dynamic t() call
   sites and no guard against a missing key — the exact gap tokenConformance closed for tokens. Evidence:
   src/frontend/src/routes/ReviewApplicationPage.tsx:277 renders t(`onboarding.fields.${f}`) for a field
   named `defaultCurrency` while i18n/config.ts:2448 defines `currencyCode` — in both languages;
   i18n/config.ts:3229 promises "returns the proposal for re-review" where Proposal.cs:428 sets Revised
   only; i18n/config.ts:2657 names two always-on notification families that
   NotificationClassification.cs:22-27,47-52 says do not exist; RfqDetailPage.tsx:980 renders a control for
   a state Rfq.cs:528-530 cannot produce; RfqDetailPage.tsx:475-476 collects an audit reason via
   window.prompt and silently does nothing if dismissed.
3. #5/#2/#10 — Rebuild the buyer's workspace around what happens next, not around eleven equal cards.
   RfqDetailPage.tsx is 1,256 lines, 78 interactive elements, JSX depth 15, fifteen sections and eight of
   the eleven state transitions in the buyer's whole job — and the one card that could direct the officer
   renders "No next action is currently available." The exits to bids, comparison and award are <a href>
   full page reloads wrapping buttons, inside a conditional block.
4. #10/#3 — Use the component vocabulary that already exists, and delete what it replaces. PageHeading is
   used in 2 files while its exact markup is hand-rolled in 24; ListCard 2 against 6; LoadMore 2 against 5;
   FilterBar 2 against 6. This is the cheapest of the five moves and it is what makes the aesthetic
   inconsistencies stop recurring.
5. #8/#2 — Make the Arabic real in the instrument before trusting the instrument. ?lng=ar is overwritten by
   the account's stored language on every authenticated route (src/frontend/src/router.tsx:105-111,
   tests/e2e/fixtures.ts:254 returns language:'en'), so both of each route's two axe scans run against the
   English, LTR UI — roughly 60 of 68 routes. The suite reports "137 scans in both languages" and
   MOTS-PROGRESS.md §6 repeats the claim. Fixing the fixture is a two-line change; what it will expose is
   not.

Redesign principles in priority order:
1. Honest (#6) — every label, badge and hint maps 1:1 to what the code does, and a control that cannot be
   used says so by looking unusable. Success: no label→behaviour mismatch survives, and a disabled field is
   visibly disabled.
2. Thorough (#8) — the six states (empty, loading, error, success, focus, disabled) exist on every
   primitive, not most of them; the skip link docs/ux/ACCESSIBILITY.md:52 requires exists; the two
   hand-rolled aria-modal overlays trap focus like Dialog does. Success: the axe suite is widened past
   wcag-tagged rules so landmark, heading-order and skip-link failures can be seen at all.
3. As little design as possible (#10) — one implementation per affordance, and a screen shows what is
   there rather than a placeholder for everything that could be. Success: PageHeading/ListCard/LoadMore/
   FilterBar used everywhere they apply, and the buyer's workspace leads with the next action.

Deliverables for the plan:
- New information architecture for the buyer's workspace, derived from the eleven state transitions rather
  than from the current card order — with the three exits (bids, comparison, award) as first-class
  navigation, not <a href> inside a conditional card.
- New primary flow for the supplier wizard, low-fi and labelled, compared side by side with the current
  eight-route/six-section shape, and an explicit answer to where the submit control belongs relative to
  the requirements it gates.
- A states checklist per primitive (empty, loading, error, success, focus, disabled, read-only) with the
  token each state consumes — --color-text-disabled already exists and is used by nothing.
- A copy-conformance instrument: every t() key resolves, and every dynamic t(`${var}`) site enumerates its
  possible keys. Model it on src/frontend/src/styles/tokenConformance.test.ts — assert the denominator
  before the rule, and carry a revert-to-red control.
- A migration path: this product has 142 delivered screens and is in a demonstration environment. The plan
  must say which screens move first, what "done" means per screen, and how a half-migrated product stays
  coherent — not a big-bang cutover.
- Cutover criteria: what has to be true before the old composition is deleted.

Anti-patterns to guard against:
- Porting the eleven-equal-cards structure under new styling.
- Keeping both compositions behind a flag indefinitely.
- Redesigning toward a trend — the audit scored #7 (long-lasting) at 2 precisely because the current
  visual language has no trend markers. Do not introduce any.
- Treating the PRESERVE list as optional. The token layer and its test are not up for renegotiation.
- Widening the scope into the domain, the API or the permission model. None of them failed this audit.
````
