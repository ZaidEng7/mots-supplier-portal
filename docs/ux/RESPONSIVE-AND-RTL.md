# Responsive & RTL Strategy — MOTS Supplier Portal

> **Status:** Baseline v1 · **Owner:** Design Lead · **Date:** 2026-08-26
> Consistent with [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md) (§7
> tokens, §8 localization, §3 personas & devices) and the [Discovery Report](../product/DISCOVERY-REPORT.md).
> Companions: [`DESIGN-SYSTEM.md`](./DESIGN-SYSTEM.md) · [`UX-PRINCIPLES.md`](./UX-PRINCIPLES.md) ·
> [`ACCESSIBILITY.md`](./ACCESSIBILITY.md) · [`UX-WRITING.md`](./UX-WRITING.md)

Arabic-first RTL and responsive behavior are **first-class**, designed-in from the start (foundational
§8: default `ar`/RTL, English secondary; i18next + CSS logical properties + `dir` switching).

---

## 1. Breakpoints

Aligned to Tailwind v4 defaults, named for intent. Mobile-first: base styles target the smallest
screen; breakpoints add capability upward.

| Token | Min width | Target | Typical layout |
|---|---|---|---|
| *(base)* | 0 | Small phones | Single column, bottom tab bar, stacked cards, sticky action bar |
| `sm` | 640px | Large phones | Comfortable single column, 2-up KPI cards |
| `md` | 768px | Tablets | 2-column forms begin, table→card hybrid, split panes appear |
| `lg` | 1024px | Small laptops | Persistent sidebar, full data tables, side drawers |
| `xl` | 1280px | Desktops | Multi-pane (list + detail), evaluation comparison grid |
| `2xl` | 1536px | Large desktops | Max content width 1440px, generous gutters, extra context columns |

```css
:root {
  --bp-sm: 40rem; --bp-md: 48rem; --bp-lg: 64rem; --bp-xl: 80rem; --bp-2xl: 96rem;
  --content-max: 1440px;
}
```

**Container queries** are used for self-contained components (cards, tables inside drawers) so they
adapt to *their* container, not just the viewport — a table in a 520px drawer stacks even on a wide
screen.

---

## 2. Per-persona responsive strategy

Device priorities come straight from the canonical persona table (foundational §3).

### 2.1 Suppliers — **mobile-first** (`supplier_admin`, `supplier_user`)

Primary device: **mobile + desktop**. Suppliers register, upload documents, and submit proposals often
from a phone, sometimes on-site, sometimes near a deadline.

- **Design the mobile screen first**, then progressively enhance to desktop.
- **Bottom tab bar** (Home / RFQs / Proposals / Documents / More); **sticky bottom action bar** for the
  primary CTA (Submit proposal, Upload document) so it is thumb-reachable and never scrolled away.
- **Onboarding & proposal wizards** are chunked, autosaving sections (progressive disclosure +
  forgiveness) — each fits a phone screen without horizontal scroll; the stepper collapses to a compact
  "Step 2 of 5 · الخطوة ٢ من ٥" pill with progress.
- **File upload** optimized for camera/gallery capture; large tap targets (≥44px); upload progress and
  document status chips are prominent.
- **Deadlines** surfaced persistently (countdown chip) — mobile users must never miss a submission
  window.
- Desktop for suppliers unlocks side-by-side (form + document preview) but the mobile flow remains
  fully capable.

### 2.2 Procurement & back-office — **desktop-first**

`onboarding_reviewer`, `procurement_officer`, `procurement_manager`, `evaluator`, `system_admin`.
Primary device: **desktop** (evaluator also tablet).

- **Design the desktop screen first**; ensure graceful tablet/`md` fallback; mobile is *supported*
  (view/approve on the go) but not the optimization target.
- **Multi-pane** layouts ≥`xl`: RFQ list + detail; evaluation list + scoring panel; review queue +
  document viewer. Below `lg` these become drill-down (list → full-screen detail → back).
- **Keyboard & density** favored: sidebar navigation, command palette, data-dense tables with
  priority columns, inline row actions. Comfortable/compact density toggle for long sessions.
- **Evaluator on tablet:** scoring UI is touch-friendly (large score steppers, swipe between proposals)
  while preserving the blind-then-consolidated model (foundational §5).

### 2.3 Ministry governance — **desktop-first, read-only**

`ministry_viewer`. Dashboards, charts, exports. Optimized for large screens; responsive down to tablet;
mobile provides a summarized read-only view. Respects the commercial-value redaction open question
(Discovery §5).

---

## 3. Responsive layout patterns

| Region | Base (mobile) | `md` | `lg`+ |
|---|---|---|---|
| App shell | Top bar + bottom tabs | Top bar + collapsible drawer | Persistent sidebar + header |
| Forms | 1 column, full-width | 1–2 columns | 2 columns, max measure ~720px per column |
| Detail views | Stacked sections | Tabbed or 60/40 split | List+detail split pane |
| Filters | Filter drawer (bottom sheet) | Filter bar + drawer overflow | Inline filter bar |
| Primary actions | Sticky bottom action bar | Inline in header/footer | Inline, inline-end aligned |
| Modals | Full-screen sheet | Centered ≤560px | Centered ≤560px |
| Drawers | Bottom sheet | Side drawer (inline-end) | Side drawer (inline-end) |

Rules: no horizontal page scroll ever (only intentional inner scroll containers for wide tables/charts).
Touch targets ≥44×44px on touch surfaces. Respect safe-area insets (notches, home indicator) on mobile.

---

## 4. Responsive table patterns (the hard part)

Tables (TanStack Table, DS-styled) carry the densest procurement data (RFQ lists, proposal comparison,
supplier registry, audit). Three coordinated strategies, chosen per table:

### 4.1 Priority columns (progressive truncation) — default for back-office lists

Each column has a **priority rank**. As width shrinks, lowest-priority columns are hidden first; hidden
data moves into an expandable row / detail drawer. Essential columns (identity + status + primary
metric) never hide.

| Example: RFQ list | Priority | ≤sm | md | lg+ |
|---|---|---|---|---|
| RFQ code (`RFQ-2026-000123`) | P1 | ✅ | ✅ | ✅ |
| Title | P1 | ✅ | ✅ | ✅ |
| Status chip | P1 | ✅ | ✅ | ✅ |
| Submission deadline | P2 | (in card) | ✅ | ✅ |
| Invited / responded count | P3 | ✕ | ✅ | ✅ |
| Buyer org | P3 | ✕ | ✕ | ✅ |
| Created / owner | P4 | ✕ | ✕ | ✅ |

### 4.2 Card / stacked transform — default for supplier (mobile) lists

Below `md`, each row renders as a **card**: primary line (code + status chip), secondary lines
(label: value pairs), and a row action. This is the mobile-first default for supplier-facing lists
(proposals, documents, invitations).

```
┌─────────────────────────────┐
│ RFQ-2026-000123   [ Open ]   │  ← primary + status chip
│ Hotel linens supply          │  ← title
│ Deadline · 30 Aug 2026 14:00 │  ← key meta (bidi-isolated)
│ 12 invited · 4 responded     │
│                    [ View › ]│  ← action (chevron mirrors RTL)
└─────────────────────────────┘
```

### 4.3 Horizontal scroll container — for irreducibly wide comparison grids

Proposal **comparison matrix** (criteria × suppliers) cannot collapse meaningfully. It lives in an
`overflow-inline: auto` container with a **sticky first column** (criterion / supplier identity) and
sticky header; a scroll shadow hints more content. On mobile it becomes a per-supplier stacked view or
a two-supplier compare picker. Money cells stay `.num` tabular and right-aligned.

**Selection rule:** back-office data lists → priority columns; supplier/mobile lists → card transform;
matrix/comparison → sticky-scroll (with mobile fallback). Never force a wide table to squeeze into a
phone width with tiny unreadable text.

---

## 5. RTL guidelines

Arabic (`ar`) is the **default, RTL**; English (`en`) is LTR secondary. RTL is the design baseline, not
a post-hoc flip.

### 5.1 Core mechanics

- **`dir` attribute on `<html>`** (`rtl`/`ltr`) driven by the active locale; toggled instantly via the
  header language switch. Zustand holds UI locale; i18next holds strings.
- **CSS logical properties everywhere** — the single most important rule:

| ❌ Physical (banned) | ✅ Logical (required) |
|---|---|
| `margin-left / margin-right` | `margin-inline-start / -end` |
| `padding-left / padding-right` | `padding-inline-start / -end` |
| `left / right` | `inset-inline-start / -end` |
| `border-left / border-right` | `border-inline-start / -end` |
| `text-align: left / right` | `text-align: start / end` |
| `float: left/right` | logical layout via flex/grid |

- **Flexbox/Grid** honor `dir` automatically for main-axis order — no manual reversing needed; avoid
  `row-reverse` hacks that break under locale switch.

### 5.2 Icon & directional mirroring

- **Mirror** (via `.icon-directional { [dir=rtl] & { transform: scaleX(-1); } }`): chevrons, arrows,
  back/forward/next/prev, breadcrumb separators, send/reply, list indent, progress connectors, drawer
  slide direction, undo/redo.
- **Do NOT mirror:** search (magnifier), user/avatar, calendar, clock, checkmark, most brand/logo,
  media play (play direction is a locale/[ASSUMPTION] decision — default do not mirror), numbers.
- **Charts:** category axis and legend flip to start at inline-start under RTL; time axis conventionally
  still flows left→right for time-series `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` — confirm with
  stakeholders; provide a config flag.

### 5.3 Bidirectional (bidi) text — pitfalls & rules

Mixed Arabic + Latin/numbers is constant here (`عرض RFQ-2026-000123`, prices, phones, emails). Without
isolation the bidi algorithm scrambles order (e.g. codes reverse, punctuation jumps).

- **Wrap embedded LTR runs in `<bdi>`** or apply `unicode-bidi: isolate`. Apply to: entity codes
  (`RFQ-…`, `SUP-…`, ERP `ExternalId`), currency amounts, phone numbers, emails, URLs, version strings,
  file names, dates.
- Use the correct **directional marks** only when necessary; prefer `<bdi>`/`isolate` over manual
  `&lrm;`/`&rlm;` littering.
- **Punctuation & brackets** mirror logically; never hard-code parenthesis direction.
- Test string: `أرسل العرض RFQ-2026-000123 بقيمة 1٬250٬000 ل.س قبل 30 Aug 2026` — must render with the
  code, amount, and date each internally LTR and correctly placed within the RTL sentence.

### 5.4 Common RTL pitfalls (design-review checklist)

- ❌ Physical margins/paddings leaking into a component → breaks mirror. **Grep for `-left`/`-right`.**
- ❌ Absolute-positioned close buttons using `right: 0` → use `inset-inline-end: 0`.
- ❌ Icon-with-text where only text mirrors → the affinity (icon side) must flip too.
- ❌ Scroll shadows / gradients hard-coded to one physical side.
- ❌ Third-party widget (date picker, chart) not RTL-aware → must be wrapped/configured or replaced.
- ❌ Numerals switching font mid-string → always `.num`/Inter, harmonized line-height.
- ❌ Truncation ellipsis on the wrong side → logical `text-overflow` respects `dir`.
- ❌ Animations sliding the physical wrong way (toast/drawer) → use logical/inline-based transforms.

---

## 6. Numerals, dates, currency presentation

### 6.1 Numerals

- **Default: Western Arabic digits (0–9)** per foundational §8/§7 `[ASSUMPTION]` for Syrian business
  context; **configurable** to Eastern Arabic (٠–٩) at the tenant/user level.
- Rendered via `.num` (Inter, `tnum lnum`) so they are **tabular** in tables/prices and never swap
  fonts mid-string. The numeral *system* is an i18n formatting concern (Intl/`toLocaleString` with the
  chosen numbering system), independent of layout direction.

### 6.2 Dates & times

- **Gregorian default** (foundational §8); locale-aware formatting via `Intl.DateTimeFormat` (Arabic
  month/day names under `ar`). Week start per locale.
- Deadlines always show **date + time + timezone** to avoid disputes (e.g. "30 Aug 2026, 14:00 (+03)").
- `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` optional **Hijri** display is future/optional — do not
  invent conversion rules; if enabled, show dual (Gregorian primary / Hijri secondary).
- Relative time ("2 hours ago / قبل ساعتين") for feeds/notifications; absolute on hover/detail.

### 6.3 Currency & amounts

- **SYP (Syrian Pound) default**, configurable; **multi-currency proposals** with an explicit display
  currency (Discovery §3.1 confirms currency per quotation). Always show the currency code/symbol
  adjacent to the amount, never ambiguous.
- Formatting via `Intl.NumberFormat` with locale grouping (thousands separator), fixed decimals per
  currency, and the configured numeral system. Amounts are `.num`, right/inline-end-aligned in tables,
  bidi-isolated in prose.
- **Never invent Syrian tax rules** — tax fields render generically and are tagged
  `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` (foundational §8, Discovery §5).

---

## 7. Testing the responsive & RTL matrix

Every screen is verified across the **matrix**: `{ ar-RTL, en-LTR } × { base, md, lg, xl } × { light,
dark }`. Storybook renders this grid; Playwright runs viewport + `dir` permutations; a bidi test string
is included in visual snapshots. See [`ACCESSIBILITY.md §8`](./ACCESSIBILITY.md) for the automated +
manual approach shared with a11y testing.
