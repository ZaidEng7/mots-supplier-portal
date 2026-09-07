# Findings from the manual walkthrough — 2026-09-07

Found by a person driving the product from an empty database, one act at a time, on `main` at
`ba2dde2`. Every entry names what was clicked, what the API log said, and whether it is fixed here or
left for later. Nothing in this file is inferred: each was reproduced on screen first.

The batch-12 walkthrough (`walkthrough/run.sh`) had already walked this same path automatically and
found none of these six. That is the useful part: an automated walk follows the path it was written
for, and a person takes the turnings it never took — pressing the button beside the one the script
presses, mistyping a date, going back to a screen the script only visits once.

## Fixed in this branch

### F-1 — Document download, approve and reject asked for `/documents/undefined`
**Fixed** — `79e732e`

A reviewer pressed Download on a submitted application and nothing happened. The log:
`GET /api/v1/documents/undefined/download-url -> 404`.

The server sends a document's public code as `documentId` (T-010, R-9); `api/documents.ts` declared
the field as `id`. Five call sites across three screens — the reviewer's download, approve and
reject; the documents centre's download and version-history keys; onboarding's download.

Caught by nothing: TypeScript was satisfied because the type was wrong about the wire rather than
internally inconsistent, and all 22 tests passed because the fixtures were written from that same
wrong type. The guard added with the fix is on the traffic instead — `vitest.setup.ts` fails any test
whose code fetches a URL containing an `undefined` path segment.

### F-2 — A reviewer could not approve or reject a single document
**Fixed** — `9d27b56`

428 on every attempt. The reviewer reads the application at `/api/v1/review/{code}` and writes its
documents at `/api/v1/suppliers/{code}/documents/{doc}/approve`. Both address one aggregate; the
client's ETag store walks a path upwards and never sideways, so the version was unreachable from the
write. Proved against the API: the same POST returns 200 when the review view's ETag is sent by hand.

Third instance of this exact shape — D-47 (proposals), D-60 (supplier profile), this.

### F-3 — Offering edit and deactivate answered 428 from the supplier's own catalogue
**Fixed** — `0ac4aef`

The catalogue lists offerings through a GET that issues no ETag, and both row actions require
`If-Match`. The single-item GET does issue one, and nothing called it — the route was added in batch
3 for exactly this purpose after T-029 recorded that a guard whose precondition cannot be obtained
refuses every caller.

Both writes now read the item first. **This narrows the concurrency window rather than closing it:**
two supplier users editing one offering can still overwrite each other inside it. Closing it properly
means carrying the version from the read the user saw, which is a change to how the catalogue holds
its data. Logged here rather than smuggled into a transport fix.

## Left for later

### F-4 — There is no way to edit a Draft tender through the interface
**Open.** Sized **S**, and the highest value of the three open items.

An officer created a tender with the submission window opening five minutes in the *past* — the
easiest mistake to make on that form, since both dates are typed by hand. `Draft` is documented as
the state where everything is editable. Nothing on the screen edits it.

The capability exists on both sides and is wired to nothing:

- `PUT /api/v1/rfqs/{code}` accepts titles, description, currency, publish date, submission window,
  clarification deadline and evaluation target
- `api/rfqs.ts` exports `updateRfqBasics`
- **no component in the frontend calls it**

So the recovery is to cancel the tender and author it again from nothing, or to have an engineer
issue the PUT — which is what was done during the walk, to avoid losing the act's work.

**The guard worth building with it.** Batch 12's router test catches a *screen* nothing links to.
This is a *capability no screen exposes*, and no instrument looks for that. The check is cheap: every
exported function in `src/frontend/src/api/*.ts` should be referenced by something outside `api/`, or
be listed as deliberately not yet surfaced — the same two-list shape as `router.test.tsx`. A sweep
with it would say how many more of these exist; right now nobody knows, and that number is the
interesting part.

### F-5 — Sign-in sends the wrong personas to the wrong shell
**Open.** Sized **S**.

Two symptoms, one cause each, both seen during the walk:

1. **A `system_admin` lands on `/evaluation`.** `LoginPage.tsx:73` asks whether the account holds
   `evaluation.score` and routes anyone who does to the evaluator's dashboard. `system_admin` holds
   all 104 permissions, including that one. The check needs to ask whether the account is *only* an
   evaluator, or ask about the role rather than the permission.
2. **A `system_admin` landed on the supplier shell at `/dashboard`**, whose dashboard call then
   404s, because sign-in honoured a stale `?redirect=/dashboard` from a previous session. Honouring
   the redirect is right; honouring one to a shell the persona cannot use is not.
   `router.tsx:239` guards the supplier layout with `ensureAuthenticated('/dashboard')` — that is
   *authenticated*, not *is a supplier*.

### F-6 — A reviewer cannot reopen an application they have already approved
**Open.** Sized **S**.

The review queue lists only reviewable states, so an approved supplier drops out of it and there is
no "decided" list. The detail page still loads by URL and still offers the document controls — which
is how the documents in this walk were eventually approved. A reviewer who wants to look back at a
decision they made has no route to it that does not involve typing a reference code.

Not the same as the batch-12 class: the screen is linked, it is the *list* that is missing. The
router guard would not catch it, and did not.

## Also confirmed, already known

**D-66 — the offering category checkbox does not re-tick until the profile is re-read.** Recorded in
`walkthrough/walk.mjs` during batch 12 and seen again here. Still not fixed; still silent on failure.
