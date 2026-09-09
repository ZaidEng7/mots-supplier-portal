# Comp build — scope

Approved on 2026-09-09. The three comps in `src/frontend/comps/` become the shipped screens, and
every screen after that gets its own comp, approved before it is built.

The comps are already committed, in two forms: the live source in `src/frontend/comps/*.html`, and
the captures in `DESIGN-IS-2026-09-09/comps/*.png`. They landed in PR #134 and were never built
against; the redesign that followed took decisions from them rather than porting their composition.

## Constraints, unchanged

- `src/frontend/src/styles/tokens.css` stays verbatim. A test enforces it. Every comp already draws
  from it and adds no literal colour, so this costs nothing.
- No new dependencies without asking first.
- `docs/` is read-only.
- The rule the whole redesign ran under still holds: this changes how the product looks and reads,
  not what it does. Where a label and a behaviour disagree, the words change. A genuine behaviour
  defect goes to the product owner as a question rather than being fixed in passing.
- Every guard added during phases 1 to 7 stays green: token conformance, page-heading coverage,
  density conformance, card coverage, list-state coverage, load-more coverage, list-card coverage,
  async-state coverage, dynamic-key coverage, AR/EN key parity.

## Part 1 — the three comped screens

### 1A · Buyer tender workspace

Comp: `src/frontend/comps/rfq-detail.html`, captured light, dark and RTL.
Code: `src/frontend/src/routes/back-office/RfqDetailPage.tsx`, 1355 lines, 16 cards on one page.

| Comp asks for | Today | Work |
| --- | --- | --- |
| Tab strip with live counts | Four section labels, everything stacked | New nav; 4 of 6 tabs are existing routes |
| Title is the tender name | Title is the reference code, name appended | Copy and heading change |
| Two chips: state, and time left | One state chip | New "closes in N days" chip |
| One named next action in the band | Actions scattered down the page | Move; decide the one action per state |
| Right rail: what happens next | Rail says which state it is in | New card, with the action inside it |
| Right rail: where this tender stands | Absent | New stepper, six lifecycle stops |
| Right rail: at a glance | Absent | New count panel, five figures |
| Cards carry a count and one action | Cards carry a title only | Card header gains count and action slot |
| Empty section is one line, not a card | Empty section is a full card saying "No items yet" | New empty-row treatment |

The tab strip is the structural move, and it is why this screen is the biggest of the three. Four of
the six tabs already exist as routes: Bids, Evaluation, Award and the tender itself. Two do not:
Suppliers and Settings. Settings is where the comp files the four management forms the page
currently stacks inline — ownership, submission deadline, addenda and cancel — which is most of
what makes the page 1355 lines.

No new dependency: these are links, not a tab widget.

### 1B · Supplier onboarding

Comp: `src/frontend/comps/onboarding.html`, captured light and RTL. The comp shows two states, in
progress and after submission.
Code: `src/frontend/src/routes/OnboardingPage.tsx` plus `OnboardingStepNav`.

| Comp asks for | Today | Work |
| --- | --- | --- |
| Five step cards, each with its own status | Five names in a row | Step nav gains per-step state |
| Gate card with a progress bar | Gate card with a list | Add the bar |
| Outstanding items as chips | Outstanding items as badge rows | Restyle |
| Document row: status then action on the right | Chip on the left, action far right | Row layout |
| Document row carries its own hint line | No hint | Copy per document type |
| Read-only state as a banner at the top | A green sentence inside one card | Move and restyle |

The gate card itself shipped in phase 6 and matches. This is the smallest of the three.

### 1C · Ministry award analytics

Comp: `src/frontend/comps/charts-light.png` and `charts-dark.png`, source `comps/charts.html`.
Code: `src/frontend/src/routes/ministry/MinistryAwardAnalyticsPage.tsx`, and the single
`components/charts/BarChart.tsx`, which draws vertical bars only.

| Comp asks for | Today | Work |
| --- | --- | --- |
| Ranked horizontal bars, value written at the end | Vertical bars, value in the axis | New chart mode |
| Two charts side by side | One chart per full-width card | Grid |
| Category coverage, approved against can-trade-today | Lives on another screen entirely | Decision below |
| Review queue against at-risk and overdue thresholds | Lives on another screen entirely | Decision below |
| Withheld state as a bordered panel stating the decision | A banner at the top of the page | Restyle |
| Table beside every chart | Already there | None |

**One decision needed before this is built.** The comp is a single analytics surface carrying charts
that today belong to three different screens: awards, `/ministry/categories`, and the review queue.
Either the comp becomes one combined screen and the other two lose their charts, or each chart goes
home to its own screen and the comp is read as a chart language rather than a page. My
recommendation is the second, because the review queue is a working screen for reviewers rather than
a ministry reporting surface, and moving its numbers away from the people who act on them is a
behaviour change dressed as a layout one.

`BarChart` needs a ranked horizontal mode and a two-series mode. Both are recharts props already, so
no new dependency.

## Part 2 — screen by screen

After the three, every remaining screen gets a comp before it gets built. The loop, per screen:

1. I write the comp as a static HTML file in `src/frontend/comps/`, drawing only from `tokens.css`.
2. I capture it light, dark and RTL, and put the pictures in front of you.
3. You approve, or you say what to change and I redraw.
4. Only then is it built, with a test per behaviour the comp asserts.

Roughly 66 routes exist. Not all need a comp of their own: most fall into families that share a
composition, and one approved comp settles the family.

| Family | Screens | Note |
| --- | --- | --- |
| List screens | 11 | Already share one component; the comp settles the table, filters and empty state once |
| Detail workspaces | 6 | The tender comp is the template; supplier, proposal, evaluation and award follow it |
| Forms and wizards | 9 | Onboarding's comp is the template |
| Dashboards | 4 | No comp yet, and the least resolved surface in the product |
| Analytics | 4 | The charts comp is the template |
| Auth | 5 | Centred cards, already consistent, likely cheapest |
| Admin | 8 | Reference data, roles, templates, strings |
| Everything else | ~19 | Help, about, notifications, settings, account |

Order I would take them: dashboards first, because they are what a user sees on landing and the only
family with no design decision recorded anywhere; then detail workspaces, because the tender comp
gives them a template while it is fresh; then forms; then the long tail.

## Sequencing against tomorrow

Nothing merges before the walkthrough. You walk main as it stands, against the database that was
reset on 2026-09-09, and report what you find. Findings fold into Part 1 before it is built.
