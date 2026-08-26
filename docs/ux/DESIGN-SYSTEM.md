# Design System — MOTS Supplier Portal

> **Status:** Baseline v1 · **Owner:** Design Lead · **Date:** 2026-08-26
> The bespoke component library and token source of truth. 100% consistent with the canonical tokens in
> [`00-foundational-decisions.md §7`](../architecture/00-foundational-decisions.md). Built on **Tailwind
> CSS v4 + Radix UI primitives** (NOT MUI/AntD/Bootstrap), themed via CSS custom properties.
> Companions: [`UX-PRINCIPLES.md`](./UX-PRINCIPLES.md) · [`RESPONSIVE-AND-RTL.md`](./RESPONSIVE-AND-RTL.md)
> · [`ACCESSIBILITY.md`](./ACCESSIBILITY.md) · [`UX-WRITING.md`](./UX-WRITING.md)

---

## 1. Foundations & philosophy

- **Brand direction (canonical):** trustworthy, calm, premium. Deep **evergreen-teal** primary
  (heritage/tourism, deliberately distinct from generic SaaS blue), **warm-stone** neutrals, restrained
  **gold** accent reserved for awards / KPIs / genuine significance.
- **Themeable by tokens only.** Components never hard-code a hex value; they consume semantic CSS
  variables so light/dark and future white-label re-skins are token swaps.
- **Two-layer token model:** _primitive_ tokens (raw scales, `--brand-500`) → _semantic_ tokens
  (`--color-bg-surface`, `--color-text-primary`) that components actually use. Only semantic tokens
  change between light and dark.
- **Storybook is the workshop** (foundational §2): every component below is developed and visual-QA'd in
  Storybook with light/dark × LTR/RTL × states matrices.

---

## 2. Color tokens

### 2.1 Primitive palette — Brand (evergreen-teal)

Canonical brand ramp expanded to a complete usage scale.

| Token | Hex | Intended usage |
|---|---|---|
| `--brand-50` | `#ECF6F3` | Subtle brand tint backgrounds, hover on ghost buttons |
| `--brand-100` | `#D2EBE4` | Selected-row tint, brand chip background |
| `--brand-200` | `#A6D6C9` | Disabled brand fills, dividers on brand surfaces |
| `--brand-300` | `#6FBAA8` | Secondary illustrations, focus ring on dark |
| `--brand-400` | `#3E9A85` | Hover state of primary in dark theme |
| `--brand-500` | `#1F8069` | Brand accent, active borders, links on light |
| `--brand-600` | `#136A57` | **Primary button fill (light)**, primary brand surface |
| `--brand-700` | `#0F5647` | Primary button hover (light), pressed |
| `--brand-800` | `#0D453A` | Primary button pressed, dark brand surface |
| `--brand-900` | `#0A3730` | Deep brand text on tint, dark app chrome |

### 2.2 Primitive palette — Neutrals (warm stone)

| Token | Hex | Intended usage |
|---|---|---|
| `--n-50` | `#FAF9F7` | App background (light) |
| `--n-100` | `#F3F1ED` | Sunken surfaces, table zebra (light) |
| `--n-200` | `#E7E3DC` | **Default 1px borders (light)**, dividers |
| `--n-300` | `#D6D0C6` | Input borders, disabled borders |
| `--n-400` | `#B4AB9D` | Placeholder text, disabled text (light) |
| `--n-500` | `#8B8173` | Muted/secondary text (light) |
| `--n-600` | `#6B6255` | Secondary text, icons |
| `--n-700` | `#4C463B` | Body text on light |
| `--n-800` | `#302B24` | Headings (light), dark surfaces |
| `--n-900` | `#1C1B19` | Primary text (light), app background (dark) |

> Warm-stone neutrals (not pure gray) are what separate the premium feel from a cold admin template.

### 2.3 Primitive palette — Accent & semantic

| Token | Hex | Usage |
|---|---|---|
| `--accent-gold-500` | `#C8A045` | **Sparingly:** award badges, KPI highlights, premium moments only |
| `--accent-gold-600` | `#A98633` | Gold hover/pressed, gold text on light (contrast-safe) |
| `--success-500` | `#1E874B` | Approved, success toasts, positive deltas |
| `--success-600` | `#166B3B` | Success text on light (AA) |
| `--warning-500` | `#B7791F` | Expiring soon, pending, caution |
| `--warning-600` | `#8F5E17` | Warning text on light (AA) |
| `--danger-500` | `#C0392B` | Rejected, destructive, errors |
| `--danger-600` | `#9C2C20` | Danger text on light (AA), danger button hover |
| `--info-500` | `#2563A6` | Informational, neutral status, sync-in-progress |
| `--info-600` | `#1D4E82` | Info text on light (AA) |

Each semantic color carries a matching **tint** for chip/alert backgrounds:
`--success-50 #E9F5EE` · `--warning-50 #FBF2E1` · `--danger-50 #FBEAE7` · `--info-50 #E8F0F8`
(dark theme uses ~12–16% alpha overlays of the 500 value instead — see §2.5).

### 2.4 Semantic tokens — Light theme (`:root`)

Components consume these, never primitives directly.

```css
:root {
  color-scheme: light;

  /* Surfaces */
  --color-bg-app:        var(--n-50);
  --color-bg-surface:    #FFFFFF;
  --color-bg-sunken:     var(--n-100);
  --color-bg-inset:      var(--n-100);
  --color-bg-hover:      var(--n-100);
  --color-bg-selected:   var(--brand-100);
  --color-bg-overlay:    rgba(28, 27, 25, 0.48); /* modal scrim */

  /* Text */
  --color-text-primary:   var(--n-900);
  --color-text-secondary: var(--n-600);
  --color-text-muted:     var(--n-500);
  --color-text-disabled:  var(--n-400);
  --color-text-inverse:   #FFFFFF;
  --color-text-link:      var(--brand-600);
  --color-text-brand:     var(--brand-700);

  /* Borders */
  --color-border:         var(--n-200);
  --color-border-strong:  var(--n-300);
  --color-border-focus:   var(--brand-500);
  --color-border-input:   var(--n-300);

  /* Brand / interactive */
  --color-brand-solid:        var(--brand-600);
  --color-brand-solid-hover:  var(--brand-700);
  --color-brand-solid-active: var(--brand-800);
  --color-brand-subtle:       var(--brand-50);

  /* Focus ring */
  --focus-ring: 0 0 0 2px var(--color-bg-surface), 0 0 0 4px var(--brand-500);

  /* Semantic surfaces + text */
  --color-success-fg: var(--success-600); --color-success-bg: var(--success-50); --color-success-solid: var(--success-500);
  --color-warning-fg: var(--warning-600); --color-warning-bg: var(--warning-50); --color-warning-solid: var(--warning-500);
  --color-danger-fg:  var(--danger-600);  --color-danger-bg:  var(--danger-50);  --color-danger-solid:  var(--danger-500);
  --color-info-fg:    var(--info-600);    --color-info-bg:    var(--info-50);    --color-info-solid:    var(--info-500);
  --color-gold-fg:    var(--accent-gold-600); --color-gold-solid: var(--accent-gold-500);
}
```

### 2.5 Semantic tokens — Dark theme

Applied via `.theme-dark` class **or** `@media (prefers-color-scheme: dark)`. Only semantic tokens
change; the dark palette is warm (not blue-black) to preserve brand warmth. Contrast re-audited to AA.

```css
.theme-dark, :root[data-theme="dark"] {
  color-scheme: dark;

  --color-bg-app:        var(--n-900);      /* #1C1B19 */
  --color-bg-surface:    #26241F;           /* warm elevated surface */
  --color-bg-sunken:     #1F1D19;
  --color-bg-inset:      #201E1A;
  --color-bg-hover:      #2F2C26;
  --color-bg-selected:   rgba(31, 128, 105, 0.22);
  --color-bg-overlay:    rgba(0, 0, 0, 0.60);

  --color-text-primary:   #F4F1EC;
  --color-text-secondary: #C4BDB1;
  --color-text-muted:     #948C7E;
  --color-text-disabled:  #6B6357;
  --color-text-inverse:   var(--n-900);
  --color-text-link:      var(--brand-300);
  --color-text-brand:     var(--brand-300);

  --color-border:         #3A362F;
  --color-border-strong:  #4A453C;
  --color-border-focus:   var(--brand-300);
  --color-border-input:   #4A453C;

  --color-brand-solid:        var(--brand-500);
  --color-brand-solid-hover:  var(--brand-400);
  --color-brand-solid-active: var(--brand-300);
  --color-brand-subtle:       rgba(31, 128, 105, 0.16);

  --focus-ring: 0 0 0 2px var(--color-bg-surface), 0 0 0 4px var(--brand-300);

  --color-success-fg:#6FCF97; --color-success-bg:rgba(30,135,75,0.16);  --color-success-solid:var(--success-500);
  --color-warning-fg:#E3B15C; --color-warning-bg:rgba(183,121,31,0.16); --color-warning-solid:var(--warning-500);
  --color-danger-fg:#E8897E;  --color-danger-bg:rgba(192,57,43,0.18);   --color-danger-solid:var(--danger-500);
  --color-info-fg:#7FB0E0;    --color-info-bg:rgba(37,99,166,0.18);     --color-info-solid:var(--info-500);
  --color-gold-fg:#E0BE6E;    --color-gold-solid:var(--accent-gold-500);
}
```

> **Contrast note:** all `*-fg` on their paired `*-bg`, and all body/heading text on surfaces, meet
> **WCAG 2.2 AA** (≥4.5:1 normal, ≥3:1 large/UI) in both themes. See [`ACCESSIBILITY.md §5`](./ACCESSIBILITY.md).

---

## 3. Typography

### 3.1 Families (canonical)

| Role | Family | Notes |
|---|---|---|
| Arabic (default) | **IBM Plex Sans Arabic** | Primary UI face when `dir="rtl"` / locale `ar`. |
| Latin & numerals | **Inter** | Latin text, all digits, IDs, code. |
| Tabular numerals | **Inter** `font-feature-settings: "tnum" 1` | Tables, prices, KPIs — columns align. |
| Mono (rare) | ui-monospace, "SF Mono", Menlo | Correlation IDs, ERP `ExternalId` display. |

```css
:root {
  --font-sans-ar: "IBM Plex Sans Arabic", "Inter", system-ui, sans-serif;
  --font-sans-latin: "Inter", "IBM Plex Sans Arabic", system-ui, sans-serif;
  --font-numeric: "Inter", system-ui, sans-serif;
  --font-mono: ui-monospace, "SF Mono", Menlo, monospace;
}
:root[dir="rtl"] { --font-ui: var(--font-sans-ar); }
:root[dir="ltr"] { --font-ui: var(--font-sans-latin); }
body { font-family: var(--font-ui); }
/* Numerals always render in Inter even inside Arabic text, harmonized to line height */
.num, [data-numeric] { font-family: var(--font-numeric); font-feature-settings: "tnum" 1, "lnum" 1; }
```

> IBM Plex Sans Arabic and Inter are size/weight-harmonized so mixed AR/EN strings (e.g.
> `عرض RFQ-2026-000123`) sit on one baseline without a bolt-on look.

### 3.2 Type scale (canonical rem steps: 12/13/14/16/18/20/24/30/36)

| Token | Size / line-height | Weight | Usage |
|---|---|---|---|
| `--text-display` | 36 / 44px | 700 | Page hero (rare — dashboards, award screen) |
| `--text-h1` | 30 / 38px | 700 | Page title |
| `--text-h2` | 24 / 32px | 650 | Section title |
| `--text-h3` | 20 / 28px | 600 | Card / panel title |
| `--text-h4` | 18 / 26px | 600 | Sub-section, drawer title |
| `--text-body-lg` | 16 / 24px | 400 | Primary body, form inputs |
| `--text-body` | 14 / 22px | 400 | Default body, table cells |
| `--text-body-sm` | 13 / 20px | 400 | Secondary text, helper text |
| `--text-caption` | 12 / 16px | 500 | Labels, badges, metadata, timestamps |

```css
:root {
  --text-display: 2.25rem; --text-h1: 1.875rem; --text-h2: 1.5rem; --text-h3: 1.25rem;
  --text-h4: 1.125rem; --text-body-lg: 1rem; --text-body: 0.875rem; --text-body-sm: 0.8125rem;
  --text-caption: 0.75rem;
  --lh-tight: 1.25; --lh-normal: 1.5; --lh-relaxed: 1.6; /* Arabic reads better slightly looser */
  --fw-regular: 400; --fw-medium: 500; --fw-semibold: 600; --fw-bold: 700;
}
/* Arabic gets marginally looser line-height for diacritics/legibility */
:root[dir="rtl"] { --lh-body: var(--lh-relaxed); }
:root[dir="ltr"] { --lh-body: var(--lh-normal); }
```

**Rules:** Body 14–16 (canonical). Prices/quantities/scores use `.num` tabular figures, right-aligned
in tables. Never justify Arabic body text (creates rivers); use start alignment. Truncate with
`text-overflow: ellipsis` only on non-essential secondary text, never on money or IDs.

---

## 4. Spacing, radius, elevation, motion, z-index

### 4.1 Spacing (canonical 4px grid)

```css
:root {
  --space-0: 0; --space-0-5: 2px; --space-1: 4px; --space-2: 8px; --space-3: 12px;
  --space-4: 16px; --space-5: 20px; --space-6: 24px; --space-8: 32px; --space-10: 40px;
  --space-12: 48px; --space-16: 64px;
}
```
All spacing applied via **logical properties** (`padding-inline`, `margin-block`, `gap`) — never
`padding-left/right` — so RTL mirrors automatically.

### 4.2 Radius (canonical)

```css
:root {
  --radius-sm: 6px;   /* chips, small controls */
  --radius-md: 8px;   /* inputs, buttons (canonical) */
  --radius-lg: 12px;  /* cards (canonical) */
  --radius-xl: 16px;  /* large cards, modals (canonical) */
  --radius-pill: 9999px; /* status chips, avatars */
}
```

### 4.3 Elevation (soft, layered — never harsh)

```css
:root {
  --shadow-sm: 0 1px 2px rgba(28,27,25,0.06), 0 1px 1px rgba(28,27,25,0.04);
  --shadow-md: 0 4px 12px rgba(28,27,25,0.08), 0 1px 3px rgba(28,27,25,0.05);
  --shadow-lg: 0 12px 32px rgba(28,27,25,0.12), 0 4px 8px rgba(28,27,25,0.06);
  --shadow-focus: var(--focus-ring);
}
.theme-dark {
  --shadow-sm: 0 1px 2px rgba(0,0,0,0.40);
  --shadow-md: 0 4px 12px rgba(0,0,0,0.45);
  --shadow-lg: 0 16px 40px rgba(0,0,0,0.55);
}
```
Default separation is a **1px `--color-border`**; elevation is added only for truly floating layers
(dropdowns `sm`, cards/drawers `md`, modals/popovers `lg`). No harsh 0-offset black shadows.

### 4.4 Motion (canonical 120–200ms ease-out; respects reduced motion)

```css
:root {
  --motion-fast: 120ms; --motion-base: 160ms; --motion-slow: 200ms;
  --ease-out: cubic-bezier(0.16, 1, 0.3, 1);
  --ease-in-out: cubic-bezier(0.45, 0, 0.15, 1);
}
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation-duration: 0.01ms !important; transition-duration: 0.01ms !important; }
}
```
Use motion for **meaning** (entrance direction of a drawer, state change of a chip), never decoration.
Toasts slide from the block-end; drawers from the inline-end (mirrored under RTL).

### 4.5 Z-index scale

```css
:root {
  --z-base:0; --z-sticky:100; --z-header:200; --z-dropdown:300; --z-drawer:400;
  --z-modal:500; --z-popover:600; --z-toast:700; --z-tooltip:800;
}
```

---

## 5. Layout system

### 5.1 App grid & shell

- **Back-office / governance surfaces (desktop-first):** fixed **sidebar** (`inline-start`, 264px;
  collapsible to 72px icon rail) + **header** (64px) + scrollable content region with a **max content
  width of 1440px**, gutters `--space-6`.
- **Supplier surface (mobile-first):** no persistent sidebar on mobile; **bottom tab bar** (≤`md`) +
  top app bar; expands to sidebar layout at `lg`.
- **12-column fluid grid** inside content, `gap: var(--space-6)`; forms use a **max readable measure**
  of ~720px for single-column, two-column only ≥`lg`.

### 5.2 Header

Contains: breadcrumb / page title (inline-start), global **command palette / search**, notification
bell, theme toggle, **language switch (ar/en)**, user menu (inline-end). Under RTL the entire header
mirrors via logical properties.

### 5.3 Sidebar / navigation

- Grouped by domain (Dashboard, RFQs, Proposals, Suppliers, Evaluations, Awards, Admin) with
  permission-filtered items (RBAC affordance-hiding per foundational §6 — re-checked server-side).
- Active item: `--color-bg-selected` fill + `inline-start` accent bar (mirrored) + `--color-text-brand`.
- Icon rail collapse persists per user (Zustand UI state).

### 5.4 User menu

Avatar → dropdown: name + role + org/supplier scope, "Switch language", "Theme", "Profile",
"Sign out". Scope is always visible so users know *which* supplier/organization context they act in.

### 5.5 Mobile navigation

- **Bottom tab bar** (max 5 items) for the supplier persona: Home, RFQs, Proposals, Documents, More.
- Secondary destinations in a **drawer** opened from "More".
- Sticky bottom **action bar** for primary form actions (Submit proposal) so the CTA is thumb-reachable.

---

## 6. Component specifications

Each component: **anatomy**, **variants**, **states**, **do/don't**. All are keyboard-accessible and
built on Radix primitives where one exists (Dialog, Dropdown, Tabs, Tooltip, Checkbox, RadioGroup,
Popover, Toast) then bespoke-styled with tokens.

### 6.1 Buttons

- **Anatomy:** `[optional leading icon] label [optional trailing icon / badge]`. Min height 40px
  (default), 32px (sm), 48px (lg / mobile primary). Radius `--radius-md`. Icon-only buttons are square
  with an accessible `aria-label`.
- **Variants (hierarchy — max one primary per view):**

| Variant | Fill / text | Use |
|---|---|---|
| **Primary** | `--color-brand-solid` bg, inverse text | The single main action |
| **Secondary** | surface bg, `--color-border-strong` border, primary text | Alternative actions |
| **Tertiary / ghost** | transparent, brand text, hover `--brand-50` | Low-emphasis inline |
| **Danger** | `--color-danger-solid` bg / or danger-outline | Destructive confirms only |
| **Gold** | `--color-gold-solid` bg | Award action only — extreme restraint |
| **Link** | text + underline-on-hover | Navigation-like |

- **States:** default · hover (`-hover` token) · active/pressed (`-active`) · focus-visible (`--focus-ring`)
  · disabled (`--color-text-disabled`, no shadow, `aria-disabled`) · **loading** (spinner replaces
  leading icon, label stays, width locked to avoid shift, `aria-busy`).
- **Do:** keep labels verb-first & bilingual ("حفظ" / "Save"). **Don't:** place danger next to primary
  at equal weight; don't disable the primary silently — explain why it's blocked (helper text).

### 6.2 Inputs (text, number, textarea, search)

- **Anatomy:** label (always visible, not placeholder-as-label) → optional helper → field
  `[leading icon] value [trailing icon/unit/clear]` → error/success message slot. Height 40px, radius
  `--radius-md`, `--color-border-input`.
- **States:** default · hover (border `--n-400`) · focus (`--color-border-focus` + ring) · filled ·
  **error** (`--color-danger-solid` border + `--color-danger-fg` message + `aria-invalid` +
  `aria-describedby`) · success (subtle) · disabled · read-only (no border, muted).
- **Number/price:** `.num` tabular, currency prefix/suffix (SYP default), thousands separators,
  `inputmode="decimal"`. Numerals honor the configured system (Western default).
- **Do:** show units and currency inline; keep helper text persistent. **Don't:** use placeholder as the
  only label; don't validate on every keystroke (validate on blur/submit — forgiveness principle).

### 6.3 Select / Combobox

- Radix-less bespoke on `@radix-ui/react-select` for native-select semantics; combobox (searchable) for
  long lists (categories tree, suppliers, currencies).
- **Anatomy:** trigger (chevron mirrors position under RTL — always trailing) → popover list → options
  with check on selected, optional description, grouping, sticky search.
- **States:** closed/open, hover/active option, selected, disabled option, empty ("No matches"),
  loading (skeleton rows). Multi-select uses removable chips in the trigger.
- **Category tree** uses an indented, expandable combobox (foundational Category is a tree).
- **Do:** virtualize long lists; keep keyboard type-ahead. **Don't:** cram >7 options that could be
  radios; don't lose the search query on reopen.

### 6.4 Date picker

- Calendar popover + typable masked input. **Gregorian default** (foundational §8); locale-aware
  formatting; week starts per locale. `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` optional Hijri
  display is future/optional — do not hard-code Hijri rules.
- **RTL:** month grid mirrors (Saturday/Sunday placement per locale), navigation chevrons mirror,
  weekday order flips. Ranges (RFQ timeline: publish → submission close) use a two-month range picker.
- **States:** empty, selected, range (start/in-range/end), disabled dates (before today for deadlines),
  today marker, min/max, error. Deadlines show the **time + timezone** explicitly.

### 6.5 File upload (document lifecycle)

Central to the **SupplierDocument** state machine (`Required → Uploaded → UnderReview → Approved |
Rejected`; `Approved → ExpiringSoon → Expired`).

- **Anatomy:** dropzone (drag + click) → file row(s): thumbnail/type icon, name, size, **status chip**,
  progress bar, actions (preview, replace, remove), reviewer note slot.
- **States:** idle · dragover · uploading (determinate progress + cancel) · uploaded/pending review ·
  under review · **approved** (green chip) · **rejected** (red chip + **reason shown inline**) ·
  **expiring soon** (amber) · **expired** (red, blocks completeness) · error (with retry, honest copy).
- **Constraints displayed up front:** accepted types, max size, required vs optional (from
  `DocumentType`), expiry field where relevant. Storage is provider-agnostic (`IFileStorage`,
  foundational §2) — UX is identical for local/S3.
- **Do:** allow replace-in-place preserving the document slot; show reviewer's rejection reason
  permanently. **Don't:** silently drop oversized files — reject with a clear message and keep the rest.

### 6.6 Checkboxes & radios

- Radix `Checkbox` / `RadioGroup`. 20px target (44px hit-area on touch). Label always clickable.
- **States:** unchecked · checked · indeterminate (checkbox) · focus-visible · disabled · error (group).
- Radio for mutually exclusive small sets; checkbox for multi. Group has a legend/`aria-labelledby`.

### 6.7 Cards

- **Anatomy:** optional header (title + meta + actions), body, optional footer. Surface bg, 1px border,
  `--radius-lg`, `--shadow-sm` (raise to `md` on interactive hover only).
- **Variants:** static content card, **interactive/link card** (whole card clickable → detail), **KPI
  card** (large tabular number, label, delta with semantic color+icon+sign), **status card** (leading
  status color bar). Gold accent only on genuine award/KPI significance.
- **Do:** one primary action per card. **Don't:** nest cards in cards (use sections/dividers).

### 6.8 Tables (TanStack Table, headless, DS-styled)

- **Anatomy:** toolbar (search, filters, column visibility, bulk actions, export) → header (sortable,
  sticky) → rows (zebra via `--n-100`, hover) → row actions (kebab / inline) → footer (pagination,
  selection count).
- **Numeric columns** right-aligned, `.num` tabular; **money** shows currency; text start-aligned.
- **States:** loading (skeleton rows matching column widths), empty (empty-state pattern), error,
  filtered-empty ("No results — clear filters"), row-selected, row-expanded (detail drawer/inline).
- **Responsive:** priority-columns → card/stacked transform on small screens (see
  [`RESPONSIVE-AND-RTL.md §4`](./RESPONSIVE-AND-RTL.md)).
- **RTL:** first logical column is inline-start; sort carets and resize handles mirror.
- **Do:** sticky header + horizontal scroll container for wide tables. **Don't:** show raw enums or DB
  IDs; render the human status chip and the public code (`RFQ-2026-000123`).

### 6.9 Tabs

- Radix `Tabs`. Underline-style active indicator (animated, mirrored under RTL). Keyboard: arrow keys
  move, Home/End jump, `Tab` leaves tablist. Overflow scrolls, not wraps.
- **States:** active, hover, focus, disabled, with count badge. Used for RFQ detail (Overview / Items /
  Invitations / Clarifications / Evaluation).

### 6.10 Breadcrumbs

- `Home / RFQs / RFQ-2026-000123 / Evaluation`. Separator is a chevron that **mirrors under RTL**
  (points inline-forward). Current page is non-link, `aria-current="page"`. Truncate middle on overflow
  with a "…" menu. Uses public codes, never internal IDs.

### 6.11 Pagination

- Variants: numbered (tables), "Load more" (feeds/notifications), cursor (long lists). Shows range
  ("1–25 of 340 · ٢٥ من ٣٤٠"). Prev/next chevrons mirror under RTL. Page-size selector (10/25/50).
  Keyboard operable; current page `aria-current`.

### 6.12 Modals (Radix Dialog)

- **Anatomy:** scrim (`--color-bg-overlay`) → panel (surface, `--radius-xl`, `--shadow-lg`, max 560px
  default) → header (title + close) → body → footer (actions, primary inline-end).
- **Behavior:** focus trapped, `Esc` closes (unless destructive-unsaved → confirm), focus returns to
  trigger, background inert (`aria-hidden`), scroll-locked. Mobile: full-screen sheet.
- **Use for:** short focused tasks & confirmations. **Don't** use for long multi-step flows (use a page
  or drawer). Never stack modal-in-modal.

### 6.13 Drawers / sheets

- Slide from **inline-end** (mirrors under RTL) for detail/side-tasks (view proposal, edit invitation,
  reviewer panel); from **block-end** as a bottom sheet on mobile.
- Same a11y as modal (focus trap, `Esc`, return focus). Widths: sm 400 / md 520 / lg 720px. Supports a
  sticky footer action bar. Non-modal variant (inspector) allowed when background stays interactive.

### 6.14 Toasts / notifications

- **Toast (transient):** block-end inline-end stack, `--radius-md`, `--shadow-lg`, leading semantic
  icon, message, optional action ("Undo"), auto-dismiss (success ~4s, error persists until dismissed),
  `aria-live` (polite success / assertive error). Max ~3 visible, queue the rest.
- **Notification (persistent):** bell → panel list with read/unread, grouped by day, per-item link to
  the object; mirrors the `Notification` aggregate. Follows [UX-Writing notification copy](./UX-WRITING.md).
- **Do:** honest, actionable copy. **Don't:** toast for validation errors (show inline on the field).

### 6.15 Badges & status chips

The most-used component — every domain state renders as a chip pairing **color + icon + label** (never
color alone; accessibility + clarity principles). Pill radius, `--text-caption`, semantic tint bg +
`-fg` text.

| Domain | State | Chip color token | Icon | Label AR / EN |
|---|---|---|---|---|
| Onboarding | Draft | neutral | `pencil` | مسودة / Draft |
| Onboarding | Under review | info | `clock` | قيد المراجعة / Under review |
| Onboarding | Info requested | warning | `alert-circle` | مطلوب معلومات / Info requested |
| Onboarding | Approved | success | `check-circle` | معتمد / Approved |
| Onboarding | Rejected | danger | `x-circle` | مرفوض / Rejected |
| Onboarding | Suspended | warning | `pause-circle` | موقوف / Suspended |
| Document | Required | neutral | `upload` | مطلوب / Required |
| Document | Approved | success | `check-circle` | معتمد / Approved |
| Document | Expiring soon | warning | `alert-triangle` | ينتهي قريباً / Expiring soon |
| Document | Expired | danger | `x-octagon` | منتهٍ / Expired |
| RFQ | Published / Open | success | `radio` | مفتوح للتقديم / Open |
| RFQ | Under evaluation | info | `scale` | قيد التقييم / Under evaluation |
| RFQ | Awarded | gold | `award` | تمت الترسية / Awarded |
| RFQ | Cancelled | danger | `slash` | ملغى / Cancelled |
| Proposal | Submitted | info | `send` | مُقدَّم / Submitted |
| Proposal | Shortlisted | success | `list-checks` | ضمن القائمة المختصرة / Shortlisted |
| Proposal | Not selected | neutral | `minus-circle` | غير مختار / Not selected |
| Proposal | Withdrawn | neutral | `undo` | مسحوب / Withdrawn |
| Sync | Pending ERP | info | `refresh-cw` | بانتظار المزامنة / Sync pending |
| Sync | Synced | success | `cloud-check` | تمت المزامنة / Synced |
| Sync | Sync failed | danger | `cloud-off` | فشل المزامنة / Sync failed |

> Full label catalogue lives in [`UX-WRITING.md §7`](./UX-WRITING.md). Icons via Lucide, mirrored where
> directional.

### 6.16 Workflow / stepper indicators

Mirrors the canonical state machines (foundational §5). Two forms:

- **Horizontal stepper** (onboarding, proposal submission): numbered steps with
  completed (check, brand) / current (filled ring) / upcoming (muted) / **blocked** (warning icon +
  reason on hover). Connector line mirrors under RTL (progress flows inline-start→end in LTR,
  reversed in RTL).
- **Vertical timeline** (RFQ lifecycle, audit history): each node = state transition with actor +
  timestamp + reason, sourced from `AuditLog`. This is the **trust-surfacing** component.
- **States must cover the real machine** — e.g. onboarding shows the `InfoRequested → Resubmitted`
  loop; RFQ shows `Cancelled` as a terminal off-path node.

### 6.17 Empty / loading / error / success states

Every data surface designs all four (UX-Principles DoD).

- **Empty:** illustration/icon + title + one-line explanation + primary action + optional secondary
  learn-more. Follows the [empty-state formula](./UX-WRITING.md). Distinguish *first-run empty* from
  *filtered-empty*.
- **Loading:** **skeleton** shaped like final content (cards/rows/detail), shimmering with reduced
  motion respected; never a bare full-screen spinner. In-place small waits use a 16px inline spinner.
- **Error:** icon + what happened + what to do + retry (per error formula). Distinguish
  recoverable (retry) vs blocked (contact/permission) vs not-found. Never a raw stack trace or code.
- **Success:** inline confirmation + surfaced audit ("Awarded by Sara · 26 Aug 2026 · 14:20"),
  transient toast for minor, dedicated screen for major moments (award).

### 6.18 Confirmation dialogs

- **Tiered by risk:**
  - *Low* (reversible): optimistic action + **toast with Undo**, no dialog.
  - *Medium* (state change): dialog with clear title, consequence sentence, primary/cancel.
  - *High* (irreversible: cancel RFQ, reject supplier, finalize evaluation, deactivate): dialog
    **requires a typed reason** (and sometimes typing the object code to confirm), danger primary,
    focus defaults to Cancel. Reason is stored in `AuditLog` and shown thereafter.
- Copy follows the [confirmation formula](./UX-WRITING.md). Never rely on the dialog alone for
  irreversible loss — prefer soft-delete/withdraw where the domain allows.

### 6.19 Charts (Recharts + custom SVG)

- For Ministry governance dashboards & procurement analytics. Themed strictly via tokens; **never**
  default Recharts colors.
- **Categorical palette** (color-blind-considered, from brand + semantics, min 3:1 adjacent):
  `--brand-500`, `--info-500`, `--accent-gold-500`, `--warning-500`, `--brand-300`, `--n-500`.
  Sequential ramps use the brand scale.
- **Rules:** always a title + legend + accessible data-table fallback (`role="img"` + `aria-label` +
  visually-hidden table). Tooltips use surface+shadow tokens, tabular numerals, currency-formatted.
  Axis labels bilingual; value axis respects numeral system; RTL flips category axis order and legend
  alignment. Empty ("No data for this period") and loading (skeleton chart) states required.
- `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` whether Ministry sees commercial values or only
  aggregate/anonymized metrics (Discovery §5) — charts must support a redacted mode.

---

## 7. Iconography

- **Lucide** (canonical), 1.5px stroke, 20px default / 16px dense / 24px touch. Consistent metaphors
  (see chip table). **Directional icons mirror under RTL** (chevrons, arrows, back/forward, send,
  breadcrumb separators) via `[dir="rtl"] .icon-directional { transform: scaleX(-1); }`; **non-directional**
  icons (search, user, calendar, clock) never mirror. Every standalone icon has an `aria-label` or is
  `aria-hidden` when decorative beside text.

---

## 8. RTL rules (design-system level)

Full guidance in [`RESPONSIVE-AND-RTL.md`](./RESPONSIVE-AND-RTL.md). Non-negotiables here:

1. **Logical properties only** — `margin-inline`, `padding-inline`, `inset-inline`, `border-inline`,
   `text-align: start/end`. Physical `left/right` is banned in component CSS.
2. **`dir` on `<html>`** drives everything; components must not assume direction.
3. **Icon mirroring** per §7. **Numerals** default Western Arabic, configurable (foundational §8),
   always `.num`/Inter, tabular in tables.
4. **Bidi isolation** for mixed strings and codes: wrap in `<bdi>` / `unicode-bidi: isolate` so
   `RFQ-2026-000123`, prices, and phone numbers never reorder inside Arabic text.
5. **Shadows, gradients, animations** use logical/symmetric values or mirror intentionally (drawer
   slides from inline-end in both directions).

---

## 9. Token quick-reference snippet (drop-in `:root`)

```css
:root {
  /* brand */
  --brand-50:#ECF6F3; --brand-100:#D2EBE4; --brand-200:#A6D6C9; --brand-300:#6FBAA8;
  --brand-400:#3E9A85; --brand-500:#1F8069; --brand-600:#136A57; --brand-700:#0F5647;
  --brand-800:#0D453A; --brand-900:#0A3730;
  /* neutrals (warm stone) */
  --n-50:#FAF9F7; --n-100:#F3F1ED; --n-200:#E7E3DC; --n-300:#D6D0C6; --n-400:#B4AB9D;
  --n-500:#8B8173; --n-600:#6B6255; --n-700:#4C463B; --n-800:#302B24; --n-900:#1C1B19;
  /* accent + semantic */
  --accent-gold-500:#C8A045; --accent-gold-600:#A98633;
  --success-500:#1E874B; --success-600:#166B3B; --warning-500:#B7791F; --warning-600:#8F5E17;
  --danger-500:#C0392B; --danger-600:#9C2C20; --info-500:#2563A6; --info-600:#1D4E82;
  --success-50:#E9F5EE; --warning-50:#FBF2E1; --danger-50:#FBEAE7; --info-50:#E8F0F8;
}
```

---

## 10. Governance

- **No raw hex in components** — token or reject in review.
- **No banned framework components** (MUI/AntD/Bootstrap) — Radix primitives + bespoke only.
- Every new component ships with: Storybook stories (light/dark × ltr/rtl × all states), axe pass,
  keyboard spec, and entries in [`ACCESSIBILITY.md`](./ACCESSIBILITY.md) + [`UX-WRITING.md`](./UX-WRITING.md).
- Token or component changes require a design-system version bump and changelog entry.
