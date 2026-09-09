# Reconciliation · what was taken from the generated design systems, and what was refused

`ui-ux-pro-max --design-system` was run twice on 2026-09-09 and wrote
`design-system/mots-back-office/MASTER.md` (density 8) and `design-system/mots-supplier/MASTER.md`
(density 4). Those files are the tool's output, unedited.

This file is the ruling on them. The direction contract (`DESIGN-IS-2026-09-09/06-direction-contract.md`
§2) says `docs/ux/DESIGN-SYSTEM.md` is the authority and that where a tool disagrees with the spec, **the
spec wins and the disagreement is written down**. So:

## Refused

| The tool proposed | Refused because |
|---|---|
| **Back office palette** — navy `#0F172A`, blue accent `#0369A1`, slate neutrals | The token layer is preserved verbatim (contract §2.1) and scored 2/3 on principle #7. Evergreen teal on warm stone stays. |
| **Supplier palette** — `#1E40AF` primary, `#3B82F6` secondary | Same, and a second palette for a second audience would split one product into two products. |
| **Atkinson Hyperlegible** (back office) | The product ships Inter + IBM Plex Sans Arabic, self-hosted via `@fontsource`. Atkinson has no Arabic face; swapping it would break half the audience to serve a Latin-only accessibility claim. |
| **Outfit / Work Sans** (supplier) | Same reason, and its own note says "portfolios, agencies, modern brands, **landing pages**". |
| **Pattern: "Minimal Single Column"** — hero headline → benefit bullets → CTA → footer | A conversion layout for a marketing page. The back office is a workspace; there is no CTA and nobody is being persuaded. |
| **Pattern: "Hero + Features + CTA", sticky hero CTA** (supplier) | The supplier is completing a legal application under deadline, not evaluating a purchase. |
| **Motion: GSAP `ScrollTrigger` scroll-reveal, 300–400ms** | Contract §5: motion carries state, never decoration. It would also add a dependency, which §5 forbids without asking. |

**Both palettes would additionally have failed `tokenConformance.test.ts` on the first commit**, which is
the point of having the test.

## Taken

| The tool said | Kept as |
|---|---|
| Style match, back office: **"Accessible & Ethical"** — high contrast, visible focus, keyboard, reduced motion, 44×44 targets; *"Best for: government, healthcare, public"* | Independent confirmation of the direction already taken. No change required; it describes what the product does. |
| Style match, supplier: **"Minimalism & Swiss Style"** — *"Best for: enterprise apps, dashboards, professional tools"* | Same. Both matches land on restraint, which is the one thing the audit said not to touch. |
| Dense spacing set: **2 · 4 · 8 · 12 · 16 · 24 · 32** | A **subset** of the product's own `--space-*` scale, and a useful rule: dense screens should not reach for 40/48/64. Adopted as the back-office subset below. |
| **"No emojis as icons (use SVG)"** | A live defect: `NotificationBell.tsx:39-43` renders 🔔 in a product that otherwise uses lucide. This is also the single dated marker that cost principle #7 its third point. Queued for Phase 4. |
| **Hover states, 150–300ms** | Consistent with §4.4's 120–200ms; the product's existing `--motion-fast` (120ms) stays, and `Input` gains the hover state the spec always required. |
| **AVOID: ornate · low contrast · motion effects · AI gradients** | Already true. Recorded so it stays true. |

---

# The density rules Phase 2 owed

Derived from the two comps, the two audiences, and `docs/ux/DESIGN-SYSTEM.md` §3.2. These are the answers
the audit found missing — 13px was the most common size on every screen measured, and `<h1>` rendered at
three different sizes for one job.

## Shared, both audiences

| Decision | Value |
|---|---|
| Page title | `--text-h1` (30px). **One size, once per page.** Never h2 or h3 for the page's own name. |
| Section / card title | `--text-h4` (18px), `--fw-semibold` |
| Sub-section inside a card | `--text-body` bold, not a smaller heading |
| Metadata, timestamps, counts, helper text | `--text-caption` (12px) |
| Numbers in tables and KPIs | `.num` — tabular figures, aligned to the end edge |
| Table cell padding | `--space-2` block / `--space-3` inline |
| Card padding | `--space-5` (20px) back office · `--space-6` (24px) supplier |

## Back office — dense

| Decision | Value | Why |
|---|---|---|
| **Body** | `--text-body` **14px** | §3.2's canonical body floor. 13px is demoted to metadata only. |
| Secondary text | `--text-body-sm` (13px) | Where it belongs — beside a value, not as the value |
| Spacing subset | **2 · 4 · 8 · 12 · 16 · 24 · 32** | The tool's dense set, which is a subset of the product's scale |
| Max measure | none — tables may run the full width | Data density is the job |
| Rhythm between sections | `--space-4` (16px) | Tight; a working screen, not a document |

## Supplier — standard

| Decision | Value | Why |
|---|---|---|
| **Body** | `--text-body-lg` **16px** | An infrequent visitor reading a legal form under deadline |
| Secondary text | `--text-body` (14px) | |
| Spacing subset | **4 · 8 · 12 · 16 · 24 · 32 · 48** | More air; reaches the upper scale the back office does not |
| Max measure | **64rem** for the page, 46rem for prose | Line length matters when the text is being read rather than scanned |
| Rhythm between sections | `--space-6` (24px) | |

## What this fixes, measurably

Re-running the audit's own measurement should show: **14px or 16px as the most common size on every
screen** (today it is 13px on all twelve measured), **one page-title size** (today three), and **no
spacing value off the 4px grid** (today four: 6, 10, 80, 96px).
