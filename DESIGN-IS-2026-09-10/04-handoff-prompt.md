# Handoff — MOTS Supplier Portal

Copy the block below verbatim. It is self-contained: the next session sees none of this audit unless
it is quoted in.

````
/make-plan Redesign the copy, claims and information layers of the MOTS Supplier Portal. Current design failed a Dieter Rams audit at 16/30 with critical gaps in principles #4 understandable (1/3), #6 honest (1/3), #10 as little design as possible (1/3) and #9 environmentally friendly (0/3).

Verdict paragraph (quoted from the audit):
> The composition this product just spent six phases rebuilding now scores 2s and a 3; what fails is everything those phases were forbidden to touch — the words, the claims and the redundancy — and the total of 16 is below the threshold of 20 that a refine would need.

Why redesign and not refine: the total is below 20, and the failures are not scattered polish items. They sit in one layer that was closed by instruction for the whole of the previous effort ("where a label and behaviour disagree, the words change, not the code"), so it has never been opened. Principle #6 honest scores 1 because a supplier is recorded as having accepted Terms & Conditions that do not exist and are never shown to them.

The product: a government procurement portal for a Ministry of Transport. React 19, TanStack Router and Query, Tailwind 4, Radix, recharts; ASP.NET Core and EF Core behind it. Full Arabic RTL via i18next, both languages shipping together, 2,024 English and 2,027 Arabic strings with zero key drift between them. Two audiences: a procurement officer in the product all day moving tenders draft-to-award, and a supplier company using it a few times a year under deadline to complete a legal application and bid.

Preserve from the current design — six phases built this and it now scores 2s and a 3, do not reopen it:
- The token layer. src/frontend/src/styles/tokens.css — 50 semantic colour tokens over 35 primitives, guarded by src/frontend/src/styles/themeContrast.test.ts asserting 83 pairs per theme.
- Both shells. src/frontend/src/shells/AppShell.tsx, Sidebar.tsx, TopBar.tsx — a grouped dark rail, a top bar carrying breadcrumb and controls, the account at the foot, and src/frontend/src/shells/reachability.test.tsx proving every declared route is reachable or exempted with a written reason.
- The seventeen shared components in src/frontend/src/components/ui, and the four screen archetypes built on them: ListCard (11 screens), the tender workspace and its TenderHeader (6 views), FormMeasure (5 wizard screens), Metric and MetricRow (5 dashboards).
- The visual language itself. Zero dated markers were found across nine screenshots and repository-wide greps; the only gradient in the codebase is a loading shimmer. Principle #7 long-lasting scored 3/3.

Discard — these patterns caused the failures:
- One fixed read-only string for every non-editable supplier state. src/frontend/src/routes/OnboardingPage.tsx:536-537 computes isReadOnly = !isEditableState and :589 renders the single string "Your application is with a reviewer", so an Approved supplier is told their application is with a reviewer under a page headed "Complete Your Supplier Profile". Caused failure on principle #6.
- Recording consent while showing nothing. src/frontend/src/routes/OnboardingPage.tsx:828-857 renders a checkbox and a button and no document, and stores an accepted version. Caused failure on principle #6.
- The page title and the card title naming the same thing. 11 list screens render an h1 and then a card h2 for one list — src/frontend/src/routes/back-office/RfqListPage.tsx:59 and :65 produce "RFQs" then "RFQ List". Caused failure on principle #10.
- The screen-reader caption repeating the visible card heading on 26 sites. src/frontend/src/components/ui/Card.tsx:43 and src/frontend/src/components/ui/Table.tsx:38 — live innerText returns "Items Items # TITLE CATEGORY QUANTITY". Caused failure on principle #10.
- Raw domain values rendered as user-facing text. tour_operations appears as a category value in the tender workspace. Caused failure on principle #4.

Top five moves from the audit (verbatim):
1. Principle #6, honest — stop recording consent to a document that does not exist. The onboarding card asks the supplier to confirm they have read the Terms & Conditions and data-processing notice, and records the acceptance with a version. It renders a checkbox and a button and nothing else, while the product's own About page says the terms have not been issued. Either the document is published and linked from that card, or the card stops claiming the supplier read anything. Evidence: OnboardingPage.tsx:828-857; DESIGN-IS-2026-09-10/shots/supplier-onboarding.png; AboutPage.
2. Principle #6, honest — make the two lying dialogs tell the truth. The withdrawal dialog says "You cannot re-enter this tender"; StartProposalHandler treats a withdrawn proposal as absent and creates a new draft while the window is open. The read-only banner says "Your application is with a reviewer" to every non-editable supplier, including an Approved one. Evidence: src/frontend/src/i18n/config.ts:3372 passed at SupplierProposalPage.tsx:447 against StartProposalHandler; OnboardingPage.tsx:536-537 and :589.
3. Principle #4, understandable — one word per object, and no raw values. "RFQ" appears 27 times in strings shown to the supplier whose own navigation calls the same thing a tender; "Addenda" ships to the same reader; tour_operations is rendered as a category value. And the Assignee filter on the review queue displays the State filter's value. Evidence: config.ts:2093 and :2401 against the nav label at :2059; shots/workspace-tender.png; live DOM reading "[0] Awaiting a decision | [1] Awaiting a decision" on /back-office/review.
4. Principle #10, as little design as possible — delete the five things on the tender list. The Owner column holds 25 identical values beside a filter that answers the same question; the screen-reader caption repeats the card's own heading on 26 sites; the h1 and the card h2 name the same list on 11 screens; the footer Help duplicates the top-bar Help; two Search controls point at one destination. Evidence: RfqListPage.tsx:91, :105, :59, :65; Card.tsx:43 with Table.tsx:38; shells/navigation.ts:97 with TopBar.tsx:105-120.
5. Principle #9, environmentally friendly — decide the dark theme. 83 contrast pairs are asserted for a theme no user can reach, and the palette ships to everyone as bytes. Either bind it to prefers-color-scheme, or give it a switch, or remove it. Evidence: src/frontend/src/styles/themeReachability.test.ts:35-41.

Redesign principles in priority order:
1. Honest (#6) — success is that every claim, dialog and recorded consent maps one-to-one to what the system actually does. No screen states two incompatible facts about the same record. No consent is stored for a document the reader was not shown.
2. Understandable (#4) — success is that one object has one name across both shells and both languages, no domain code or enum reaches the reader as text, and every filter displays its own value.
3. As little design as possible (#10) — success is that removing any remaining element on the tender list would cost the reader something. Nothing is announced twice, nothing is offered twice, no column repeats itself down every row.

Deliverables for the plan:
- A copy inventory keyed by screen, with every string that makes a claim marked against the code path that fulfils it, and each mismatch resolved in the direction of truth.
- A naming decision for each domain object (tender, addendum, bid, award) that holds across both shells, both languages, and the notification and validation catalogues in the backend.
- A per-state message map for the supplier's application screen: what the banner, the title and the step cards each say in EmailVerified, ProfileInProgress, InfoRequested, Submitted, UnderReview, Approved, Rejected and Suspended.
- A decision on the Terms & Conditions: publish and link, or remove the claim. Whichever, name who owns it.
- A dark-theme decision recorded the way themeReachability.test.ts asks for it.
- A states checklist (empty, loading, error, success, focus, disabled) confirming the removals broke none of them.
- Migration path: the copy changes land per screen family, matching the phase structure already in the repository, so each lands consistent rather than converging over several merges.
- Cutover criteria: the audit is re-run and principles #4, #6 and #10 each score at least 2.

Anti-patterns to guard against:
- Porting the old strings under new punctuation. If a claim is false, the fix is the claim, not its wording.
- Reopening the composition. Principles #3, #5 and #7 scored 2, 2 and 3; restyling them is out of scope and would risk the only 3 on the card.
- Treating the Preserve list as optional. The token layer, both shells, the seventeen components and the four archetypes stay as they are.
- Deleting the screen-reader caption without checking what announces the table afterwards. The card heading is a visible h2, not a table caption; confirm the accessible name survives.
- Fixing the dark theme by deleting the guard rather than by making a decision.
````
