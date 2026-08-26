# Accessibility — MOTS Supplier Portal

> **Status:** Baseline v1 · **Owner:** Design Lead · **Date:** 2026-08-26
> Target: **WCAG 2.2 Level AA** (foundational [§9](../architecture/00-foundational-decisions.md)).
> Tooling: **axe-core + Playwright + manual** (foundational §2). Consistent with
> [`DESIGN-SYSTEM.md`](./DESIGN-SYSTEM.md), [`RESPONSIVE-AND-RTL.md`](./RESPONSIVE-AND-RTL.md),
> [`UX-WRITING.md`](./UX-WRITING.md), [`UX-PRINCIPLES.md`](./UX-PRINCIPLES.md).

Accessibility is a **floor, not an aspiration** (UX Principle 2.7). This is a public-sector platform;
keyboard-only and screen-reader users, low-vision users, and users with motor/cognitive constraints
must complete every core flow: onboarding, RFQ authoring, proposal submission, evaluation, award.

---

## 1. Scope & standard

- **WCAG 2.2 AA** across both themes (light/dark) and both directions (RTL/LTR).
- Aligns with **OWASP ASVS L2** security posture where it intersects (e.g. accessible, non-timing-out
  session prompts) (foundational §9).
- Bilingual: screen-reader experience must be correct in **Arabic and English**, including correct
  `lang`/`dir` announcement and pronunciation.

---

## 2. WCAG 2.2 AA checklist mapped to components

Grouped by POUR. "Component" = where this is primarily enforced in the DS.

### 2.1 Perceivable

| SC | Requirement | Where enforced |
|---|---|---|
| 1.1.1 Non-text Content | All icons have `aria-label` or `aria-hidden`; charts have text-alternative + data-table fallback; uploaded-file thumbnails have alt | Icons, Charts §6.19, File upload |
| 1.3.1 Info & Relationships | Semantic HTML; form label↔control association; table `<th scope>`; headings hierarchical; lists as lists | Inputs, Tables, all layouts |
| 1.3.2 Meaningful Sequence | DOM order = reading order in both RTL/LTR; no CSS-only reordering that breaks meaning | Layout, RTL logical props |
| 1.3.4 Orientation | No lock to portrait/landscape; responsive both ways | Responsive strategy |
| 1.3.5 Identify Input Purpose | `autocomplete` tokens on name/email/phone/address/org fields | Onboarding forms |
| 1.4.1 Use of Color | **Status never color-only** — chip = color + icon + text; chart series get patterns/labels | Status chips §6.15, Charts |
| 1.4.3 Contrast (min) | Text ≥4.5:1, large text ≥3:1 — audited both themes | Color tokens §5 |
| 1.4.10 Reflow | No horizontal scroll at 320px width / 400% zoom (except intentional data tables/charts) | Responsive §3–4 |
| 1.4.11 Non-text Contrast | UI components & focus indicators ≥3:1 vs adjacent | Borders, focus ring |
| 1.4.12 Text Spacing | Layout survives increased line/letter/word/paragraph spacing | Type system, no fixed-height text |
| 1.4.13 Content on Hover/Focus | Tooltips/popovers dismissable (`Esc`), hoverable, persistent | Tooltip, Popover |

### 2.2 Operable

| SC | Requirement | Where enforced |
|---|---|---|
| 2.1.1 Keyboard | Every action keyboard-operable | All interactive components |
| 2.1.2 No Keyboard Trap | Focus can always leave; traps only in modal/drawer with `Esc` exit | Modal §6.12, Drawer §6.13 |
| 2.1.4 Character Key Shortcuts | Single-key shortcuts (command palette trigger) remappable/scoped to focus, not global-only | Command palette |
| 2.4.1 Bypass Blocks | "Skip to content" link; landmark regions | App shell |
| 2.4.3 Focus Order | Logical order matching visual/reading order (RTL-aware) | All flows |
| 2.4.7 Focus Visible | Always-visible focus ring (`--focus-ring`), ≥3:1 | Focus tokens |
| 2.4.11 **Focus Not Obscured (Min)** *(2.2)* | Focused element not hidden by sticky headers/footers/sticky action bar | App shell, sticky bars |
| 2.4.13 **Focus Appearance** *(2.2)* | Focus indicator meets size/contrast minimums | Focus tokens |
| 2.5.3 Label in Name | Visible label text is in the accessible name | Buttons, links |
| 2.5.7 **Dragging Movements** *(2.2)* | Any drag (file upload, reorder) has a click/keyboard alternative | File upload, list reorder |
| 2.5.8 **Target Size (Min)** *(2.2)* | Interactive targets ≥24×24px (≥44px on touch) | Buttons, chips, table actions |

### 2.3 Understandable

| SC | Requirement | Where enforced |
|---|---|---|
| 3.1.1 / 3.1.2 Language of Page/Parts | `lang`+`dir` on `<html>`; inline language switches marked (`lang="en"` inside Arabic) | i18n shell, bidi |
| 3.2.1 On Focus / 3.2.2 On Input | No surprise context change on focus/input; explicit submit | Forms, Selects |
| 3.2.4 Consistent Identification | Same icon/label for same function everywhere | DS governance |
| 3.2.6 **Consistent Help** *(2.2)* | Help/support access in a consistent location | App shell header/footer |
| 3.3.1 Error Identification | Errors named in text, tied to field | Inputs error state |
| 3.3.2 Labels/Instructions | Persistent visible labels; format hints | Inputs |
| 3.3.3 Error Suggestion | Errors say how to fix (UX-Writing formula) | Forms |
| 3.3.4 Error Prevention (legal/financial) | Award/submit/irreversible actions reversible, checked, or confirmed | Confirmation dialogs §6.18 |
| 3.3.7 **Redundant Entry** *(2.2)* | Previously entered info auto-populated/selectable across steps | Onboarding/proposal wizards |
| 3.3.8 **Accessible Authentication (Min)** *(2.2)* | No cognitive-function test to log in; paste allowed; MFA supports authenticator/passkey, no puzzle | Auth (Identity + MFA-ready) |

### 2.4 Robust

| SC | Requirement | Where enforced |
|---|---|---|
| 4.1.2 Name, Role, Value | Correct roles/states via Radix primitives + ARIA | All components |
| 4.1.3 Status Messages | `aria-live` for toasts, autosave, async results — no focus steal | Toasts §6.14, autosave |

---

## 3. Keyboard interaction model

Global and per-component keyboard contract. RTL note: **Left/Right arrows follow visual direction** —
under RTL, `→` moves toward inline-start; components map arrows to logical start/end.

| Context | Keys |
|---|---|
| Global | `Tab`/`Shift+Tab` move focus; `Esc` closes top layer; `Cmd/Ctrl+K` command palette; skip-link on first `Tab` |
| Buttons/links | `Enter`/`Space` activate |
| Inputs | Type; `Esc` clears combobox/search where applicable |
| Select/Combobox | `Enter`/`Space`/`↓` open; `↑`/`↓` navigate; type-ahead; `Enter` select; `Esc` close; `Home`/`End` |
| Checkbox/Radio | `Space` toggle checkbox; arrows move within radio group |
| Tabs | Arrows move (direction-aware), `Home`/`End`, `Tab` exits tablist to panel |
| Menu (kebab/user) | `Enter`/`↓` open; arrows navigate; type-ahead; `Esc` close, return focus |
| Modal/Drawer | Focus trapped; `Esc` close (guarded if unsaved); focus returns to trigger |
| Date picker | Arrows move by day; `PageUp/Down` month; `Shift+PageUp/Down` year; `Enter` select; `Esc` close |
| Table | `Tab` to actionable cells; arrow-key grid nav optional; sortable headers `Enter`/`Space`; row selection `Space` |
| Stepper/Tabs wizard | Arrows between steps if non-linear; `Enter` activate |
| Toast action | Reachable via `Tab`; action `Enter` |

**No keyboard trap** (2.1.2) — the only intentional traps are modal/drawer, always escapable.

---

## 4. Focus management

- **Visible focus** everywhere via `--focus-ring` (2px surface gap + 4px brand ring; brand-300 in dark)
  — meets 2.4.7, 2.4.13, 1.4.11.
- **Focus not obscured** (2.4.11): sticky header/footer/mobile action bar use `scroll-margin` so a
  focused element scrolls fully into view, never hidden behind a sticky layer.
- **Route changes:** on navigation, move focus to the new page's `<h1>` (or a "skip to content"
  target) and announce the page title via a polite live region so SR users know they moved.
- **Overlays:** modal/drawer/menu trap focus, return focus to the trigger on close; background made
  inert (`inert`/`aria-hidden`).
- **Dynamic content:** newly revealed relevant content (validation summary, expanded row) receives or
  is announced to focus appropriately; deletions move focus to a sensible neighbor.
- **Never** remove focus outlines without an equal-or-better replacement; `:focus-visible` used so mouse
  users aren't spammed but keyboard users always see it.

---

## 5. Contrast & color tokens

All pairings verified to **WCAG 2.2 AA** in light **and** dark (see [`DESIGN-SYSTEM §2`](./DESIGN-SYSTEM.md)).

| Pairing | Ratio target | Notes |
|---|---|---|
| `--color-text-primary` on `--color-bg-surface` | ≥4.5:1 | Body/headings, both themes |
| `--color-text-secondary` on surface | ≥4.5:1 | Secondary text |
| `--color-text-muted` on surface | ≥4.5:1 | Kept above min despite lower emphasis |
| Semantic `*-fg` on `*-bg` (chips/alerts) | ≥4.5:1 | Success/warning/danger/info text on tint |
| Primary button inverse text on `--brand-600` | ≥4.5:1 | White on evergreen |
| Focus ring vs adjacent | ≥3:1 | Non-text contrast |
| Input border `--color-border-input` vs surface | ≥3:1 | Perceivable field boundary |
| Chart series adjacent colors | ≥3:1 | Plus non-color encoding |

**Color is never the sole channel** (1.4.1): status = color + icon + text; charts add labels/patterns;
form errors add icon + text + border + `aria-invalid`. `--accent-gold` text usage uses `-600` on light
to stay AA (pure gold on white fails, so gold-on-light is reserved for large/graphical, with `-600` for
text). Warning `-600` used for any warning *text* to meet AA.

---

## 6. ARIA patterns per component

Prefer **native semantics + Radix primitives** (which ship correct ARIA); add ARIA only to fill gaps.

- **Modal (Dialog):** `role="dialog"` `aria-modal="true"`, `aria-labelledby` (title), `aria-describedby`
  (body); focus trap; `Esc`; background inert.
- **Menu (Dropdown):** trigger `aria-haspopup="menu"` `aria-expanded`; `role="menu"` + `menuitem`;
  roving tabindex; type-ahead.
- **Tabs:** `role="tablist"`/`tab`/`tabpanel`, `aria-selected`, `aria-controls`, roving tabindex.
- **Combobox/Select:** WAI-ARIA combobox pattern — `role="combobox"` `aria-expanded`
  `aria-controls` `aria-activedescendant`; listbox options `aria-selected`.
- **Tables:** native `<table>` with `<caption>`, `<th scope="col|row">`, `aria-sort` on sortable
  headers; selection via labeled checkboxes; row actions labeled ("Actions for RFQ-2026-000123").
- **Toasts / status messages:** container `role="status"`/`aria-live="polite"` for success/info,
  `role="alert"`/`aria-live="assertive"` for errors; **do not move focus** to toasts; action buttons
  reachable via Tab.
- **Autosave indicator:** polite live region announces "Saved / Saving…" without interrupting typing.
- **Stepper:** ordered list; current step `aria-current="step"`; completed/blocked states in the
  accessible name ("Step 2 of 5, Documents, current" / "Step 4, blocked: 2 documents pending").
- **Breadcrumb:** `nav` with `aria-label="Breadcrumb"`, current `aria-current="page"`.
- **File upload:** button + hidden input with label; dropzone `aria-describedby` constraints; each file
  row announces name + status; progress via `role="progressbar"` `aria-valuenow`; drag has a keyboard
  alternative (2.5.7).
- **Pagination:** `nav aria-label`, current page `aria-current="page"`, prev/next labeled.
- **Charts:** wrapper `role="img"` + descriptive `aria-label` (summary) **and** a visually-hidden data
  table (`.sr-only`) as the real accessible content.

---

## 7. Forms & error accessibility (critical path)

Onboarding and proposal submission are the highest-stakes flows; form a11y is non-negotiable.

- **Labels** always visible and programmatically associated (`<label for>`); never placeholder-as-label.
- **Required** conveyed in text ("Required / مطلوب") and `aria-required`, not asterisk-only.
- **Instructions/format hints** persistent, tied via `aria-describedby`.
- **Inline validation** on blur/submit (forgiveness — not per-keystroke): errored field gets
  `aria-invalid="true"` and `aria-describedby` pointing to the error text; error text follows the
  [UX-Writing error formula](./UX-WRITING.md) (what happened / what to do / data saved? / safe to
  retry?).
- **Error summary** at submit: a focusable summary region (`role="alert"` or moved focus) listing each
  error as a link jumping to its field — essential for long onboarding forms and SR users.
- **Grouped controls** (address, radio sets) use `<fieldset>`/`<legend>` or `role="group"` +
  `aria-labelledby`.
- **Autosave** state announced politely; on submit failure, no data loss — the form retains input and
  says so (Redundant Entry 3.3.7).
- **Async submit** disables the primary button with `aria-busy` and an accessible "Submitting…" status.
- **Currency/number** fields expose plain semantics; formatting/numeral system does not break SR
  reading (value read as the number, not the glyph variant).

---

## 8. Testing approach (axe + manual)

Automated tools catch ~30–40%; the rest is manual. Both are gates in the UX Definition of Done
(UX-Principles §6).

### 8.1 Automated
- **axe-core** via `@axe-core/playwright` and Vitest + React Testing Library + `jest-axe`-style checks
  on every component story (light/dark × RTL/LTR) — CI fails on new violations (foundational §2 test
  stack).
- **Storybook a11y addon** for in-workshop checks during development.
- **Playwright E2E** runs core flows keyboard-only and asserts focus order, live-region announcements,
  and no keyboard traps.
- Lighthouse a11y budget in CI as a secondary signal.

### 8.2 Manual (required — automation cannot verify these)
- **Keyboard-only pass** of every flow (no mouse): onboarding, upload, RFQ author/publish, proposal
  submit, evaluation scoring, award approve.
- **Screen readers:** VoiceOver (Safari/macOS, iOS), NVDA (Firefox/Windows), TalkBack (Android Chrome)
  — in **Arabic and English**, verifying `lang`/`dir`, status announcements, table navigation, form
  errors.
- **Zoom/reflow** to 400% and 320px width; **text-spacing** bookmarklet.
- **Reduced-motion** and **forced-colors / Windows High Contrast** rendering.
- **Contrast** spot-checks of real screens (not just tokens) in both themes.
- Target-size and focus-not-obscured checks on sticky-bar screens (mobile supplier flows).

### 8.3 Cadence
- Per-component: axe + keyboard + SR spot-check before merge.
- Per-slice: full manual keyboard + one SR pass in Arabic on the new flow (foundational §11 gate).
- Release: full matrix regression (axe suite + manual SR sweep of core journeys).

---

## 9. Accessibility acceptance checklist (per screen)

- [ ] Reachable & operable **keyboard-only**; logical, RTL-aware focus order.
- [ ] Visible focus everywhere; focus not obscured by sticky bars.
- [ ] All contrast AA in **light and dark**.
- [ ] No color-only meaning (status = color+icon+text).
- [ ] Correct roles/names/states (Radix + ARIA); verified with a screen reader in **Arabic**.
- [ ] Forms: visible labels, `aria-invalid`+`describedby`, error summary, no data loss on error.
- [ ] Live regions for toasts/autosave/async — no focus theft.
- [ ] Targets ≥24px (≥44px touch); drag has keyboard alternative.
- [ ] Reflow at 320px/400% with no loss; reduced-motion honored.
- [ ] axe: zero new violations.
