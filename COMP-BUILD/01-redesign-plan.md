# The redesign, screen by screen

Approved 2026-09-10, from the four templates in `src/frontend/comps/redesign/`. Every screen in the
product gets this shell, this palette and this component language, with behaviour unchanged.

## The measurement that decides the plan

Taken from the source on 2026-09-10, not estimated:

| | |
|---|---|
| Screens | 68 |
| Screens already importing the shared component layer | 63 |
| Screens that are not | 5 |
| Shared UI components carrying the look | 17 |
| Colour references in screens | 413, all through `var(--token)` |
| Raw hex values in screens | **0** |

That last row is the plan. Phases 1 to 7 funnelled every screen through one component layer and one set
of token names, and nothing in the product reaches for a literal colour. So the re-skin is a change to
what the token names *mean*, plus 17 components and 2 shells, and 63 screens inherit it without being
touched.

The seam already exists. This plan is about using it, not building it.

## Rules that hold throughout

- **Token names are added, never removed.** `tokenConformance.test.ts` fails when a component asks for a
  token that is not defined, so a removed name breaks screens silently at runtime and loudly in CI.
  Values change freely; names are a contract.
- **Behaviour does not change.** 806 unit tests and 232 Playwright tests are the instrument. A phase that
  needs a behaviour test rewritten is a phase that changed behaviour, and that is a finding, not a step.
- **Both languages, every phase.** The axe sweep runs over all 70 routes in Arabic and English. A phase
  is not done when it is done in English.
- **`docs/` stays read-only.** Where `docs/ux/DESIGN-SYSTEM.md` and this redesign disagree, the disagreement
  is recorded and taken to the product owner rather than resolved in code.

## Phase A. The token layer

One file, the whole product changes appearance, nothing changes structurally.

- Rewrite the values in `src/frontend/styles/tokens.css` under the names that already exist.
- Add the names the new shell needs and the current layer has no equivalent for: the sidebar field
  colours, the accent wash and line, the four status washes.
- Retire brass as the accent. Two independent passes found it: the Rams audit called the palette the
  product's strongest dimension, and the taste pass found the exact hex on its banned list, as the
  palette every generated interface ships. The accent becomes a brighter step of the product's own
  evergreen.
- Update `tokenConformance.test.ts` to the new values. It keeps enforcing the same five rules.

**New instrument, and this phase is where it belongs.** A contrast guard that computes the WCAG ratio
for every foreground and background pair the token layer can produce, and fails below 4.5. The taste
pass found four pairs under the line by computing them, including white on the primary button at 4.42.
A number that close is invisible to any eye and to every reviewer.

**Done when:** every screen renders in the new palette, the whole suite is green, and the contrast guard
passes in both themes.

## Phase B. The two shells

The change the client asked for by name: categories down the side, and everything reachable.

- `SupplierShell` and `BackOfficeShell` both become sidebar shells: a grouped vertical nav, a top bar
  carrying breadcrumb and search, the account at the foot.
- Every destination appears in the sidebar under a heading. The back office carries five groups; the
  supplier shell carries three.
- Counts ride on the destinations that have one, so a queue of twenty-one is visible from any screen.
- Breadcrumb is the second level, a record's own tabs are the third. Three levels, always visible.

**New instrument.** A reachability guard: every route the router declares is reachable from its shell's
navigation, or is named in an exemption with a reason. The gap found by hand this week, where the bids
screen had no way back, is the shape of defect this closes.

**Done when:** no route is unreachable, both shells render in both languages, and the axe sweep is clean.

## Phase C. The seventeen components

Restyle each to the template language. No API changes, so no screen edits.

Card, Button, Table, Badge, StatusChip, Field, Input, Select, Dialog, Toast, Skeleton, PhoneInput,
PageHeading, ListScreen and its family, Stepper, FactList, NextActionCard.

**Done when:** every component story renders in the new language and every existing component test still
passes untouched.

## Phase D. The four archetypes

Each template becomes the composition for its family. This is where screens are edited.

| Archetype | Template | Screens |
|---|---|---|
| List | `1-list.html` | 11 |
| Detail workspace | `2-workspace.html` | 6 |
| Form and wizard | `3-form.html` | 9 |
| Dashboard and analytics | `4-dashboard.html` | 8 |

Each family lands as one change: the family's screens together, so the family is consistent when it
lands rather than converging over four merges.

## Phase E. The long tail

The 34 screens outside the four families, and the 5 that import no shared component at all:
`HomePage`, `ResetPasswordPage`, `AcceptStaffInvitePage`, `AcceptTeamInvitePage`, and `TenderTabs`,
which is a component living in the routes tree rather than a screen.

## Phase F. Verification

- axe over all 70 routes, both languages.
- Contrast guard over the token layer, both themes.
- Reachability guard over the router.
- A capture of all 68 screens, light and dark, as the record of what shipped.
- The act-by-act walkthrough, driven by a person, because the automated walk follows the path it was
  written for and a person takes the turnings it never took.

## What this plan does not decide

- **Whether an icon set is added.** The templates hand-draw their icons, which is fine in a static comp
  and wrong in the build. Using Phosphor or Tabler means a new dependency, which is the client's call.
- **Whether `docs/ux/DESIGN-SYSTEM.md` is updated.** It is the authority on tokens and it is read-only to
  design work, so this redesign contradicts a document it may not edit.

## Outstanding before Phase A starts

`fix/walkthrough-findings` is open as pull request 149 and its backend quality gate fails on two Sonar
numbers for new code: security rating C and duplication at 8.7 percent. Neither is a test failure. It
should land before the redesign begins, so that a red gate during the redesign means the redesign.
