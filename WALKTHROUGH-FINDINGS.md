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

### F-7 — Nothing on a tender can be corrected, only removed and re-added
**Open.** Sized **S**, and it is F-4's sibling rather than a separate idea.

Every editable collection on the tender detail screen offers **Remove** and nothing else. Items,
requirements and attachments are all add-or-delete: a line item with the wrong quantity, a
requirement with a typo, an attachment with the wrong file — each has to be deleted and typed again.

Seen for real: an officer meant to add one line item reading "Hot lunch, primary school, per pupil
per day" with a quantity of 180,000, and ended up with three items — "Hot lunch", "primary school",
"per pupil per day" — each at quantity 1,000. Nothing on the screen corrects any of it, and by the
time it was noticed the tender had moved to `InternalReview`, where even Remove is gone.

The recovery that does exist is the manager's **Return for edits**, which puts the tender back in
`Draft` — a stage-gate meant for "this tender is wrong, think again", pressed here because a number
was mistyped. It works, and using it for this is out of proportion.

Same shape as F-4, one level down: F-4 is the tender's own fields having no editor, this is its
children having none. Whoever builds one should build both, and the API side is worth checking first
— `PUT /rfqs/{code}` already exists with nothing calling it, and the item and requirement routes may
be in the same state.

### F-8 — A published tender's attachments cannot be corrected by any route
**Open.** Sized **M**, and this one is a rule question before it is a build.

The wrong file was attached to a tender during the walk — a supplier registration certificate where
the specification should have been. By the time it was noticed the tender was `SubmissionOpen`, and
there is no way to correct it:

- `AddAttachment` and `RemoveAttachment` both call `EnsureDraftEditable()`, so attachments are
  `Draft`-only.
- `ReturnForEdits` is refused from anything but `InternalReview`, so a published tender has no path
  back to `Draft`.
- **An addendum carries no file.** `IssueAddendum` takes a title and description in both languages
  and nothing else, and it is the only content change a published tender allows.

So the only remedy is to cancel the tender and author it again — on a tender bidders have already
read, with a clarification already answered on it.

**The rule question first.** Locking a published tender's attachments is defensible: bidders price
against what they downloaded, and a file swapped underneath them is exactly what the lock exists to
prevent. But real procurement corrects published documents constantly, and the instrument for it is
an addendum that CARRIES the revised document, so the change is announced, dated, and visible to
every invitee at once. This product has the announcement and not the document.

Whether an addendum may carry an attachment is a procurement decision, not a code one. If the answer
is yes, the build is small: an attachment collection on `Addendum`, reusing the existing upload and
scan path.

### F-9 — Closing a tender early records a canned reason instead of the officer's
**Open.** Sized **S**. One field, and both the endpoint and the aggregate already take the value.

`Rfq.CloseSubmissionWindow` refuses an early close without a reason — the rule exists because
closing bidding before the advertised deadline is a decision bidders can challenge, and the answer
has to be on the record. `RfqDetailPage.tsx:241` satisfies it with a constant:

```ts
mutationFn: () => closeRfqSubmission(referenceCode, t('rfq.manualCloseReason'))
```

So the officer is never asked, and every early close in the system carries the same sentence. The
audit trail says a human closed it early and nothing about why, which is the half the rule was
written for. `closeRfqSubmission(referenceCode, reason)` already takes the string; only the prompt is
missing.

Same family as F-7 in miniature: a control that supplies an input the domain demands rather than
asking the person who has the answer.

### F-10 — The admin is offered procurement screens that cannot load for them
**Open.** Sized **S** to build, but it is a decision first.

`system_admin` opens the Procurement dashboard from their own navigation and gets "Couldn't load the
dashboard — Try again". Reproduced against the API:

    GET /api/v1/procurement/dashboard   as officer@mots.local -> 200
                                        as admin@mots.local   -> 404 RESOURCE_NOT_FOUND

BRULE-029 scopes every procurement query by the caller's buying body, and the bootstrap admin belongs
to no organization — deliberately, exactly as `ministry_viewer` does. So the query finds nothing. The
LINK is there because the navigation gates on permission, and `system_admin` holds all 104.

Third location of the shape F-5 describes: an affordance offered on a permission the persona holds,
to something the persona's data scope has no place in.

**Two answers, and choosing between them is not an engineering call:**

1. **Hide it.** Gate the procurement links on holding an organization as well as the permission —
   consistent with how the Ministry viewer is already treated, and cheap.
2. **Widen it.** Let a platform administrator read across organizations. That is a policy question
   about what an administrator may see of live procurements, and it is the same question BRULE-086
   answers for the Ministry. It should not be answered by accident here.

**The message makes it worse either way.** "Try again" invites a retry that can never succeed: this
is not a transient failure, and nothing about the screen says so. Same class as F-9 — a message that
describes the wrong thing to the person reading it.

### F-11 — Adding a contact answers 428, on a page whose other writes work
**Open.** Sized **M** — the cause is not yet pinned, and that is the honest state of it.

On `/onboarding/contacts`, **Add contact** answers 428: "This resource requires the ETag of the
version you are editing, sent as If-Match". Representatives on the same screen had saved fine.

`POST /suppliers/me/contacts` declares `RequireIfMatch` and `WithFreshETag`; the page reads the
profile through `getOwnSupplier()`, which files the version under both `/api/v1/suppliers/me` and
`/api/v1/suppliers/{code}`; and the write walks up to the first of those. On paper it should hold.

**The two mechanisms that would explain it, neither confirmed:**

1. **The sibling-child hole T-030 split (2) closed, reopening when a write returns no ETag.** The
   transport forgets every prefix on a successful mutation and then files the response's fresh
   version back at the precondition prefix — but `rememberETag` ignores a null tag. So a mutating
   route that answers without an `ETag` header leaves the aggregate's version deleted and nothing put
   back, and the NEXT write to a sibling collection has no precondition to send. That would make the
   failure depend on which write you did first, which matches: the representative saved, the contact
   did not.
2. **The page never read the profile in this tab.** The store is in-memory and per tab; a route
   transition that served the contacts page from cache without re-reading `/suppliers/me` would leave
   nothing to walk up to.

Deciding between them takes a browser with the network panel open and a deliberate sequence — read
profile, add representative, add contact — checking which responses carry an ETag. Worth doing
properly rather than fixing the symptom: this is the fifth 428 of the day, and the four before it
each had a different cause.

### F-12 — The offering dialog grows past the screen and takes its buttons with it
**Open.** Sized **S**.

Add four or five rows to **Additional attributes** on an offering and the dialog outgrows the
viewport. There is no internal scroll, so the fields keep pushing downward and Save and Cancel go off
the bottom of the screen — the form is still there, it just cannot be finished or dismissed without
zooming the browser out. The top of the dialog is clipped too: the Arabic name field is cut off above
the visible area.

A dialog whose height is driven by a repeatable row needs a max height and its own scroll region,
with the action row pinned. It is worth checking every other dialog with a repeater in it at the same
time rather than fixing this one: the same shape is likely wherever "add another row" exists.

## Also confirmed, already known

**D-66 — the offering category checkbox does not re-tick until the profile is re-read.** Recorded in
`walkthrough/walk.mjs` during batch 12 and seen again here. Still not fixed; still silent on failure.
