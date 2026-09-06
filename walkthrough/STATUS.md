# Walkthrough — status

The driver (`walk.mjs`) is real and runs. It is **not finished**: it currently reaches step 21 of a
sequence that needs roughly 45, and stops where the onboarding submit button is still disabled.

## What runs today, end to end, against an empty database

| Act | Steps | State |
|---|---|---|
| 1 · public surface, bootstrap admin signs in with TOTP | 01–04 | works |
| 2 · admin invites six staff, each accepts by email link | 05–06 | works |
| 3 · supplier registers, verifies its email | 07–10 | works |
| 4 · supplier completes profile, contacts, categories | 11–19 | partial |
| 5 · documents, submit, reviewer approves | 20–21 | blocked |

Everything above **was created through the interface**. Nothing was seeded by script.

## Where it stops, and why

The submit button is correctly disabled: the profile is incomplete. Three completeness conditions are
unmet because the driver does not yet fill them:

- **Currency** — the control is a Radix `Select` named by its placeholder, and the first version drove
  it as a labelled field. Fixed in the driver; not yet re-verified.
- **Head-office address** — `fillOnboardingStep` opens the dialog and fills its text boxes, but the
  address dialog also carries a country/region select and an address-kind choice it leaves alone.
- **Bank account** — same shape, plus a currency select of its own.

`fillOnboardingStep` was written generically for three dialogs that are not actually alike. It needs
one filler per dialog.

## What is left after that

Acts 6–12, none of them started: authoring an RFQ and attaching tender documents, internal review and
approval, publishing and inviting, the supplier reading the tender and asking a clarification, the
officer answering it with the asker anonymised, drafting and pricing and submitting a proposal, closing
the window, opening evaluation, scoring with bidders anonymous, consolidating, the comparison matrix,
recommending, routing, a second manager approving, issuing, the supplier seeing the outcome, and the
ERP sync attempt. Plus each persona's own screens.

## Two things that are decisions, not defects

- **The Ministry dashboard shows no commercial figures.** Deliberate, pending MOT Legal. BRULE-086
  grants aggregate access only, and BRULE-087 defaults to aggregate-only wherever visibility is
  undecided.
- **`clamd` must be running or every document upload is refused.** Fail-closed by design: the scanner
  folds any failure into "infected" rather than storing a file it could not scan.

## Running it

```bash
./walkthrough/reset.sh          # drop, migrate, clear MailHog
# restart the API with DevSeed__Enabled=false
node walkthrough/walk.mjs
```

The bootstrap admin's TOTP secret is read from the database by the driver, so a reset needs no edit.
