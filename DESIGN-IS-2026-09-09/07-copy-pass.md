# Phase 5 · the copy pass, and what it refused to do

Run against §C2 (five label-to-behaviour mismatches), §C4 (23 jargon items) and §C5 (interchangeable
error strings) of `01-evidence.md`, under the direction contract's §5a: **this work changes how the
product reads, not what it does. Where a label and the behaviour disagree, the words change, not the
code. A genuine behaviour defect goes to the product owner as a question.**

Everything below is one of four verdicts: **fixed in English**, **fixed in both languages**, **kept, with
the reason**, or **raised as a question**.

## The finding that shaped the whole pass

**The jargon was almost entirely an English problem.** On seven of the audit's items the Arabic already
said the plain thing and the English carried the acronym, the Latin term, or a contradiction:

| The thing | Arabic already said | English said | Now |
|---|---|---|---|
| The minimum a criterion must reach | الحد الأدنى, in both places | `Threshold` on one screen, `Minimum` on the other | `Minimum score`, both places |
| Delivery terms | شروط التسليم (Incoterm) | `Incoterm`, bare, twice | `Delivery terms (Incoterm)` |
| Combining the evaluators' scores | توحيد النتائج ("unify the results") | `Consolidate` | `Combine scores` |
| An evaluator leaving an evaluation | استبعاد ("removal") | `Recuse` / `Recusal reason` | `Stand down` / `Reason for standing down` |
| The evaluator a row belongs to | معرّف المقيّم ("evaluator identifier") | `Evaluator user id` | `Evaluator ID` |
| A request for more information | طلب معلومات | `Request info`, confirmed by a button reading `Submit` | `Request information`, confirmed by `Send request` |
| The thing a supplier bids on | الطلبات, one word throughout, including the FAQ | `RFQs` in the nav, `tender` in the FAQ — **whose answer had to translate between them in one sentence**: *"Tenders you have been invited to appear in your RFQ list"* | `Tenders`, everywhere on the supplier side |

That last row is the clearest evidence in the audit that the English had a naming problem: the product's
own help text performed the translation for the reader.

**This means the pass cost three Arabic strings, not sixty.** They are logged in `ARABIC-REVIEW.md` on
D-65's terms — accepted for the demonstration build without a line-by-line read.

## Kept, with the reason

| Term | Why it stays |
|---|---|
| **Envelope** (technical / financial / commercial) | Standard procurement vocabulary in both languages (المغلف الفني / المالي), and the sealed-envelope separation is the thing it names. `proposal.financialLocked` already explains it in use |
| **Addenda** | The Arabic is الملاحق, equally the domain term. A company bidding for government work meets "addendum" on every tender |
| **RFQ, in the back office** | Officials are domain-literate, and the audit itself said the supplier-facing items matter most "because that audience is an outside company". Changed on the supplier side only |
| **"Recuse me", on the evaluator's own declaration** | That screen is where an evaluator declares a conflict of interest, and recusal is the correct word for what they are doing. The officer-facing label is the one that became plain |
| **`Evaluator ID` naming an identifier** | The field takes a user id, so the label is honest. The control is the defect, not the word — see the questions below |

## Two audit claims that did not survive inspection

Recorded because an audit that is never wrong is not being checked.

1. **§C2.2 said invitations and document expiry "neither exists as a notification type".** Invitations do
   exist, as `RfqSubmissionOpened` — `NotificationClassification.cs` says so in terms, and explains that
   the tender opening *is* the invitation as far as a bidder is concerned. Only document expiry has no
   type, because it is an email reminder. The old string was therefore **true**; what it failed to say is
   where document expiry arrives, which is why a reader could not find it in the list rendered directly
   beneath. The new string says so.
2. **§C5 said `documents.*` has no empty-state string while carrying four error strings.** Correct, and
   right: the document list is the set of required document types, drawn from reference data, and cannot
   be empty. The one thing that can be — version history — has `noHistory`. Nothing to word.

## §C5 · the error strings, and why copy cannot fix them

The audit is right that five of six read failures are interchangeable, and that a reader learns *that* it
failed and never *why*. Copy cannot fix this, because the *why* is already in the building and the screens
throw it away:

- Every API module wraps its failures in a typed error whose `message` comes from `problemMessage()`,
  which prefers the server's RFC 9457 `detail` — *"human-readable explanation of this occurrence"*.
- Write paths use it. `DocumentsPage` and `OnboardingPage` both render `err.message` when an upload fails,
  which is why an upload tells you which rule it broke.
- **Read paths discard it.** They render a static `t('x.loadFailed')` and never look at the error object.

So "Could not load the registry" is not a badly-worded string. It is a correctly-worded fallback being
used as the whole message. Rewording all 31 of them would make the catalogue longer and the screens no
more informative. **The fix is to pass the error into `QueryError` and show the server's explanation when
there is one** — which is code, and belongs to Phase 6.

## Raised as questions, not fixed

Each of these is a behaviour defect that a copy change would only paper over.

1. **`rfq.clarifications.publish` "Publish to all" is unreachable.** Guarded on
   `answer && visibility === 'PrivateToAsker'`, and answering sets both fields at once
   (`Rfq.cs:528-530`), so the combination cannot occur. The button and its "Private to asker" badge
   describe a state the domain cannot produce. **Question: should private-to-asker answers exist at all?**
   If yes, the domain needs to allow them; if no, the control and its badge should go. Wording it better
   would only make an impossible state more convincing.
2. **`rfq.closeSubmission` collects its audit reason through `window.prompt`.** Cancelling or typing only
   whitespace fires nothing and **shows no feedback**, while the copy promises the reason is recorded. The
   string written for this, `rfq.manualCloseReason`, is a dead key. **Question: confirm this should be a
   themed dialog like every other reason field on the screen**, at which point the dead key becomes its
   label.
3. **"Submit for review" also commits the approver nomination.** The select's empty option is already
   labelled "Any manager", so that half is not hidden — but nothing says the choice is committed by the
   *other* button. Saying so needs a hint slot on that field, which is markup. **Question: add the hint?**
4. **The evaluator assignment field takes a raw user id typed by hand** (`RfqDetailPage.tsx:103,386`),
   while the page already fetches the assignee list for two other pickers. **Question: make it a picker?**
   Until then the label stays honest rather than friendly.
5. **Withdrawing a proposal is terminal, has no confirmation, and is the lowest-emphasis variant in the
   system** (`ghost`). No copy tells the supplier it is final. A warning string is easy; the missing
   confirmation step is not copy. **Question: add a confirm step?**

## Also fixed, from §D3

`nav.onboarding` "Complete Profile" sat beside `nav.profile` "Profile" with nothing to tell them apart —
the Arabic distinguishes them as استكمال الملف and ملف الشركة, so the English now says **Complete profile**
and **Company profile**. Title case went to sentence case on "Back Office" and the "Notification
Preferences" page title, matching every other title in the catalogue.

## One regression this pass exposed

Changing a nav label to a longer one reflowed the supplier header and pushed the notification bell's
spacing below the WCAG 2.5.8 floor, failing axe on 18 English routes. The cause was Phase 4's: the emoji
the bell used to be rendered in the reader's system font at their system's size, so its 24px target was
adequate **by accident**, and stopped being adequate the moment it became a real 18px icon. The link is
now padded to a 24x24 target. The icon is still 18px.

226 Playwright tests pass, 642 unit tests pass.
