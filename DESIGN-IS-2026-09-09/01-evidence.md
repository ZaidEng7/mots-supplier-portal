# 01 · Evidence

Collected 2026-09-09 against `main @ cff6b8e`. Facts only — scoring is in `02-scorecard.md`.
Authenticated screens were rendered through the repo's own e2e mock harness (`tests/e2e/fixtures.ts`),
because the API is not running; anything that could not be observed either way is marked so.

---

## A · Visual evidence (orchestrator, first-hand)

### A1 · Type scale as rendered

Measured in-browser on twelve authenticated screens (temporary Playwright capture, 1280×900, English).
Counts are text-bearing leaf nodes per computed `font-size`:

| Screen | 13px | 16px | 18px | 14px | 24px | 12px | distinct text colours |
|---|---:|---:|---:|---:|---:|---:|---:|
| tender list | 21 | 3 | 2 | 1 | 1 | 1 | 3 |
| tender detail | 16 | 7 | 6 | 2 | 1 | 1 | 4 |
| onboarding | 37 | 26 | 7 | 6 | 1 | 12 | 7 |
| review application | 36 | 18 | 6 | 6 | 1 | 18 | 8 |
| review queue | 20 | 3 | 1 | 3 | 1 | 4 | 7 |
| proposal | 21 | 5 | 6 | 4 | 1 | 1 | 4 |
| RFQ detail (buyer) | 12 | 11 | 12 | 5 | 1 | 2 | 7 |
| compliance directory | 17 | 3 | 2 | 3 | 1 | 7 | 6 |

**13px is the most common text size on every screen measured.** `docs/ux/DESIGN-SYSTEM.md` §3.2 assigns
13px (`--text-body-sm`) to "secondary text, helper text" and states the rule: **"Body 14–16 (canonical)."**
Source counts agree: `--text-body-sm` is used **202** times across the product against **14** uses of
`--text-body`.

### A2 · Page-title size is inconsistent three ways

`<h1>` renders at `--text-h2` (24px) on **44** screens, `--text-h3` (20px) on **6**, `--text-h1` (30px) on
**5**. The spec's §3.2 assigns 30px to "page title". The shared `PageHeading` is the source of the 24px
majority: `src/frontend/src/components/ui/ListScreen.tsx:24`.

### A3 · Spacing steps in use

Tailwind spacing utilities resolve onto the 4px grid the spec declares, with four values off it:
2, 4, **6**, 8, **10**, 12, 16, 20, 24, 32, **80**, **96** px. §4.1's scale stops at 64 and contains
neither 6 nor 10. Off-scale instances: 6px ×13, 10px ×4, 80px ×1, 96px ×1.

### A4 · Radius, elevation, motion

All radii now come from tokens (`--radius-sm/md/lg/xl` ×89, `rounded-full` ×7); verified in-browser:
button 8px, input 8px, card 12px, `--shadow-sm` resolving to the layered pair, `--motion-fast` 120ms with
`cubic-bezier(0.16, 1, 0.3, 1)`. (Both fixed earlier today in #130/#131 — before that, 41 references
resolved to nothing.)

### A5 · A raw translation key is rendered to a reviewer

`src/frontend/src/routes/ReviewApplicationPage.tsx:277` renders ``t(`onboarding.fields.${f}`)`` over
`PROFILE_DISPLAY_FIELDS` (`src/frontend/src/routes/profileDisplayFields.ts:17-23`). One of those five
fields is `defaultCurrency`, and the `onboarding.fields` block defines **`currencyCode`**, not
`defaultCurrency` — in **both** languages (`i18n/config.ts:2448` EN, AR block equivalent). The screen
therefore prints the literal string **`onboarding.fields.defaultCurrency`** as the label of a field on the
supplier-application review screen. Confirmed in the capture: `shots/review-application.png`.

There are **53** dynamic ``t(`…${var}`)`` call sites in `routes/` and `components/`; a static checker
cannot resolve them, and the product has **no guard against a missing key** — unlike design tokens
(`tokenConformance.test.ts`) and async states (`asyncStateCoverage.test.ts`).

### A6 · The input primitive implements neither `disabled` nor `read-only`

`src/frontend/src/components/ui/Input.tsx:13-35` sets `backgroundColor`, `color` and `border` as **inline
styles** and has no disabled or hover handling. Inline styles beat the user-agent stylesheet, so a
disabled input renders **identically to an editable one**. `--color-text-disabled` is defined in both
themes (`tokens.css:76`, `:193`) and consumed by **nothing**.

Consequence, visible in `shots/onboarding.png`: after an application is submitted the wizard sets
`disabled={!fieldEditable(...)}` on every field (`OnboardingPage.tsx:552-574`) and the form still looks
editable. `DESIGN-SYSTEM.md` §6.2 lists both states as required: "default · hover … · disabled ·
read-only (no border, muted)". `Button` does implement disabled (`Button.tsx:37`) and hover; the `Select`
trigger implements neither.

### A7 · Two different failure presentations for the same situation

`QueryError` (`components/ui/ListScreen.tsx`) renders the failure copy with a **secondary** button;
`SupplierDashboardPage.tsx:61` and `:239` render the same situation with a **ghost** button, which has no
border or fill and reads as plain text (`shots/supplier-dashboard.png`).

### A8 · Placement of a page-scope message inside one card

`shots/onboarding.png`: "Your application has been submitted and is now read-only" renders inside the
*Profile details* card, below the phone field — beneath two cards of fields it also governs.

### A9 · Two date formats on one screen

`shots/onboarding.png` shows `01/01/2020` (a date input) and `01 Aug 2026, 03:00` (the T&C acceptance
line) on the same page.

### A10 · Navigation breadth

Supplier shell: **12** top-level links (`Dashboard, Complete Profile, Profile, Documents, Help, My
proposals, Offerings, Team, RFQs, Settings, Notification Preferences` + bell, language, log out), flat and
ungrouped, with **"Complete Profile" and "Profile" adjacent**. Back-office shell: **5**.

### A11 · Interactive element counts (rendered)

onboarding 42 · proposal 31 · RFQ detail (buyer) 26 · tender detail 26 · tender list 22 · supplier
dashboard 22 · review application 16 · review queue 14 · compliance directory 14 · award 10.

### A12 · RTL — the Arabic half could not be observed, and neither can the a11y suite

Driving `?lng=ar` through the mock harness on three screens returned `document.documentElement.dir=**ltr**`,
`lang=en`, `localStorage.i18nextLng=**en**`, and English headings — because an authenticated session
overrides the query string with the account's stored language
(`src/frontend/src/router.tsx:109`, `components/FirstRunLocale.tsx:34`). On the live dev server the
*unauthenticated* `/login?lng=ar` does render Arabic RTL.

**Mechanism, exactly.** `tests/e2e/fixtures.ts:254` mocks the account as
`{ language: 'en', languageChosen: true }`. `src/frontend/src/router.tsx:105-111` runs
`applyStoredLanguage()` on load: it fetches the account and, if `i18n.language !== account.language`,
calls `changeLanguage(account.language)` — overwriting whatever `?lng=` set. On an unauthenticated route
`getAccount()` fails and the `.catch()` leaves the query-string language alone.

**Proved by contrast, same harness, same run:**

| Route | `?lng=ar` result |
|---|---|
| `/login` (public) | `dir=rtl`, `lang=ar`, heading `تسجيل الدخول` ✓ |
| `/onboarding` (authenticated) | `dir=ltr`, `lang=en`, heading "Complete Your Supplier Profile" ✗ |
| `/rfqs/{code}` (authenticated) | `dir=ltr`, `lang=en` ✗ |
| `/back-office/review/{code}` (authenticated) | `dir=ltr`, `lang=en` ✗ |

**So the project's own headline accessibility claim is half-false.** `tests/e2e/app-a11y.spec.ts` scans
each of 68 routes as `?lng=ar` and `?lng=en` and reports 137 passing scans "in both languages"; for every
authenticated route — roughly 60 of the 68 — **both passes scan the English, LTR UI**. The Arabic
interface of an Arabic-first government portal has never been axe-scanned on any screen behind sign-in,
and RTL mirroring, Arabic line-height and Arabic string truncation are unverified there by any
instrument. `MOTS-PROGRESS.md` §6 repeats the claim, and D-68 defers the human audit on the assumption
that automated coverage exists.

---

## B · Weight & friction (subagent, measured)

### B1 · Bundle

| Measure | Value |
|---|---|
| JS emitted | 78 files, **1,170,702 B** raw / **347,951 B** gzip |
| Largest chunk | `assets/index-*.js` **700,564 B** raw / **202,240 B** gzip — build warns ">500 kB" |
| Initial fetch `/login` | 12 files, **252,841 B transferred**, **856,574 B decoded** |
| Initial fetch `/` | 12 files, 224,046 B transferred, 759,876 B decoded |
| CSS | 56,937 B raw / 17,920 B gzip |
| Brotli | none — `vite.config.ts:15` requests gzip only |

**Route splitting is real but incomplete**: `src/router.tsx` lazy-loads **49** route components and
**statically imports 17** (`router.tsx:1-15`, `:18`, `:25`) — every visitor to `/login` downloads the
admin, operations, audit, reports and ministry screens.

### B2 · Requests on the primary views (production build, headless Chromium)

`/login` 20 requests (17 static, 3 API — 2 unique × `retry: 1`); `/` 25 (18 static, 7 API — 3 unique).
Unrequested API calls before any interaction: `GET /ui-strings/en`, `GET /meta`, and on `/` also
`/health/ready` and `/reference/currencies`.

On-mount query counts from source: `RfqDetailPage` fires **7 parallel queries** unconditionally
(`:108-113`, `:250`) plus 2 conditional; `OnboardingPage` 3 unconditional + 2 conditional;
`SupplierDashboardPage` 2, both unconditional.

### B3 · Time to interactive

Committed Lighthouse config exists (`lighthouserc.json`); **the reports are gitignored**, so the only data
is a local run from **2026-08-30** — ten days old, against a main chunk **2.07× smaller** than today's:

| | `/` | `/login` |
|---|---|---|
| TTI recorded | 1,994 ms | 1,551 ms |
| TBT / CLS | 0 ms / 0.0005 | 0 ms / 0 |
| Performance score | 0.99 | 0.99 |

Scaled to today's bundle: **ESTIMATED** `/login` ~1.7–1.9 s, `/` ~2.2–2.5 s — the latter at or over the
repo's own asserted 2,500 ms LCP budget.

### B4 · Motion and idle work

Six animation declarations in the entire product. **One** loops — the skeleton shimmer
(`index.css:60`), and only while loading. `prefers-reduced-motion` is honoured twice over: globally
(`index.css:32-39`) and specifically for the shimmer (`:63-68`), with a regression test
(`Skeleton.test.tsx:99-101`). No `setInterval`-driven UI, no `requestAnimationFrame`, no autoplay,
no carousel.

### B5 · Uninvited surfaces on load

Cold anonymous `/login`: **0 modals**, 1 conditional banner. First authenticated load: **1 blocking modal**
(FirstRunLocale, `router.tsx:145`), up to 2 stacked banners (maintenance + ERP degraded), 1 unread badge,
and a fixed bottom bar on mobile. `SessionExpiredOverlay` can take the whole screen mid-session at
`--z-tooltip`, the topmost layer.

### B6 · Polling

Two session-long 60 s polls on **every authenticated screen in both shells** —
`NotificationBell.tsx:30` and `ErpStatusBanner.tsx:27`. One 2 s poll while a document is scanning
(`OnboardingPage.tsx:321`), deliberately carrying `refetchIntervalInBackground: true` (`:326`), so it
keeps firing while the tab is hidden. React Query defaults: `staleTime: 30_000`, `retry: 1`
(`lib/queryClient.ts:3-10`).

---

## C · Copy & honesty (subagent, static trace to the handler)

**Inventory.** 1,721 English strings in the catalogue; **1,152 in scope** across 42 prefixes
(`rfq.*` 135, `onboarding.*` 80, `evaluation.*` 78, `status.*` 68, `proposal.*` 56, `review.*` 47 …).
34 notification types in `NotificationCatalogue.jsonc:34-391`.

**C1 · Inflation: none of the marketing kind.** A 40-term superlative lexicon returned **zero** hits in
scope. What it did return were absolute factual claims, and eight of nine were traced to the code that
makes them true — the audit-logged bank reveal (`ManageBankAccountHandler.cs:180`), the terminal
deactivation (`Supplier.cs:727,755`), password change revoking other sessions
(`ChangePasswordHandler.cs:54-68`), clarification answers going to every invitee
(`Rfq.cs:530`, `RfqHandlers.cs:1210-1222`), the invite email (`InviteSupplierUserHandler.cs:47`).

**C2 · Five label→behaviour mismatches.**

1. `proposal.clarificationHint` (`config.ts:3229`) — "Recording your response returns the proposal for
   re-review." The call sets `State = Revised` only (`Proposal.cs:428`); `Revised → UnderReview` is a
   separate action owned by the officer (`:433-440`). **The sibling string at `:3233` says this
   correctly** — the screen contradicts itself, and the inaccurate half is the one shown *before* the
   click.
2. `notificationPreferences.alwaysOnHint` (`config.ts:2657`) — names invitations and document expiry as
   always-on. **Neither exists as a notification type**; both absences are documented in
   `NotificationClassification.cs:22-27,47-52`. The list rendered directly beneath the hint is built from
   the real types (`NotificationPreferencesPage.tsx:60`), so hint and list disagree on screen.
3. `rfq.clarifications.publish` "Publish to all" (`config.ts:2951`, `RfqDetailPage.tsx:980`) — guarded on
   `answer && visibility === 'PrivateToAsker'`, a combination the domain cannot produce: answering sets
   both fields at once (`Rfq.cs:528-530`). The button and its "Private to asker" badge describe a state
   that cannot exist.
4. `rfq.submitForReview` (`config.ts:2861`, `RfqDetailPage.tsx:461`) — the same click also commits the
   approver nomination from the adjacent select (`:243`), and leaving it blank silently means "any
   manager". Explained in a code comment (`:241-242`), nowhere in the interface.
5. `rfq.closeSubmission` (`RfqDetailPage.tsx:475-476`) — collects its audit reason through a native
   `window.prompt`; cancelling or typing whitespace fires nothing and **shows no feedback**, though the
   copy promises the reason is recorded. `rfq.manualCloseReason` (`:2874`) is a dead key.

**C3 · Dark patterns: none found**, in six categories, with the three hardest cases checked and cited —
the consent checkbox is opt-in from `useState(false)` with the accept button disabled until ticked
(`OnboardingPage.tsx:304,652,659`); deactivation is `variant="danger"`, warned, and unreachable from
Active; no pricing, trial or renewal strings exist anywhere.

Two findings adjacent to the category, recorded rather than classified:
- **Withdraw proposal has no confirmation and is irreversible** (`Proposal.cs:553-555` — `Withdrawn` is
  terminal). The control is `variant="ghost"`, the *lowest*-emphasis variant in the system
  (`SupplierProposalPage.tsx:424-431`), and no copy tells the supplier it is final. Under-weighted rather
  than disguised.
- On the conflict-of-interest declaration, **"No conflict — continue" is the primary button and "I have a
  conflict — recuse me" the secondary** (`MyEvaluationPage.tsx:129,141`). The words are even-handed; the
  emphasis is not.

**C4 · Jargon, 23 items.** The supplier-facing ones matter most, because that audience is an outside
company: `nav.rfqs` "RFQs" (the rest of the catalogue says "tender"), "Addenda", "Envelope",
"Incoterm", "Consolidate", "Recuse", "Threshold" (the evaluator brief already solves this as "Minimum",
`:2758` — two screens disagree), `rfq.boundTemplate` rendering a **raw GUID** to the user (`:2897`),
`evaluation.evaluatorUserId` "Evaluator user id" (`:2982`), and `review.submit` as the bare confirm label
of a Request-info dialog (`:3316`).

**C5 · Empty vs error copy.** Checked on six screens: a reader can tell the two apart on all six. The
weakness is the other direction — five of six error strings are interchangeable ("Could not load X"), so
a reader learns *that* it failed and never *why* or whether retrying helps. `documents.*` has **no**
empty-state string at all while carrying four distinct error strings (`config.ts:3480-3512`).

---

## D · Two screens read closely (orchestrator, from the captures)

### D1 · Buyer RFQ detail — `shots/rfq-detail-buyer.png`

**Eleven stacked cards of identical visual weight**, in this order: Ownership · Submission deadline · RFQ
Workflow · Items · Requirements · RFQ attachments · Evaluation template · Invitations · Clarifications ·
Addenda · Cancel RFQ. **Seven of the eleven are empty** ("No items yet", "No requirements yet", "No
suppliers invited yet", "No addenda yet"…). Nothing on the screen is larger, closer or louder than
anything else, so the reading order is the DOM order.

- The one element that could direct the officer — the **RFQ Workflow** card — renders "No next action is
  currently available."
- **Two state values are shown at once and disagree**: the header chip reads **Published**
  (`summary.state`) while the workflow card reads **Draft** (`workspace.rfqState`). In this capture the
  disagreement comes from the fixture, so it is not proof of a production defect — what it does prove is
  that the screen renders two independent state sources side by side and reconciles nothing.
- **`Cancel RFQ`** — irreversible — is the last card, rendered as **ghost** (the lowest-emphasis variant
  in the system) and wrapping onto two lines inside its own button.
- Every mutating card repeats the same *reason* input; five such fields on one screen.
- The deadline field is a native `datetime-local`, rendering `dd/mm/yyyy, --:-- --` in a product where
  every other control is themed.

### D2 · Supplier tender detail — `shots/tender-detail.png`

Five cards, four of them empty, and the one action that matters ("Go to my proposal") is a small button in
the header. **"Decline invitation"** — terminal — sits as the final card with a full-width reason field,
the same weight as "Clarifications". Two buttons wrap their labels onto two lines inside the button
("Send question", "Decline invitation").

### D3 · Supplier navigation — every screen

12 flat top-level links, including **"Complete Profile"** and **"Profile"** adjacent, plus
"Notification Preferences" spelled out in full. The back-office shell shows 5. No grouping, no section
headings, no visible current-page state in the captures.

---

## E · Structural evidence (subagent, with two orchestrator corrections)

### E1 · One file carries the buyer's whole job

`routes/back-office/RfqDetailPage.tsx` — **1,256 lines, 78 interactive elements in source (39 `<Button>`
alone), JSX depth 15, 15 sections, and 8 of the 11 state transitions** between "create RFQ" and "award
executed". Next largest scoped files: OnboardingPage 24, SupplierProposalPage 24, AddressesPage 22.

### E2 · Task step counts, from the code

**Supplier**: 8 routes + 6 in-route sections + ≥7 submit actions from register to submitted-for-review.
Required data is spread across three of the five wizard routes while the submit button exists on only one
(`OnboardingPage.tsx:741-746`).
**Buyer**: 6 routes (+2 adjacent), 15 sections on the detail route, **11 state transitions**.

### E3 · Twelve repeated patterns — and the shared components exist

The product has a component vocabulary and mostly does not use it:

| Pattern | Shared component | Used | Hand-rolled again |
|---|---|---|---|
| Page title + subtitle | `PageHeading` | **2** | **24** (identical `<h1 className="text-[length:var(--text-h2)] …">`) |
| Four-state list body | `ListState` / `ListCard` | 2 | 6 ternary chains |
| "Load more" | `LoadMore` | 2 | 5 |
| Filter row | `FilterBar` / `FilterField` / `SearchField` | 2 | 3 identical + 3 divergent |
| "Could not load — retry" | `QueryError` | 16 | 5 |
| Reason + confirm row | `ReasonDialog` (not in `ui/`) | 2 | 7 inline + 2 `window.prompt` |
| KPI stat card | — none | — | 3 in scope, 5 product-wide |
| Document-status row | — none | — | 3 independent implementations |
| File upload | — none | — | 5 |
| Entity form dialog | `Dialog` primitive only | — | 6 components + 2 inline |

Page titles also render at three sizes for the same job — `--text-h1` on 4 dashboards, `--text-h2` on 24
screens, `--text-h3` on 3.

### E4 · Hygiene is clean

`oxlint` over `src`: **zero** unused imports or variables. Zero dead props across the 8 scoped components
that declare prop types. 67 warnings exist, all of other rules (29 `only-export-components`, 7
`incompatible-library`, 2 `purity`, 1 `set-state-in-effect`).

### E5 · Orchestrator correction to the subagent

The subagent reported `/proposals`, `/comparison` and `/award` as having **no inbound link anywhere** —
"reachable only by typed URL". **That is wrong, and I checked it before scoring**: all three are linked
from `RfqDetailPage.tsx:1051`, `:1054`, `:1058`. The finding survives in a different form:

- They are raw **`<a href>` inside a single-page app** — each one is a full document reload (~250 KB
  re-download, in-memory state lost), not a router navigation.
- Each anchor **wraps a `<Button>`**: an interactive element inside an interactive element. Nothing in the
  suite catches it — `axe`'s `nested-interactive` rule does not fire for a button inside an anchor — so
  it passes 137 scans.
- All three sit inside the `evaluationEligible` card, so the route to the bids, the comparison and the
  award is one conditional block in the middle of a 1,256-line screen.

---

## F · Accessibility (subagent, measured in a real browser)

### F1 · Contrast passes, and is barely checked in the light theme

All eight requested pairs clear AA; the narrowest is light `--color-text-muted` on `--color-bg-sunken` at
**4.64:1**. But the automated coverage is asymmetric: **53 dark-theme pairs + 2 focus-ring pairs = 55**,
and **zero light-theme text pairs**. `darkThemeContrast.test.ts` also hardcodes its own copy of the palette
(`:25-41`) rather than reading `tokens.css`, so a token edit does not reach it.

### F2 · No skip link — and the spec asks for one

`grep` for `skip.link|skipLink|skip to content|href="#`: **0 matches** in `src/`, `index.html`, `public/`.
No `id="main"`; neither `<main>` carries one. First Tab on `/onboarding` lands on "Dashboard", with **14
chrome stops before the first page control**. `docs/ux/ACCESSIBILITY.md:52,92,115` requires a "skip to
content" link on first Tab. axe's `skip-link` rule is `best-practice`-tagged, so the suite cannot see it.

### F3 · Seven screens render outside every landmark

`<main>` exists in exactly four files; `role="main|banner|navigation|contentinfo"` appears **nowhere**. On
`/login` the only landmark in the document is `<footer>`. The same holds for `/register`,
`/verify-email`, `/forgot-password`, `/reset-password` and both invite-acceptance screens. Inside the
shells, the header `<nav>` in **both** shells carries no `aria-label`, and neither does banner, main or
contentinfo.

### F4 · Two overlays declare `aria-modal` and manage no focus

`SessionExpiredOverlay.tsx:58-71` (full-screen, contains a password field, at `--z-tooltip`) and
`FirstRunLocale.tsx:44-56` both render `role="dialog" aria-modal="true"` with **no focus trap, no initial
focus and no Escape handler** — a repo-wide grep for `.focus()|autoFocus|FocusTrap|onKeyDown` returns
exactly two hits, neither in these files. The page behind stays tabbable. The Radix `Dialog` does all of
this correctly (`ui/Dialog.tsx:26-29,61-66`, proven in `app-keyboard.spec.ts:21-58`), so the product knows
how; these two were hand-rolled.

### F5 · The read-only wizard is not read-only for three controls

`ui/Select.tsx` accepts **no `disabled` prop** (0 matches in the file). In the `UnderReview` state the
onboarding form disables every `Input` and hides both Save buttons — and the measured tab order still
stops on "Entity type", "Currency" and "Country code", all changeable, with nothing to commit them.

### F6 · The submit error summary sits behind the controls it describes

`OnboardingPage.tsx:711-732` renders the `role="alert"` blocker list **after** the whole document list, its
links point backwards, and nothing moves focus to it. A keyboard user who presses Submit must Shift+Tab
back past every document control to reach the explanation.

### F7 · Keyboard reachability is otherwise clean

Zero `onClick` on non-interactive elements product-wide. Exactly one `tabIndex` in the app, and it is a
deliberate `-1` anchor target. Every icon-only control has an accessible name. All eleven primary actions
checked are keyboard-operable. One gap: the Request-info `<textarea>` has a placeholder and **no
accessible name** (`ReviewApplicationPage.tsx:59-66`), where the sibling `ReasonDialog.tsx:32-40` has both.

### F8 · What the 137-scan gate does not check

- **39 of axe's 105 rules are not selected**, because the suite passes `wcag2a|wcag2aa|wcag22aa` only and
  the whole landmark/heading family is `best-practice`: `skip-link`, `region`, `landmark-one-main`,
  `page-has-heading-one`, `heading-order`, `empty-heading`, `tabindex`, `focus-order-semantics`,
  `aria-dialog-name` and 30 more. **Every finding in F2–F6 is structurally invisible to it.**
- **The dark theme is never scanned.** No test sets `.theme-dark`; `color-contrast` only ever runs against
  the light palette.
- **No interaction.** Each test is one `goto` + one `analyze()`: no open dialog, no validation error, no
  toast, no open listbox, no loading or error variant is ever scanned.
- **One viewport** (1280×720), so the mobile tab bar and the mobile nav are never in the scanned tree.
- **One persona per shell and one data shape per route** — `/onboarding` is only ever scanned in its
  read-only `UnderReview` state, so the editable form F5 describes is absent from both of its scans.
- And per **A12**, for authenticated routes both of those two scans run against the **English** UI.
