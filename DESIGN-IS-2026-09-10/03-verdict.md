# Verdict — MOTS Supplier Portal, 2026-09-10

## REDESIGN — 16/30

**The composition this product just spent six phases rebuilding now scores 2s and a 3; what fails is
everything those phases were forbidden to touch — the words, the claims and the redundancy — and the
total of 16 is below the threshold of 20 that a refine would need.**

The score moved from 12 to 16. That improvement is real and it is exactly where the work was aimed:
aesthetic, unobtrusive and long-lasting all sit at 2 or 3, and there is not one dated visual marker
anywhere in the product. The redesign did what it was scoped to do.

It was scoped, by explicit instruction, to change how the product looks and reads and never what it
does or says — "where a label and behaviour disagree, the words change, not the code". So the copy
layer was never opened. That is where the failures now are:

- **Honest scores 1** because a supplier is recorded as having accepted Terms & Conditions that do
  not exist and are never shown to them, and because two dialogs state the opposite of what the code
  does.
- **Understandable scores 1** because the product's own acronym leaks into screens written for people
  who never see it, and because a filter displays another filter's value.
- **As little design as possible scores 1** because a single list screen carries five removable
  things, including a column of twenty-five identical values.
- **Environmentally friendly scores 0** on one rubric clause: a complete dark theme ships as bytes to
  every user and nothing turns it on.

This is not a verdict that the screens are wrong. It is a verdict that the layer nobody was allowed to
edit is now the layer holding the score down, and that it needs opening rather than polishing.

---

## The five highest-leverage moves

**1. Principle #6, honest — stop recording consent to a document that does not exist.**
The onboarding card asks the supplier to confirm they have read the Terms & Conditions and
data-processing notice, and records the acceptance with a version. It renders a checkbox and a button
and nothing else, while the product's own About page says the terms have not been issued. Either the
document is published and linked from that card, or the card stops claiming the supplier read
anything. Evidence: `OnboardingPage.tsx:828-857`; `shots/supplier-onboarding.png`; `AboutPage`.

**2. Principle #6, honest — make the two lying dialogs tell the truth.**
The withdrawal dialog says "You cannot re-enter this tender"; `StartProposalHandler` treats a
withdrawn proposal as absent and creates a new draft while the window is open. The read-only banner
says "Your application is with a reviewer" to every non-editable supplier, including an Approved one,
under a page headed "Complete Your Supplier Profile". Evidence: `config.ts:3372` →
`SupplierProposalPage.tsx:447` against `StartProposalHandler`; `OnboardingPage.tsx:536-537,:589`.

**3. Principle #4, understandable — one word per object, and no raw values.**
"RFQ" appears 27 times in strings shown to the supplier whose own navigation calls the same thing a
tender; "Addenda" ships to the same reader; `tour_operations` is rendered as a category value. And the
Assignee filter on the review queue displays the State filter's value. Evidence: `config.ts:2093`,
`:2401` against the nav label at `:2059`; `shots/workspace-tender.png`; DOM reading
`[0] Awaiting a decision | [1] Awaiting a decision`.

**4. Principle #10, as little design as possible — delete the five things on the tender list.**
The Owner column holds 25 identical values beside a filter that answers the same question; the
screen-reader caption repeats the card's own heading on 26 sites, so it is announced twice; the `<h1>`
and the card `<h2>` name the same list on 11 screens; the footer Help duplicates the top-bar Help; two
Search controls point at one destination. Evidence: `RfqListPage.tsx:91,:105,:59,:65`;
`Card.tsx:43` + `Table.tsx:38`; `shells/navigation.ts:97` + `TopBar.tsx:105-120`.

**5. Principle #9, environmentally friendly — decide the dark theme.**
83 contrast pairs are asserted for a theme no user can reach, and the palette ships to everyone as
bytes. Either bind it to `prefers-color-scheme`, or give it a switch, or remove it. Evidence:
`themeReachability.test.ts:35-41`.

---

## Two things worth doing that the rubric does not score

**The officer's landing screen supports no task.** Zero focusable elements in `main`; a 16-stop Tab
sweep never enters it. It is a card of permission chips. Evidence: `shots/officer-dashboard.png` and
a live measurement.

**The accessibility instruments claim more than they check.** The sweep that names WCAG 2.2 exercises
exactly one 2.2 rule; it discards axe's `incomplete` bucket, never scans a narrow viewport, an
interacted state or the dark theme, and runs on fixtures returning one row against a database of
thirty-one suppliers. Activating the skip link moves no focus at all. None of this is a design defect
today — it is the measuring equipment being weaker than its own documentation says, which is how the
defects this audit found survived six phases of work.
