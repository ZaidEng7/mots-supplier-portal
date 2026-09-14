# Reconciliation: what was taken from the two design-system proposals, and what was refused

Two external design-system proposals were drawn up on 9 September 2026, one for the back office and one
for the supplier side, and evaluated against what the product already ships.

This file is the ruling on them. `docs/ux/DESIGN-SYSTEM.md` is the authority, and where a proposal
disagrees with the specification the specification wins and the disagreement gets written down. So:

## Refused

| What was proposed | Refused because |
|---|---|
| **Back office palette**: navy `#0F172A`, blue accent `#0369A1`, slate neutrals | The token layer is preserved verbatim (contract §2.1) and scored 2/3 on principle #7. Evergreen teal on warm stone stays. |
| **Supplier palette**: `#1E40AF` primary, `#3B82F6` secondary | Same, and a second palette for a second audience would split one product into two products. |
| **Atkinson Hyperlegible** (back office) | The product ships Inter + IBM Plex Sans Arabic, self-hosted via `@fontsource`. Atkinson has no Arabic face, and swapping it would break half the audience to serve a Latin-only accessibility claim. |
| **Outfit / Work Sans** (supplier) | Same reason, and the proposal's own note recommends them for "portfolios, agencies, modern brands, **landing pages**". |
| **Pattern: "Minimal Single Column"**, hero headline to benefit bullets to call-to-action to footer | A conversion layout for a marketing page. The back office is a workspace; there is no CTA and nobody is being persuaded. |
| **Pattern: hero, features and a sticky call-to-action** (supplier) | The supplier is completing a legal application under deadline, not evaluating a purchase. |
| **Motion: scroll-reveal animation, 300-400ms** | Motion carries state, never decoration. It would also add a dependency, which the specification forbids without asking. |

**Both palettes would additionally have failed `tokenConformance.test.ts` on the first commit**, which is
the point of having the test.

## Taken

| What was proposed | Kept as |
|---|---|
| Style match, back office: **"Accessible and ethical"**, meaning high contrast, visible focus, keyboard operability, reduced motion and 44×44 targets, recommended for government and public-sector work | Independent confirmation of the direction already taken. No change required; it describes what the product does. |
| Style match, supplier: **minimalism and Swiss style**, recommended for enterprise applications and professional tools | Same. Both land on restraint, which is the one thing the audit said not to touch. |
| Dense spacing set: **2, 4, 8, 12, 16, 24, 32** | A **subset** of the product's own `--space-*` scale, and a useful rule: dense screens should not reach for 40, 48 or 64. Adopted as the back-office subset below. |
| **No emoji as icons; use SVG** | A live defect: `NotificationBell.tsx:39-43` renders 🔔 in a product that otherwise uses lucide. This is also the single dated marker that cost principle #7 its third point. Queued for Phase 4. |
| **Hover states, 150-300ms** | Consistent with §4.4's 120-200ms, the product's existing `--motion-fast` (120ms) stays, and `Input` gains the hover state the spec always required. |
| **Avoid: ornate, low contrast, motion effects, synthetic purple-to-pink gradients** | Already true. Recorded so it stays true. |

---

# The density rules Phase 2 owed

Derived from the two comps, the two audiences, and `docs/ux/DESIGN-SYSTEM.md` §3.2. These are the answers
the audit found missing: 13px was the most common size on every screen measured, and `<h1>` rendered at
three different sizes for one job.

## Shared, both audiences

| Decision | Value |
|---|---|
| Page title | `--text-h1` (30px). **One size, once per page.** Never h2 or h3 for the page's own name. |
| Section / card title | `--text-h4` (18px), `--fw-semibold` |
| Sub-section inside a card | `--text-body` bold, not a smaller heading |
| Metadata, timestamps, counts, helper text | `--text-caption` (12px) |
| Numbers in tables and KPIs | `.num`, tabular figures aligned to the end edge |
| Table cell padding | `--space-2` block / `--space-3` inline |
| Card padding | `--space-5` (20px) back office, `--space-6` (24px) supplier |

## Back office, dense

| Decision | Value | Why |
|---|---|---|
| **Body** | `--text-body` **14px** | §3.2's canonical body floor. 13px is demoted to metadata only. |
| Secondary text | `--text-body-sm` (13px) | Where it belongs: beside a value, not as the value |
| Spacing subset | **2, 4, 8, 12, 16, 24, 32** | The dense set, a subset of the product's own scale |
| Max measure | None. Tables may run the full width | Data density is the job |
| Rhythm between sections | `--space-4` (16px) | Tight, for a working screen rather than a document |

## Supplier, standard

| Decision | Value | Why |
|---|---|---|
| **Body** | `--text-body-lg` **16px** | An infrequent visitor reading a legal form under deadline |
| Secondary text | `--text-body` (14px) | |
| Spacing subset | **4, 8, 12, 16, 24, 32, 48** | More air; reaches the upper scale the back office does not |
| Max measure | **64rem** for the page, 46rem for prose | Line length matters when the text is being read rather than scanned |
| Rhythm between sections | `--space-6` (24px) | |

## What this fixes, measurably

Re-running the audit's own measurement should show: **14px or 16px as the most common size on every
screen** (today it is 13px on all twelve measured), **one page-title size** (today three), and **no
spacing value off the 4px grid** (today four: 6, 10, 80, 96px).
