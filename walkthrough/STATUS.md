# Walkthrough — status

**Complete.** 96 steps, from an empty database to an issued award, every account and record created
through the interface. `walkthrough/run.sh` reproduces it from scratch in one command.

## What it covers

| Act | Steps | |
|---|---|---|
| 1 | 01–04 | The public surface, and the bootstrap admin signing in with TOTP |
| 2 | 05–08 | A buying body, then six staff invited and each accepting by email |
| 3 | 09–12 | A supplier registers and verifies its address |
| 4 | 13–21 | Profile, contacts, address, banking, categories |
| 5 | 22–25 | Documents uploaded and scanned, terms accepted, application submitted |
| 6 | 26–31 | The reviewer claims, checks and approves it |
| 6b | 32–33 | The supplier lists an offering, which is what makes it findable |
| 7 | 34–38 | The manager defines and activates a scoring template |
| 8 | 39–48 | The officer writes the tender, invites, and sends it for review |
| 9 | 49–52 | Approved, published, and the window opens on its own |
| 10–11 | 53–57 | The supplier asks a clarification; the officer answers it to all invitees |
| 12 | 58–63 | The bid: priced, answered, terms, document, submitted |
| 13–14 | 64–72 | Closed, evaluation opened, evaluator scores with bidders anonymous |
| 15 | 73–82 | Consolidate, compare, finalize, recommend, self-approval refused, approve, issue |
| 16 | 83–84 | The supplier sees the outcome; the ERP sync is inspected |
| 17 | 85–96 | Each persona's own screens |

## Running it

```bash
./walkthrough/run.sh
```

Reset, restart the API with demo seeding off, and walk. Roughly 12 minutes, most of it waiting for
the five-minute `rfq-timeline` cron to open the submission window.

Accounts and passwords are in `GUIDE.md`, regenerated on every run.

## Two things that are decisions, not defects

- **The Ministry dashboard shows no commercial figures.** Held pending an answer from MOT Legal.
  BRULE-086 grants the Ministry aggregate, cross-organization access only, and BRULE-087 defaults to
  aggregate-only wherever visibility is undecided. There is no drill-down by design.
- **`clamd` must be running or every document upload is refused.** Fail-closed: the scanner folds any
  failure into "infected" rather than storing a file it could not scan. With ClamAV stopped, onboarding
  cannot be completed — correctly.

## Orderings the product enforces

Each of these was discovered by being refused, and each is now shown in the guide rather than assumed:

- A tender cannot be sent for review with a submission window that has already started.
- It cannot be sent for review at all without at least one invited candidate, so invitations precede
  approval rather than following publication.
- A supplier is only an invitation candidate once it is Approved **and** has listed an offering — the
  categories ticked at sign-up are not enough.
- A template must exist and be activated before a tender can bind one, and binding is a precondition
  of review.
- A proposal needs a validity end date, and a requirement answer needs both languages.
- The financial envelope stays locked until the bid passes technical qualification, then must be
  scored before the evaluation can be submitted.
- Finalize and issue belong to the manager; recommend belongs to the officer; and the manager who
  recommends may not approve.
