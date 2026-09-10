# Evidence — MOTS Supplier Portal, 2026-09-10

Five subagents, facts only, each forbidden from scoring. Full returns in `raw-evidence.json`.
Everything below carries a source. Measurements marked MEASURED were taken against the live app and
the seeded database; anything derived says so.

---

## E1. Structural

**Interactive elements, MEASURED live at 1440×1000 as officer@mots.local** (Playwright, excluding
`disabled`, `[hidden]`, `aria-hidden` and sub-2px):

| Screen | Focusable | In `main` | In chrome |
|---|---|---|---|
| Tender list | 41 | 25 | 16 |
| Procurement dashboard | 30 | 14 | 16 |
| Tender workspace | 23 | 7 | 16 |
| Supplier onboarding | 23 | 6 | 17 |
| **Back-office dashboard** | **15** | **0** | **15** |
| Comparison tab | 16 | 0 | 16 |

Chrome is a near-constant 16 on every back-office screen. On the officer's own landing screen and on
the comparison tab it is **100%** of the screen's interactivity.

**Nesting.** Deepest primary tree is the tender workspace: 10 React components shell→leaf, 13 DOM
levels below `main`. Three of the 13 are unnamed layout divs — `RfqDetailPage.tsx:395`, `:480`, `:553`.

**Steps to the primary tasks, MEASURED.**
- Officer, draft tender → recommended award: **14 committing actions** plus N invitations, across 4
  URLs, ~20 form controls, and one step that requires a different account (segregation of duties,
  correct).
- Supplier, register → submitted application: **8 committing actions** across 5 in-app routes plus
  **2 stops outside the product** (the verification email, then the invitation email), a minimum of
  **28 typed fields**, 2 file pickers, 1 category selection.

**Repeated affordances.**
- The card's `<h2>` and the table's screen-reader `<caption>` are the same string on **26 sites**, so
  it is announced twice. Live `innerText`: `"Items Items # TITLE CATEGORY QUANTITY"`,
  `"Approvals Approvals STEP DECISION COMMENTS"`. `Card.tsx:43` + `Table.tsx:38`.
- **11 list screens** render an `<h1>` and then a card `<h2>` for the same list — visible in
  `shots/officer-tender-list.png` as "RFQs" then "RFQ List". `RfqListPage.tsx:59` and `:65`.
- Two labelling mechanisms for text entry: 108 `<Field label=` sites vs 29 `<Input aria-label=` sites
  whose label disappears once the field has content. Both appear on one screen —
  `RfqDetailPage.tsx:691, :692, :717, :756`.
- "New RFQ" exists twice with two behaviours; on the dashboard it is a `<Button>` nested inside a
  `<Link>`, yielding two consecutive focus stops with the same name for one destination.
  `ProcurementDashboardPage.tsx:65-67`.

**Removable, each measured.** The Owner column (25/25 identical values, `RfqListPage.tsx:91,:105`);
the sr-only caption (26 sites); the Received-proposals and Comparison ButtonLinks duplicating the tab
strip (`RfqDetailPage.tsx:903,:906`); the Requirements and RFQ-attachments cards on any non-draft
tender (0 controls, empty text, `:703`/`:767`); the "Decisions" heading over a single card (`:866`);
a link labelled with its own card's title (`SupplierDashboardPage.tsx:187` and `:223`); one of the two
Search controls (sidebar row and top-bar box, same destination); the footer Help on signed-in screens.

**Dead props, 0 call sites each.** `Dialog.trigger` (`Dialog.tsx:12`), `Input.invalid`
(`Input.tsx:5,38,42,57` — `Field.tsx:45` passes `aria-invalid` instead, so the danger branch is never
taken), `NotificationBell.to` (`NotificationBell.tsx:22` — its default `/notifications` is what makes
the back-office bell answer 403).

---

## E2. Visual

**Spacing.** Declared: a 4px grid, `--space-0 … --space-16` (`tokens.css:173-175`). **Shipped:
Tailwind's own defaults.** There is no `tailwind.config.*` and no `@theme` block in `index.css`, so
the declared scale has no consumers. Off-scale values ship: 6px via `gap-1.5`, `py-1.5`, `mt-1.5`.

**Type.** Nine steps declared (`tokens.css:157-159`). Actual usage: `--text-body-sm` ×156,
`--text-caption` ×46, `--text-body` ×17, `--text-h4` ×15 — and `--text-h1` ×1, `--text-h2` ×1. The
product is carried almost entirely by 13px and 12px. Zero Tailwind `text-*` size utilities ship.

**Colour.** 50 semantic `--color-*` tokens resolving to 39 distinct values (38 hex + 1 rgba), over 35
primitives in 7 ramps.

**Lowest text contrast: 1.93:1** — `--color-text-disabled` `#A6B2AC` on `--color-bg-sunken` `#EFF1EF`.
MEASURED in `shots/supplier-onboarding.png` on every value of the read-only application form:
"Demo Supplies Co", "RC-DEMO-0001", the entity type, the currency, the phone number.
`Input.tsx:29,:53` and `Select.tsx:45-46`. WCAG exempts disabled controls — but this is the supplier's
own submitted content being displayed to them, not an inert control.

Second lowest: 2.14:1, white on `#87BCAE`, the disabled Search button in `shots/officer-search.png`.

**States.** All six present. Rough in three:
- *Focus* — `--focus-ring` is applied by **4 shipped files**. MEASURED on `/back-office/rfqs`: a
  sidebar link and an in-page link have `boxShadow: none` and fall back to the 1px UA outline; a
  Button shows the token ring. `ACCESSIBILITY.md:114-115` claims "Visible focus everywhere".
- *Loading* — rendered two ways: inside titled cards with the heading and filters still on screen
  (`officer-reports.png`), and as bare bars on the page ground with no heading
  (`officer-ministry-awards.png`).
- *Empty* — one implementation, a bare line (`ListScreen.tsx:151`). No distinction between "nothing
  yet" and "nothing matched your filter".

**Inconsistencies.** The same From/To date range is built twice: on the procurement dashboard a raw
`<input type="date">` 29px tall with a `#E1E6E3` border (**1.26:1**); on reports a shared `<Input>`
39px tall with a `#7C8A83` border (3.61:1). `ProcurementDashboardPage.tsx:57-64` vs
`ReportsPage.tsx:70-73`.

**Dated markers: none found.** The only `linear-gradient` in the codebase is the skeleton shimmer.
Zero `textShadow`, zero inset shadows, zero bevels. Flat fills throughout; shadows are single soft
layers. Checked across nine screenshots and greps over all of `src/`.

---

## E3. Copy and honesty

**Volume.** 2,024 English and 2,027 Arabic user-facing strings. Keys present in one language and
missing in the other: **0**. Arabic values identical to their English: **0**. The 3-string surplus is
CLDR plural variants Arabic needs.

**Dark pattern — consent to a document that does not exist.** The onboarding Terms & Conditions card
asks the supplier to confirm "I confirm I have read and accept the Supplier Portal Terms & Conditions
and data-processing notice" and records the acceptance with a version. The card renders the checkbox
and a button and **nothing else** — no link, no modal, no text (`OnboardingPage.tsx:828-857`). A
repo-wide grep finds no terms route and no terms document. The product's own About page says "Terms
of use and the privacy notice have not been issued yet." In `shots/supplier-onboarding.png` the card
shows one line: "Version v1 of the Terms & Conditions was accepted on 09 Sept 2026, 23:29."

**Label contradicts behaviour — withdrawal.** The dialog says "Withdrawing is final. You cannot
re-enter this tender, and a withdrawn proposal cannot be restored."
(`config.ts:3372` → `SupplierProposalPage.tsx:447`). The backend allows re-entry:
`StartProposalHandler` guards with
`if (existing is not null && existing.State != ProposalState.Withdrawn) return existing;` — a
withdrawn proposal falls through to `Proposal.Create(...)`. The product's own Help page agrees with
the code and contradicts the dialog.

**Label contradicts state — the read-only banner.** `isReadOnly = !isEditableState`
(`OnboardingPage.tsx:536-537`), and the banner is one fixed string, "Your application is with a
reviewer" (`:589`). Shown to every non-editable state. In `shots/supplier-onboarding.png` an
**Approved** supplier is told their application is with a reviewer, under a page titled "Complete
Your Supplier Profile" — for a profile that is complete and approved.

**Jargon.** "RFQ" appears 27 times in the English catalogue, including strings shown to the supplier
whose own sidebar calls the same object "Tenders" — `supplierDashboard.emptyBody`,
`notifications.emptyBody`. "Addenda" is rendered on the supplier's tender screen. A raw category code,
`tour_operations`, is rendered as a value in `shots/workspace-tender.png`.

**Inflation.** One: `help.subtitle` claims "Answers to what people ask about this system most" over a
hard-coded 7-topic array, while its sibling string admits no support channel exists.

**Zero exclamation marks in either language.** A 40-term superlative sweep over all 1,789 English
strings returned only functional uses of "complete", "always", "never".

---

## E4. Weight and friction

**Initial JS, MEASURED from a fresh production build:** 705,139 bytes raw / **218,939 gzip** for the
officer dashboard — entry plus 5 preloaded chunks plus the 6 the route adds. Gzip figures are the real
emitted `.gz` files, not estimates.

**Requests:** 26 derived for production (`dist` import graph), 7 XHR measured live.

**Time to interactive, MEASURED.** Production, Slow 4G + 4× CPU throttle, login route: FCP 3,140 ms,
LCP 3,788 ms, DCL 2,610 ms, **zero long tasks**. Unthrottled localhost: DCL 228 ms.

**Idle animations: ZERO,** measured — `document.getAnimations()` returned an empty array on two idle
loaded screens. One `@keyframes` exists in `src/` (the skeleton shimmer); `animate-spin` has exactly
one consumer, gated on `isLoading`.

**Interruptions on load: none.** No modal, no banner; both banner components return null on healthy
data, and the ERP one uses `role="status"` rather than `role="alert"`.

**prefers-reduced-motion: respected twice** — a global wildcard (`index.css:80-87`) and a scoped
skeleton rule (`:111-116`).

**Dark mode: NOT honoured.** The theme is fully built, shipped to every user as bytes, and
unreachable. Recorded by the project itself in `themeReachability.test.ts:36`.

---

## E5. Accessibility

**Contrast guard** asserts 83 pairs per theme, read from `tokens.css` — but three shipped tokens are
outside its denominator: `--color-danger-solid-hover` (the destructive button's hover fill),
`--color-bg-overlay`, `--color-chrome-border`.

**Axe sweep** covers all 70 routes × 2 languages, `wcag2a/2aa/22aa` tags. What it does **not** cover:
- axe-core 4.13 carries exactly **one** rule under `wcag22aa` (`target-size`). SC 2.4.11 Focus Not
  Obscured, 2.4.13 Focus Appearance and 3.3.8 Accessible Authentication are unchecked by the sweep
  that names WCAG 2.2.
- Only `violations` is asserted; the `incomplete` bucket is discarded. `/back-office/review` returns
  2 incomplete colour-contrast nodes the assertion cannot see.
- Never an interacted state, a narrow viewport, or the dark theme. Scan viewport is 1280×720.
- It runs on mocked fixtures returning 1–2 rows with `hasMore:false`, against a live database of 31
  suppliers and 25 tenders. Pagination, long tables, empty, loading and error states are outside
  every scanned DOM.
- Landmark structure is checked by nothing: `landmark-one-main`, `region`, `page-has-heading-one`,
  `heading-order` are all best-practice-tagged and outside the sweep's tags.

**Skip link moves no focus.** `#main` has no `tabindex="-1"`; after Enter, `document.activeElement` is
`BODY` (measured live). The unit test asserts the href, the hidden-until-focused behaviour, the
first-Tab position and the existence of `id="main"` — never what happens after activation.

**Tablist without arrow keys.** `/evaluation` renders `role="tablist"` with three `role="tab"` and
`aria-selected`, but no `aria-controls`, no `tabpanel`, and ArrowRight does not move focus.
`ACCESSIBILITY.md:96` states the contract as "Arrows move".

**The keyboard suite's denominator is stale.** Its comment claims a grep found zero matches for
`role="tab"` and `tabIndex`; re-running it today finds 1 and 2.

**The focus-order test asserts no order.** It counts distinct elements over 25 Tab presses and passes
at ≥10.

**Back-office landing screen: 0 focusable elements in `main`.** A 16-stop Tab sweep never enters it.
