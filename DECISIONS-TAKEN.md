# Decisions taken

Decisions made in the course of building, that were **not** settled by a document and are now settled
here. Newest last.

This file exists because `docs/` is read-only and externally owned. A ruling that changes what the
product does has to live somewhere a reader can find it, and a code comment is not findable by
someone asking "why does it behave like that". Where a decision contradicts something in `docs/`,
its row says so plainly rather than quietly diverging.

**How to read a row.** The **Why** is the field that matters: it is written for someone deciding
whether to overturn the decision, not for someone auditing that a decision was made. "No document
specifies this" is never a reason.

**The standing principle behind all of them.** A default under-serves rather than over-discloses, and
is one line to change when the real answer arrives. Where any choice would produce an *outcome*
rather than a *posture* — a tie-break, an approval threshold, who wins — the system decides nothing
and surfaces the case to a person. That refusal is itself the implementable decision.

---

## Completeness

D-6 to D-16 were made before this file existed and live in the plan of record, which is not in this
repository. **All eleven are now transcribed below.** Seven came from wording quoted verbatim in a
batch prompt or in `BACKLOG-REMEDIATION.md`; the remaining four — D-11, D-13, D-14 and D-16 — were
supplied from the plan of record on 2026-09-05, after this file had recorded them as missing rather
than reconstructing them.

D-17 onward were made in the course of the work and are recorded here as they were taken.

## The log

### D-6 — The Ministry viewer sees governance data, and no commercial figures

| | |
|---|---|
| **What was undecided** | Whether a ministry_viewer may see contract values, bid prices and award amounts across organizations. |
| **Where the gap is** | `OQ-001`, open. BRULE-086 grants "read-only, cross-organization access to aggregate/governance metrics only"; BRULE-087 defaults to aggregate-only where visibility is undecided. Neither says what a "metric" may contain. |
| **What was decided** | `[MinistryViewer]` gets governance content. Every commercial figure sits behind one flag, defaulted off. |
| **Why** | The two errors are not symmetric. Withholding a figure from a ministry viewer produces a request — they ask, someone answers, the flag flips the same day. Disclosing a competitor's bid price across organizations cannot be recalled, and in a live tender it is a procurement-integrity failure rather than a UI complaint. One flag rather than a conditional per screen is what makes MOT Legal's answer a value change instead of an epic. |
| **What it costs if wrong** | If too narrow: a persona sees less than it is entitled to, fixed by flipping one flag. If too wide: unrecallable disclosure. |
| **Who should confirm it** | MOT Legal, on `OQ-001`. |

### D-7 — Proposal attachments carry an envelope, and unstated means Commercial

| | |
|---|---|
| **What was undecided** | Which proposal attachments are technical and which commercial, and who says so. |
| **Where the gap is** | No `FEAT` or `BRULE` assigns envelopes to proposal attachments. `ProposalDocument` had no such field. |
| **What was decided** | The uploading supplier declares it. Unstated is `Commercial`, in the endpoint's parse fallback, the domain default, and the migration's default for existing rows. The buyer gate refuses both kinds until the evaluation is Consolidated. |
| **Why** | A file's contents are opaque to us — a supplier can put a priced bill of quantities inside something captioned "compliance matrix", and nothing in the system can tell. So the label has to come from the only party who knows. Defaulting to Commercial makes the failure the recoverable one: mislabelling a technical file hides it from an evaluator, who raises it within the hour; the other direction leaks a competitor's prices during scoring, silently. |
| **What it costs if wrong** | Suppliers who never set the field have every attachment gated until consolidation — visible, and fixed per file. |
| **Who should confirm it** | Procurement. |

### D-8 — A tie surfaces; the system does not break it

| | |
|---|---|
| **What was undecided** | What happens when two proposals reach the same weighted total. |
| **Where the gap is** | No documented tie-break order. |
| **What was decided** | The system decides nothing. A tie is surfaced to a named person, who records the choice. |
| **Why** | A tie-break rule produces an *outcome* — a supplier wins a contract — rather than a posture. Any rule invented here (earliest submission, highest technical score) would be a procurement policy written by an engineer, and it would be invisible in the award record afterwards. Surfacing is implementable, auditable, and does not pre-empt the policy. |
| **What it costs if wrong** | An officer must act on a tie that could have been automatic. Cheap, and the record is better for it. |
| **Who should confirm it** | Procurement. |

### D-9 — Every award follows the full approval path; there are no authority limits

| | |
|---|---|
| **What was undecided** | Whether an award below some value may skip approval steps. |
| **Where the gap is** | No documented authority thresholds. |
| **What was decided** | No thresholds. Every award routes through the documented approval path regardless of value. |
| **Why** | A threshold is an approval *outcome*, and inventing one grants spending authority nobody delegated. The conservative direction is unambiguous here: routing an award that could have been auto-approved costs an approver one click; auto-approving one that needed a signature is a control failure that shows up in an audit. |
| **What it costs if wrong** | Approvers handle more low-value awards than necessary. Adding thresholds later is additive. |
| **Who should confirm it** | Procurement, and whoever owns delegated financial authority. |

### D-10 — Scan everything, fail closed, and scan on first access

| | |
|---|---|
| **What was undecided** | Whether RFQ and proposal attachments must be virus-scanned, and by what. |
| **Where the gap is** | `OQ-014`, tagged `[REQUIRES BUSINESS CONFIRMATION]`. BRULE-019 requires quarantine for supplier documents and is silent on the other two aggregates. |
| **What was decided** | Both aggregates carry `ScanState`. The download gate scans on first access, deletes an infected object, and refuses with the same 404 as any other miss. Existing rows enter `PendingScan`, not `Clean`. |
| **Why** | Scanning on access rather than on upload avoids a backfill that would have had to walk every object in storage before anything worked. Entering existing rows as `PendingScan` is the whole decision: assuming clean would leave exactly the files that predate the scanner permanently unexamined. The refusal is the ordinary 404 because a distinct "infected" reply confirms to an uploader that their malware arrived and is being stored. |
| **What it costs if wrong** | If scanning is not required, a per-download scan is wasted work — one predicate to remove. |
| **Who should confirm it** | Security, on `OQ-014`. |

### D-11 — The review SLA has no duration

| | |
|---|---|
| **What was undecided** | How long an onboarding review may take before it is late. |
| **Where the gap is** | `BUSINESS-PROCESSES.md` §2 names the review timer three times and never gives a number. |
| **What was decided** | No threshold invented. Aging displays show a **duration**, never a breach — "in review for 6 days", never "overdue". |
| **Why** | "Elapsed" is computable from the data the system already has; "overdue" requires a number nobody has stated. Inventing one makes the product assert a commitment the ministry never made, to suppliers who may read it as one. |
| **What it costs if wrong** | Reviewers get no urgency signal, so a stalled application looks the same as a fresh one. One config value to change. |
| **Who should confirm it** | Procurement. |

### D-12 — Deadline extension is unbounded, and audited

| | |
|---|---|
| **What was undecided** | How far a submission deadline may be extended. |
| **Where the gap is** | BRULE-035 permits extension and names the actors, the audit event and the notification. It states no bound, and the whole rule carries `[ASSUMPTION]`. |
| **What was decided** | No cap. The `rfq.deadline_extended` audit row, and the notification to every invitee, are what make an abusive extension visible. |
| **Why** | A cap is a fairness rule with a number in it, and the number would be invented. Worse, a wrong cap blocks a legitimate extension during a real procurement — a supplier's country-wide outage, a corrected specification — with no override. Visibility achieves what the rule is for without pre-empting the policy: an extension that every invited supplier is told about, on the record, is not a quiet one. |
| **What it costs if wrong** | An officer can extend indefinitely. Every extension is audited and notified, so the abuse is detectable rather than silent. Adding a cap later is one guard. |
| **Who should confirm it** | Procurement. |

### D-13 — No re-application cooldown

| | |
|---|---|
| **What was undecided** | Whether a rejected supplier must wait before applying again. |
| **Where the gap is** | BRULE-012 describes the re-application policy as "configurable" and states no default. |
| **What was decided** | No cooldown. The setting exists and its default is off. |
| **Why** | The conservative reading is not to bar a supplier the documents never barred. Turning a supplier away is the harder error to discover — they simply do not come back, and nobody files a report about it — whereas letting one re-apply too soon shows up in a reviewer's queue. |
| **What it costs if wrong** | A rejected supplier can re-apply immediately, and reviewers absorb the repeat. One default value. |
| **Who should confirm it** | Procurement. |

### D-14 — `report.read` is granted to no role

| | |
|---|---|
| **What was undecided** | Who may see cross-organisation aggregate reports. |
| **Where the gap is** | No document names a holder for `report.read`. |
| **What was decided** | Granted to no role. The report screens are built and unreachable until someone grants it. |
| **Why** | A permission wrongly granted is far harder to notice than one wrongly withheld — nobody reports being able to see too much, while being locked out is reported within the hour. Cross-organisation aggregates are exactly the kind of read where that asymmetry bites. |
| **What it costs if wrong** | The reports are dead on arrival until a grant is made. It is on the first-deploy checklist. |
| **Who should confirm it** | Whoever owns roles. |

### D-15 — Child-write concurrency needs an application-managed version column

| | |
|---|---|
| **What was undecided** | How a write to a child row bumps its aggregate root's version, given `xmin` is database-generated. |
| **Where the gap is** | §8.1 specifies the ETag/If-Match contract and assumes a version that moves; it does not say what the version is. |
| **What was decided** | An application-managed version column alongside `xmin`. **Not yet built** — see T-030. |
| **Why** | `xmin` advances only when the root ROW is written, and a child insert does not write it, so a correct `If-Match` is silently skipped. Forcing a parent touch is not available: `xmin` cannot be assigned, and a second UPDATE against the same row and token is the failure `AppDbContext` already documents. That leaves a second, application-owned counter as the only mechanism that can be bumped deliberately. |
| **What it costs if wrong** | It is a second concurrency mechanism across six roots; a half-applied version of it is worse than the current gap. |
| **Who should confirm it** | The architecture owner. |

### D-16 — Signed URLs stand, and the consequence is recorded

| | |
|---|---|
| **What was undecided** | Whether downloads should be signed URLs or streamed through the application. |
| **Where the gap is** | `SECURITY-ARCHITECTURE.md` §4.2 mandates signed URLs and does not discuss what that costs. |
| **What was decided** | Signed URLs, as mandated — with the consequence written down at every mint site rather than left implicit. |
| **Why** | The application can audit that access was **granted**, never that a file was **retrieved**: a signed URL is a bearer capability the app never sees used, and anyone holding it can fetch that object until it expires. That is the documented design; recording it is what stops a later reader assuming the audit trail covers retrieval. |
| **What it costs if wrong** | If retrieval itself must be auditable for a tender challenge, that needs streaming — and a different answer to §4.2 than the document currently gives. |
| **Who should confirm it** | Security. |

### D-17 — One word for clarification: `استيضاح`, never `إيضاح`

| | |
|---|---|
| **What was undecided** | Which of two Arabic words the product uses for a clarification, after the two proposal notifications from #109 drifted from the RFQ ones. |
| **Where the gap is** | `UX-WRITING.md` §8's glossary uses `استيضاح`. §7 has no proposal-clarification strings at all, so the #109 copy was drafted with nothing to match against. |
| **What was decided** | `استيضاح` everywhere. Three strings corrected. English unchanged — "clarification" carries both senses. |
| **Why** | The two words are not synonyms: `استيضاح` is Form X, the act of ASKING; `إيضاح` is Form IV, the explanation given in reply. Both §4.1 transitions are named from the asker's side, so the drift had two notifications naming the answer where they meant the question. That makes this a wrong word rather than an inconsistent one, and settles it independently of style. |
| **What it costs if wrong** | Three strings. A supplier sees the term on a chip, in a notification and in an email about one tender, so consistency matters more than the specific choice. |
| **Who should confirm it** | The doc owner, on §8's glossary. |

*Not enforced by a test, deliberately: a blocklist on `إيضاح` would fail the build on a legitimate
future use of the word in its correct sense.*

### D-18 — Screen strings and export strings are identical, not adapted

| | |
|---|---|
| **What was undecided** | Whether the report screen and its PDF/CSV artefact may word the same heading differently. |
| **Where the gap is** | No document specifies the report screen at all — no `SCR-` row, no §7 label set. The two files are hand-maintained copies. |
| **What was decided** | Identical, in both languages. The proposed "screen terse / export self-describing" rule was rejected. |
| **Why** | The rule is defensible in the abstract and false here: both surfaces render the same table with the same column headers, so there is no context the export lacks. Examined one at a time, all three divergences were errors — a parenthetical restating its own column header, a heading that named the wrong state machine, and a marker that read as a value. Two of the three moved the screen and one moved the export, so there was no consistent direction of drift to codify. |
| **What it costs if wrong** | Three strings, and an export that says slightly less than a standalone reader might want. |
| **Who should confirm it** | The doc owner. |

*The real defect is unfixed: the two files are hand-maintained copies and nothing enforces that they
agree. The fix is a shared source of report copy across a C# generator and a TypeScript SPA.*

### D-19 — An evaluator sees the bidder's name

| | |
|---|---|
| **What was undecided** | Whether an evaluator scoring a bid may see which supplier submitted it. |
| **Where the gap is** | `ROADMAP.md` §P7 defines blindness as "each scores blind (**cannot see peers**)" — evaluator-to-evaluator. No document anywhere asks for anonymised bidders. |
| **What was decided** | The supplier's reference code and both display names travel with each bid on the evaluator's workspace. |
| **Why** | BRULE-067 gives an evaluator a recusal mechanism for conflict of interest, and that control is unusable if they cannot see whose bid it is — withholding the name would be a fail-closed default that disables a documented safeguard, which is the one case where fail-closed is the wrong instinct. The documented blindness is a different property and is unaffected: no evaluator sees another's scores at any point before consolidation, which is asserted separately. |
| **What it costs if wrong** | If the ministry wants anonymised evaluation, it is one projection to strip — but the recusal flow would need a replacement first. |
| **Who should confirm it** | Procurement. |

### D-20 — An evaluator's document list is not filtered on scan state

| | |
|---|---|
| **What was undecided** | Whether a technical document that has not yet been scanned appears in an evaluator's list. |
| **Where the gap is** | D-10 put the scan at first ACCESS. Nothing in it says what a LIST should show. |
| **What was decided** | The list shows every Technical-envelope document except `ScanRejected`. The download still scans and still refuses. |
| **Why** | Filtering the list to `Clean` made it permanently empty — nothing scans a proposal document until someone downloads it, so under that filter nobody could ever download one. That is a deadlock, not a stricter posture. `PendingScan` means "not yet examined", not "suspect", and listing a filename is not serving the file; the gate that matters is still at the download, where it can actually refuse. |
| **What it costs if wrong** | An evaluator can click a filename that then refuses. That happens only for genuinely infected files, and the refusal is the same 404 as any other miss. |
| **Who should confirm it** | Security. |

### D-21 — The award offer has no acceptance window, and the supplier cannot accept

| | |
|---|---|
| **What was undecided** | How long a supplier has to respond to an award offer, and whether accepting is a supplier action. |
| **Where the gap is** | §4.1 tags the acceptance window as `[ASSUMPTION]` with no duration, and gives `AwardOffered -> Awarded` to `procurement_manager / award.approve` "(or supplier accept, `[ASSUMPTION]`)". |
| **What was decided** | No window is enforced: the offer stays open until the supplier declines or the buyer executes the award. `AwardOfferedAt` is stored so a long-outstanding offer is visible. Accepting is not built — the manager confirms by executing. |
| **Why** | An expiring offer produces an *outcome*: a clock runs out and the award frees for an alternate, changing who wins a public contract. That is the tie-break class of decision (D-8), so the system does not make it. Building supplier acceptance would also be an invention on top of an `[ASSUMPTION]` — a second path to `Awarded` that the buyer does not control, in a flow where the buyer holds the approval authority. The offer notification deliberately states no deadline, because naming one the system does not keep is worse than naming none. |
| **What it costs if wrong** | If procurement wants a window, it is a background job plus a transition — additive, and the timestamp it needs is already stored. Until then an ignored offer blocks the award indefinitely, which is visible to the officer rather than silent. |
| **Who should confirm it** | Procurement. |

### D-22 — `validityDays` is derived on read and refused on write

| | |
|---|---|
| **What was undecided** | Which event a proposal's validity duration counts from. |
| **Where the gap is** | §12.5's create request is `{ "currency": "SYP", "validityDays": 30 }`. This schema stores `validityStart` and `validityEnd`. No document says whether the clock starts at creation, at submission, or at award. |
| **What was decided** | `validityDays` is emitted on `ProposalDto`, derived as end − start. It is **not** accepted on any request; the two dates remain the only way to set validity. |
| **Why** | Two dates carry strictly more information than one duration, so deriving the duration loses nothing and conforms the response half without deciding anything. Accepting the duration is the half that needs an anchor, and picking one silently fixes a supplier's bid validity to the wrong event — a bid that expires days before anyone thought it would is discovered at award, which is the worst moment. Null rather than zero when either date is missing: a duration measured from nothing is not a duration of nothing. |
| **What it costs if wrong** | If procurement names an anchor, accepting `validityDays` becomes additive — the derived read already matches. |
| **Who should confirm it** | Procurement. |

### D-23 — Shortening a submission window needs its own permission, and both checks live in the handler

| | |
|---|---|
| **What was undecided** | Which permission expresses "shortening requires `procurement_manager`", and where it is enforced. |
| **Where the gap is** | BRULE-035 names the two actors — officer extends, manager shortens — and names no permission for either direction. |
| **What was decided** | A new `rfq.deadline.shorten`, granted to `procurement_manager` only. Extension stays under the officer's existing `rfq.edit`. **Both** checks are in the handler; the route requires only authentication. |
| **Why** | The direction decides the actor, and the direction is only knowable after reading the RFQ's current deadline — so no route filter can express the rule. This was got wrong first: a route requiring `rfq.edit` 403'd the manager, who does not hold it, which is the exact caller the rule names for shortening. A separate permission rather than reusing a manager-only one such as `rfq.approve`, because overloading that would silently hand the power to cut a live tender short to everyone granted approval authority. |
| **What it costs if wrong** | If the ministry wants both directions with one role, it is one grant. The permission name is an invention; the policy is not. |
| **Who should confirm it** | Procurement, and whoever owns roles. |

### D-24 — A deadline change notifies both ways, and carries no date

| | |
|---|---|
| **What was undecided** | Whether shortening notifies invitees, and whether the notification names the new date. |
| **Where the gap is** | BRULE-035 says "notify all invitees" for **extension** and says nothing about shortening. BRULE-091's payload allow-list says what a notification may carry. |
| **What was decided** | Both directions notify, under two separate types. Neither payload carries the date; the copy points at the RFQ. |
| **Why** | A window closing **earlier** is the change a bidder must hear about most urgently — a supplier planning to submit on the old date otherwise discovers the new one by being refused — so the extra notification is the addition, not the omission. The date was tried as a payload key and the allow-list gate refused it, correctly: that list's own rule is that content belongs in the authored copy or behind the link, which is the same treatment `award.rejected` gives a rejection reason. Widening the gate to make copy read better is precisely the accident it exists to prevent. |
| **What it costs if wrong** | An invitee must open the RFQ to see the new date. If procurement wants it in the notification, that is a deliberate allow-list entry with its own reasoning. |
| **Who should confirm it** | Procurement, on the second notification; security, on the allow-list. |

### D-25 — The registration response stays enumeration-safe, against §12.1

| | |
|---|---|
| **What was undecided** | Whether to conform the register response to §12.1, which documents `201 Created`, a `Location` header naming the supplier, four extra fields, and `409 DUPLICATE_RESOURCE` for a taken email. |
| **Where the gap is** | §12.1 specifies all of that. `SECURITY-ARCHITECTURE.md` §1.6 and STORY-02.2.1 require that registration and resend "do not reveal whether an address exists". The two cannot both be followed. |
| **What was decided** | §12.1 is not followed here. Registration answers `200 OK` with an identical body whether or not the email is taken; only `supplierCode` differs, and it is `null` on the duplicate path. No `Location`, no `onboardingState`/`emailVerified`/`createdAt`. |
| **Why** | A `409 DUPLICATE_RESOURCE` is an account-enumeration oracle by construction — an attacker learns which addresses are registered by watching status codes, which is the exact attack §1.6 exists to prevent. So are the four extra fields and the `Location` header: each of them confirms the account exists. The security requirement is specific and the §12.1 example is illustrative, so the security requirement wins. |
| **What it costs if wrong** | An integrator following §12.1 gets 200 where it expected 201 and no `Location`. A test asserts the indistinguishability so this cannot be quietly "conformed" back by someone reading §12.1 and not this row. |
| **Who should confirm it** | Security, and the doc owner — §12.1 should probably be corrected rather than the code. |

### D-26 — The login body carries no `user` object

| | |
|---|---|
| **What was undecided** | Whether to add §12.1's `user` object — `userId`, `email`, `roles`, `permissions`, `supplierCode`, `locale`, `mfaEnabled` — to the login response. |
| **Where the gap is** | §12.1 shows it in the worked example. Nothing else in the documents requires it, and §8's token design puts roles and permissions in the access token's claims. |
| **What was decided** | Not added. `tokenType` and `expiresIn` were added, since §12.1 names both and neither duplicates anything. |
| **Why** | The SPA already reads roles and permissions out of the access token's own claims (`authStore` decodes it). A second copy in the body would be a second source of truth for **authorization** data, and the two disagree the moment a role changes mid-session — the body is a snapshot, the token is what the API actually enforces against. Adding it would invite a client to trust the stale one. |
| **What it costs if wrong** | A client that wants identity without decoding a JWT has to call a profile endpoint. If that is judged too awkward, the safe form is a `/auth/me` read rather than a copy inside the login body. |
| **Who should confirm it** | The doc owner. |

### D-27 — The row version becomes an application-managed counter, and every in-flight ETag expires on deploy

| | |
|---|---|
| **What was undecided** | How to make a child write advance its aggregate root's version, given `xmin` is database-generated and cannot be assigned. |
| **Where the gap is** | §8.1 specifies the ETag/If-Match contract and assumes a version that moves when the aggregate changes. It never says what the version is, and the `xmin` choice predates the contract. |
| **What was decided** | All nine roots move to an application-managed `RowVersion` (`bigint`, default 1), advanced in `SaveChangesAsync` for every root the change set touches — directly or through a child. **The wire contract is unchanged**: still a `uint`, still base64url in a strong ETag. |
| **Why** | `xmin` moves only when the root ROW is written, so a child insert left it untouched and a correct `If-Match` on any child-write route was silently ignored — two callers editing different children of one aggregate both won. Forcing a parent touch was not available: `xmin` cannot be assigned, and a second UPDATE against the same row and token is the failure `AppDbContext` already documents. A counter the application owns is the only thing that can be bumped deliberately. Keeping the property a `uint` was the choice that made this landable: 34 ETag and If-Match sites, and the entire SPA, did not have to move. |
| **What it costs if wrong** | **Every ETag a client holds becomes invalid the moment this deploys** — those callers get a 412 and re-read, which is precisely the recovery §8.1 defines. There is no mapping from an `xmin` value to a counter, and seeding the counter from `xmin` would manufacture version history that never happened. The `bigint` column is reversible; the invalidation is a one-time event at cutover. |
| **Who should confirm it** | The architecture owner, and whoever schedules the deploy — the cutover produces a burst of 412s. |

*One-hop attribution. A changed entity is attributed to its root by walking foreign keys one level. Every
aggregate here is one level deep; `AppDbContext.UnattributedChildTypes()` exposes what the walk cannot
see, and a test asserts it is empty, so a grandchild introduced later fails loudly rather than silently
failing to bump.*

### D-28 — Reference data is deactivated, never deleted, and codes are immutable

| | |
|---|---|
| **What was undecided** | What happens to rows already pointing at a reference item an administrator wants to remove. |
| **Where the gap is** | FR-ADM-004 requires the six reference tables to be manageable and says nothing about referential behaviour. Nothing in the schema helps: every one of them is referenced **by code** — `RfqItem.CategoryCode`, `Offering.UnitOfMeasureCode`, the document type on a `SupplierDocument` — with no foreign key, no cascade and no nullable fallback. |
| **What was decided** | There is no delete operation. Deactivation (`IsActive = false`) is the only removal, and it is reversible. The **code cannot be changed** once created; names and DocumentType's flags can. |
| **Why** | Deleting a Category a published RFQ item points at would leave that RFQ describing something that no longer exists, and there is no cascade to notice. Renaming a code is the same damage with a longer fuse: a historical award record would silently start reading as if it had been for a different category. Deactivation hides the code from new selections and leaves every existing row intact and readable, which is the only option that is both reversible and safe on live tender data. Inactive rows stay visible to an administrator — otherwise deactivation reads as deletion and the next administrator re-creates the code, which is the precise outcome the no-delete rule exists to prevent. |
| **What it costs if wrong** | A ministry that genuinely wants a code gone must live with it deactivated. Adding a delete later is possible but needs a referential-integrity pass first — the point of this decision is that it must not be added *without* one. |
| **Who should confirm it** | Procurement, and whoever owns the data model. |

*A new `DocumentType` defaults to not-required and not-expiry-tracked when the caller says nothing:
required-by-default would retroactively make every existing supplier's profile incomplete the moment the
row was created, which is a live consequence for people who did nothing.*

### D-29 — Idempotency reserves by unique index, and an in-flight key is a conflict

| | |
|---|---|
| **What was undecided** | How to make the reservation and the handler's own write atomic, and what to answer when a key's first request has not finished. |
| **Where the gap is** | §8.2 specifies the contract's five clauses and says nothing about concurrency or partial failure. |
| **What was decided** | The filter INSERTs a reservation row before the handler runs; the unique index on `(UserId, Key)` decides the race. A key whose record exists with no stored response yet is a **409**, not a wait. Only a 2xx is stored for replay. The reservation and the handler's write are **not** in one transaction. |
| **Why** | A unique index refuses the second click without a read-then-write race, which is the flaw a "check then insert" would have. Blocking on an in-flight key would hold a request thread on a bet about another request's progress, and replaying nothing would be a lie — a client that gets the conflict retries with a new key, which is correct. Storing 4xx responses would pin a client to its own mistake for 24 hours with no way to correct the request. The transaction is left open deliberately: if the process dies after the handler commits but before the response is recorded, the work still happened **exactly once** — the client learns it by a 409 instead of a replay. Making the two atomic requires the filter to own every handler's transaction, which is a change to every handler's contract, and half-doing it would be worse than the gap. |
| **What it costs if wrong** | The narrow window above turns a replay into a conflict, so a client retries with a new key and meets the state guard — which refuses, rather than duplicating. No double submission is possible either way. |
| **Who should confirm it** | The architecture owner. |

*Two shapes were corrected while building. The response is stored as `text`, not `jsonb`: jsonb
normalises, reordering keys and re-spacing, so a replay came back byte-different from the original and
§8.2.3's "verbatim" was not met. And the record is keyed `(UserId, Key)` rather than `Key` alone —
the key is client-generated, so a shared key space would let one caller replay another caller's
response, which is a disclosure rather than a duplicate.*

### D-30 — Role defaults are seeded per permission, not once per role

| | |
|---|---|
| **What was undecided** | Whether a permission newly added to a role's defaults in code should reach an environment whose roles already exist. |
| **Where the gap is** | Nothing specifies it. `RoleSeeder` wrote one `perms:seeded` claim per role and skipped that role on every later start — a reasonable-looking guard whose purpose was to stop the seeder undoing an administrator's edits. |
| **What was decided** | The marker is per permission (`perms:offered:<permission>`). A permission with no marker has never been offered, so it is added; one whose marker exists is left alone, so an administrator's removal survives. A deployment carrying the old per-role marker has everything it currently holds back-filled as already-offered, so nothing an admin removed is resurrected. |
| **Why** | The per-role marker made the defaults a one-time snapshot. Adding a permission to a role in code had **no effect** on any environment whose roles already existed — so EPIC-18 would have shipped as "the Ministry dashboard 403s in production and works locally". Found exactly that way: the governance tests passed against a fresh database and failed against a reused one. Per-permission marking is the smallest change that distinguishes "new in code" from "removed by a person", which is the distinction the original guard was reaching for and could not express. |
| **What it costs if wrong** | A permission an administrator removed before this change, and which is still in `DefaultPermissions`, is treated as already-offered by the back-fill and stays removed. That is the intended reading; if a deployment wants the defaults re-applied wholesale, that is a deliberate admin action, not a startup side effect. |
| **Who should confirm it** | Whoever owns roles. |


### D-31 — "Invite-only" registration is implemented as a closed front door, not as a supplier invitation mechanism

| | |
|---|---|
| **What was undecided** | What FR-REG-002's "invite-only" mode actually does. |
| **Where the gap is** | FR-REG-002 names two modes — "open self-registration vs. invite-only", default open — and carries `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. Nothing anywhere describes an invitation for a supplier who does not yet exist: staff invitations invite staff, and RFQ invitations invite suppliers already registered and verified. |
| **What was decided** | The setting has two values, `open` and `closed`. `closed` refuses `POST /api/v1/auth/register` with 403 `REGISTRATION_CLOSED` and a detail telling the applicant to contact the Ministry; the SPA replaces the form with the same message. No prospective-supplier invitation entity, token or email was invented. |
| **Why** | The half of "invite-only" that is fully specified is the refusal, and it is implementable and testable today. The other half is a feature: an invitation for someone with no account needs a token, an expiry, a single-use guard, an email, an acceptance route and a seventh state on the supplier lifecycle — every one of which is a decision the documents have not made. Building a guess at that would produce a mechanism the Ministry has to live with; refusing registration produces a portal that behaves correctly under both modes and can gain invitations later without changing what `closed` means. |
| **What it costs if wrong** | If the Ministry meant "closed to the public but open to people we email a link to", the closed mode is currently a dead end for those applicants and staff must onboard them another way. Recoverable: adding invitations later only widens what `closed` permits, and no data written under this reading becomes wrong. |
| **Who should confirm it** | MOT procurement, alongside FR-REG-002's own open question. |

### D-32 — A stored setting beats configuration, and configuration beats the built-in default

| | |
|---|---|
| **What was undecided** | Which source wins for the two settings that already had an appsettings key before the settings table existed. |
| **Where the gap is** | FR-ADM-006 says `system_admin` configures these values. It does not say what happens to a deployment that had already set `Documents:ExpiringSoonWindowDays` in its own configuration. |
| **What was decided** | Precedence is: the stored row if one EXISTS, then the deployment's configuration, then the definition's default. No rows are seeded, so the table takes over a setting only when an administrator actually changes it. |
| **Why** | "Database always wins" would have silently reset every deployment that had configured the expiry cadence on purpose back to 30/14/3 the moment this shipped — a behaviour change nobody asked for, attributable to nothing an operator did. Seeding the defaults would have caused the same thing while also erasing the difference between "nobody has decided" and "an administrator chose 30", which is the fact the audit trail and the screen's own overridden/default badge exist to carry. |
| **What it costs if wrong** | An operator who expects appsettings to be authoritative can be overridden by an administrator through the screen, and the appsettings value then does nothing. The screen states which settings are overridden and when, so the surprise is visible rather than silent. |
| **Who should confirm it** | Whoever owns deployment configuration. |

### D-33 — The numeral system and the approval hierarchy are not system settings

| | |
|---|---|
| **What was undecided** | Whether FR-ADM-006's five named settings all belong in a settings table. |
| **Where the gap is** | FR-ADM-006 lists "registration mode, default currency, numeral system, document-expiry windows, approval hierarchy" and carries `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. It does not say what shape any of them takes. |
| **What was decided** | Three shipped as settings. The **numeral system** did not: R-1 makes numerals a property of the locale — Arabic renders Eastern Arabic numerals, English renders Latin — and `numberingSystemFor(locale)` already implements exactly that. The **approval hierarchy** did not either: `RfqApproval` stores an ordered step list and deliberately encodes no amount-threshold routing, so configuring a hierarchy is a feature with its own state machine, recorded as T-075 rather than approximated by a value in a table. |
| **Why** | A global numeral override would let one administrator put the wrong numerals under the wrong language for every user at once, which is a regression against R-1 dressed as configurability. And a settings row that claimed to configure an approval hierarchy nothing routes on would be an artifact asserting something untrue — the pattern this codebase keeps producing and this batch keeps removing. |
| **What it costs if wrong** | If the Ministry genuinely wants numerals decoupled from language, that is a per-user preference or a locale variant, not this table, and the work is not started. If they want threshold routing, T-075 is sized and unstarted. Neither reading loses data. |
| **Who should confirm it** | MOT procurement for the hierarchy; whoever owns the Arabic-first presentation rules for numerals. |

### D-34 — An overridden notification may use only the tokens its shipped copy already names

| | |
|---|---|
| **What was undecided** | Which interpolation tokens an administrator-authored notification template is allowed to use. |
| **Where the gap is** | FR-ADM-007 asks for admin-editable AR/EN templates and says nothing about tokens. BRULE-091's allow-list governs what may be in a notification *payload*, not what a template may name. |
| **What was decided** | The permitted set for a type is derived from that type's shipped copy: every `{token}` any of its four shipped texts uses, and nothing else. A template may use a subset — copy that says less is fine — and a token outside the set is refused with the offending tokens named. |
| **Why** | Derived rather than declared because it is already exact: the payload a type carries is built to fill its shipped copy, so a token outside that set has no value behind it and would reach the supplier as the literal characters `{price}` in the middle of a sentence. That failure is invisible to everyone who could fix it — it looks like a broken portal to the recipient and looks like ordinary stored text in the notification row. Refusing at the write is the only place the author is still present. |
| **What it costs if wrong** | An administrator who wants to surface a value the payload already carries but the shipped copy never mentioned is refused, and the fix is a code change to the shipped entry. That is the strict direction; the permissive one silently ships broken sentences to suppliers. |
| **Who should confirm it** | Whoever owns the notification copy, alongside the drafted Arabic itself. |

### D-35 — No inbound ERP write path is built, and the reason is that nothing has decided what it may write

| | |
|---|---|
| **What was undecided** | Everything FR-INT-008 covers: the direction and scope of inbound sync. |
| **Where the gap is** | FR-INT-008 is priority **C**, is worded "if enabled", and carries `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` on the direction and scope themselves. No document names which entities ERP may write, which fields, how the caller authenticates, or what happens when an inbound value contradicts a portal edit — while FR-INT-006 separately requires that conflicts are "detected and queued, never silently overwritten". |
| **What was decided** | Not built, and deliberately not scaffolded. T-063 is sized below with the four questions an implementation needs answered first. |
| **Why** | Every implementable reading of this requirement creates an externally reachable path that MUTATES portal domain state. That is not a default anyone can revise later the way a wrong default currency can be revised: an inbound writer with the wrong scope corrupts rows whose provenance is then unrecoverable, and an inbound writer with the wrong authentication is a hole in the boundary the whole security architecture is built on. This batch's standing rule is to decide rather than stall — the decision here is that the recoverable failure is shipping nothing, and the unrecoverable one is guessing. It is also the only item in this phase where a partial build would be scaffolding rather than a scoped deliverable: an ACL envelope that validates and dead-letters, with nothing permitted to pass through it, is an artifact asserting a capability that does not exist. |
| **What it costs if wrong** | If ERP integration is scheduled sooner than the C priority implies, this is the item that is not started. Nothing else depends on it: FR-INT-001 through FR-INT-007 (outbox, dispatcher, supplier-master sync, award→PO, `ExternalId`, sync fields, degraded operation) are all built and are the outbound direction. |
| **Who should confirm it** | MOT IT together with whoever owns ERPNext, answering: which entities may ERP write; which fields on each; how the caller authenticates; and what happens to an inbound value that contradicts a portal edit. |

### D-36 — Tied rankings are made deterministic using the document's first tie-break rung only

| | |
|---|---|
| **What was undecided** | The tie-break order for equal weighted totals. |
| **Where the gap is** | BRULE-069 names three rungs — highest technical score, then lowest compliant price, then earliest submission — and tags the ORDER itself `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. |
| **What was decided** | Apply the first rung (highest technical weighted score), then fall back to the proposal's own identifier as a stable residual. The remaining two rungs are not implemented and are sized as T-085. |
| **Why** | The defect being fixed is not the missing rungs — it is that the ranking had **no** tie-break at all: ordering by `WeightedTotal` alone left two equal proposals taking ranks 1 and 2 in whatever order the score rows iterated, and rank 1 is what the award flow offers. That is a defect under any tie-break order, including one nobody has confirmed yet. The first rung is the one the document names first and the only one this method has the data for. Reading "lowest price" off the financial weighted score would assume that score is inverse to price, which no document states — that would be inventing policy inside a bug fix. |
| **What it costs if wrong** | If the Ministry confirms a different order, ranks among tied proposals change and T-085 implements it. Nothing stored becomes wrong: the ranking is recomputed on every consolidation, and a re-consolidation under the confirmed order produces the correct ranks. |
| **Who should confirm it** | MOT procurement, as part of BRULE-069's own open question. |

## Part A — the seventeen rulings (batch 10)

Every row below is a **recommendation pending confirmation by its named owner**, supplied by the
product owner at the start of batch 10 and built to. Where an earlier decision in this file said
something else, that row is superseded and says so rather than being deleted.

### A-1 — Tie-breaks use BRULE-069's own order, and a full tie is surfaced rather than picked `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | The tie-break order for equal weighted totals, and what happens when every rung ties. |
| **Where the gap is** | BRULE-069 states the order parenthetically — "e.g. highest technical score, then lowest compliant price, then earliest submission" — and tags the order `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. **Checked: the document already names earliest submission as the third rung**, so the ruling's "then earliest submission as the final rung" coincides with it rather than extending it. |
| **What was decided** | All three rungs in the document's order, then: if two proposals are still tied, the consolidation marks the tie UNRESOLVED, the award flow refuses to offer rank 1 while that marker is set, and a named person resolves it with an audited reason. |
| **Why** | Earliest submission is objective, already recorded, and cannot be manipulated after the fact, which is why it is the standard final rung in public procurement. And D-8's principle applies to the residue: deterministic where the rules decide, refusing to decide where they do not. A silently-picked winner among genuine equals is the one outcome that cannot be defended after a challenge. |
| **What it costs if wrong** | The rung order is data and is recomputed on every consolidation, so a different confirmed order changes nothing stored. The surfacing path stays useful under any order. |
| **Who should confirm it** | MOT procurement. |
| **Supersedes** | D-36, which implemented the first rung only and the proposal id as a residual. |

### A-2 — The supplier tags each attachment's envelope, and the RFQ says which kind it expects `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | What decides whether a proposal attachment is technical or commercial. |
| **Where the gap is** | OQ-009 recommends a single mixed template; the two-envelope control was built anyway as the safer reading, and `ProposalDocument.Envelope` has existed since T-028 with a Commercial default. Nothing told the supplier what to tag, and no screen offered the choice. |
| **What was decided** | The supplier tags at upload; the default stays Commercial; and each `Requirement` now states the envelope it expects, so the supplier has something to tag against. |
| **Why** | The knowledge sits with whoever attaches the file. A Commercial default means a mis-tag under-serves the evaluator rather than leaking a price into the technical envelope — the direction that fails closed. |
| **What it costs if wrong** | A field and a form control. |
| **Who should confirm it** | MOT procurement. |

### A-3 — No approval threshold; every award takes the full path `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | BRULE-074's authority bands. |
| **Where the gap is** | BRULE-074 says an approver may approve only within their authority limit and that over-limit awards escalate; the bands themselves are `[ASSUMPTION]`, and §F's own preamble says the amounts are "placeholder amounts only — not real policy". |
| **What was decided** | No threshold at all. Every award follows the full approval path regardless of value. Unchanged from D-9 and restated here. |
| **Why** | A threshold nobody set would let approvals through silently — the failure mode that leaves no trace. Extra approvals are visible and annoying; skipped ones are invisible. |
| **What it costs if wrong** | Approvals that a band would have skipped still happen. Recoverable and visible. |
| **Who should confirm it** | MOT procurement. |

### A-4 — Clarification answers broadcast to every invitee, asker anonymised `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | OQ-008: whether Q&A is private to the asker or broadcast. |
| **Where the gap is** | **The documents disagree, and this is worth quoting.** BRULE-036: "Clarification Q&A is available during the open window; answers deemed material are broadcast to **all** invitees (anonymized questioner)." ASM-044 and OQ-008's recorded interim decision say the opposite: "visible to the asking supplier only by default, with an option to broadcast". The code was built to ASM-044. |
| **What was decided** | Answering publishes to every invitee. The asker's identity is never in the broadcast copy; the asker alone sees their own question attributed as theirs. Questions stay private until answered. Notifications fan out to all invitees. |
| **Why** | Equal information to all bidders is the fundamental fairness principle in tendering — a private answer hands one bidder an advantage created by the buyer. Anonymising the asker preserves what OQ-008 wanted (no bidder reveals their thinking to competitors) while removing the unfairness, and it is what BRULE-036 already says. |
| **What it costs if wrong** | A visibility flag on the answer. The `ClarificationVisibility` enum is kept, so a reversal is a default change and not a migration. |
| **Who should confirm it** | MOT procurement. |
| **Supersedes** | R-7 / the ASM-044 reading. **This is a reversal of shipped behaviour, not a new default.** |
| **Not implemented** | BRULE-036's "deemed material" qualifier. Every answer broadcasts; a materiality gate would be a flag on the answer, and gating fairness on a buyer's judgement is the direction that fails open. |

### A-5 — Review SLA: configurable, five working days, shown as a target `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | The onboarding review SLA's duration. |
| **Where the gap is** | BUSINESS-PROCESSES.md §5 starts, pauses and resumes an SLA timer across `Submitted → UnderReview → InfoRequested → Resubmitted` and never names a number. |
| **What was decided** | A system setting, default five working days, surfaced as a TARGET date on the reviewer's queue and never as a breach or a badge. |
| **Why** | The timer exists in the process and has no number, so the honest options are "no timer" or "a stated default someone can change". Calling it a target means the product never asserts a commitment the ministry did not make. |
| **What it costs if wrong** | One setting value, under D-32's precedence. |
| **Who should confirm it** | MOT procurement. |
| **Supersedes** | D-11, which recorded that no duration exists and left the timer unbuilt. |

### A-6 — Deadline extensions stay unbounded and require a reason `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | Whether a deadline extension has an upper bound. |
| **Where the gap is** | BRULE-035 permits extension while Published/SubmissionOpen and requires notification; it names no bound and is `[ASSUMPTION]`. |
| **What was decided** | No cap. A reason is mandatory, free text, audited, and included in the notification to every invitee. |
| **Why** | A cap invents a fairness rule. A required reason makes every extension defensible or obviously indefensible without inventing one — and a supplier being told WHY their deadline moved is simply better than being told that it did. |
| **What it costs if wrong** | A bound is a validator. |
| **Who should confirm it** | MOT procurement. |

### A-7 — An RFQ has an owning officer, and the approver is resolved from the assignment `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | Who "the officer" and "the approver" are, as people rather than as roles. |
| **Where the gap is** | Nothing in the documents gives an RFQ an owner. BRULE-029 scopes an RFQ to its Organization and stops there; every notification rule that says "the officer" therefore reaches a pool. SCREEN-SPECIFICATIONS' procurement dashboard lists an "Awaiting my action" tile with no mechanism behind it, which is why it was scoped to an invention when it was built. |
| **What was decided** | `Rfq.OwnerUserId`, set to the creator at creation, reassignable with an audit row, and the approver resolved from the assignment rather than from the role claim. |
| **Why** | "Notify the officer" reaching a pool is an accountability gap in a tender: no individual is on record as responsible. This is the underlying fix for a gap that has surfaced in three separate epics. |
| **What it costs if wrong** | Ownership is one nullable column and a reassignment endpoint. |
| **Who should confirm it** | MOT procurement. |
| **Built** | Batch 10. `Rfq.OwnerUserId` set to the creator; `POST /rfqs/{code}/reassign` behind the new `rfq.reassign` permission, guarded by `If-Match` and writing an audit row whose `changes` carry both ids; all six "the officer" notification sites resolve through `NotificationRecipients.RfqOwnerAsync`; the review pass records the manager it was assigned to, separately from whoever decided it; SCR-400's "Awaiting my action" tile counts what this caller is answerable for rather than what the organization holds; the buyer RFQ list carries the owner and a `?owner=me\|unassigned\|<id>` filter reusing the review queue's own three-value shape. Four consequences the ruling did not settle are logged as [[D-38]] (unowned RFQs), [[D-39]] (a deactivated owner), [[D-40]] (who may reassign) and [[D-41]] (why approval routing stays unnamed). |

### A-8 — Bidders are anonymous during scoring and revealed after consolidation `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | Whether an evaluator sees which supplier submitted the proposal they are scoring. |
| **Where the gap is** | BRULE-067 requires a conflicted evaluator to be "recused (unassigned) before submitting" and is `[ASSUMPTION]`. Nothing says whether scoring is blind. D-19 widened the evaluator's view to include the bidder name precisely so recusal was possible. |
| **What was decided** | Anonymous during scoring, revealed after consolidation. Recusal moves to assignment time: the evaluator is shown the bidder list once, when the assignment is offered, declares any conflict, and is then recused or proceeds — after which scoring is anonymous. |
| **Why** | This is what makes anonymised evaluation compatible with BRULE-067 rather than in conflict with it. Nobody has to recuse themselves from a bidder they cannot see, because the declaration already happened. |
| **What it costs if wrong** | A projection flag. |
| **Who should confirm it** | MOT procurement. |
| **Supersedes** | D-19. **This reverses a shipped widening.** |

### A-9 — Two new terminal proposal states: Lapsed and Cancelled `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | What happens to a draft that ran out of time (BRULE-052) and to a live proposal whose RFQ was cancelled (BRULE-056). |
| **Where the gap is** | BRULE-052 is `[ASSUMPTION]`; BRULE-056 carries no tag at all, so its half-enforcement was a confirmed rule going unenforced (found in batch 9 phase 12b). |
| **What was decided** | Two states, both terminal: `Lapsed` (the window closed on a draft) and `Cancelled` (the RFQ was withdrawn beneath it). |
| **Why** | They are different events and the supplier reading their proposal list must be able to tell them apart. "You ran out of time" and "the tender was withdrawn" are not the same message, and collapsing them into one state would make the product say the wrong one half the time. |
| **What it costs if wrong** | Two enum members. The work is the consumer enumeration, not the members. |
| **Who should confirm it** | MOT procurement. |

### A-10 — The Ministry sees no commercial figures `[recommended — awaiting MOT Legal]`

| | |
|---|---|
| **What was undecided** | OQ-001: whether Ministry surfaces may carry commercial values. |
| **What was decided** | Keep the fail-closed default. Aggregate, anonymised governance data only; no commercial figures. Unchanged from D-6 and restated here as a ruling rather than an interim. |
| **Why** | Too narrow means a viewer asks for more. Too wide means bid data reaches a party not entitled to it, in a government tender, irreversibly. |
| **What it costs if wrong** | The flag already exists (`GovernanceVisibility.commercialValues`, seeded off), so widening is a row. |
| **Who should confirm it** | MOT Legal. |

### A-11 — AV scanning stays as built `[recommended — awaiting security]`

| | |
|---|---|
| **What was undecided** | OQ-014's scanning scope. |
| **What was decided** | Unchanged: everything scanned, fail-closed, pre-scanner rows `PendingScan` and scanned on first access, an infected file answering the same 404 as any miss. |
| **Why** | Confirmed as correct by the owner; recorded so the next sweep does not re-open it. |
| **What it costs if wrong** | Nothing is unbuilt by this ruling. |
| **Who should confirm it** | Security. |

### A-12 — Signed URLs stay, and the grant stays audited `[recommended — awaiting security]`

| | |
|---|---|
| **What was undecided** | Whether retrieval itself must be auditable, which signed URLs cannot make it. |
| **What was decided** | Keep signed URLs per §4.2 and keep auditing the grant with document, actor and time. Do not build streaming to close the gap. |
| **Why** | The limitation is real — the app can never know a file was actually fetched — but it is §4.2's own consequence, and it is now written down rather than discovered later. |
| **What it costs if wrong** | Streaming is a second retrieval path, buildable when someone states that retrieval must be auditable. |
| **Who should confirm it** | Security. |
| **Supersedes** | D-16, which recorded the same conclusion as an interim. |

### A-13 — §12.1's register shape stays anti-enumeration `[recommended — awaiting the doc owner]`

| | |
|---|---|
| **What was decided** | Keep the built behaviour: §1.6 is specific, §12.1's shape is illustrative, and the divergence has a test asserting it. |
| **Why** | Confirmed by the owner. |
| **Who should confirm it** | The document owner. |
| **Supersedes** | D-25, as a ruling rather than an interim. |

### A-14 — A locked account answers 423, not §12.1's 429 `[recommended — awaiting the doc owner]`

| | |
|---|---|
| **What was decided** | Keep 423. |
| **Why** | A client cannot distinguish rate-limiting from a locked account if they share a status, and telling them apart is the point. |
| **Who should confirm it** | The document owner. |

### A-15 — §4.1 wins over §3.1 on shortlisting `[recommended — awaiting the doc owner]`

| | |
|---|---|
| **What was decided** | Keep §4.1. The proposal's own transition table is the more specific authority, and consolidation is where the threshold comparison actually happens. |
| **Who should confirm it** | The document owner. |

### A-16 — A generated permission catalogue `[recommended — awaiting the doc owner]`

| | |
|---|---|
| **What was undecided** | Nothing ratifies the permission names; every one of them is an invention against codebase convention. |
| **What was decided** | Generate `PERMISSIONS.md` at the repository root from the code: every permission, its `resource.action` name, what it gates, which roles hold it by default, and whether any document mentions it. Generated by a test that fails when the file drifts — never hand-maintained. |
| **Why** | A generated catalogue makes the whole set ratifiable in one pass instead of perpetually provisional, and it is the artefact the doc owner needs in order to answer at all. A hand-written list drifts, which is the defect this project has now fixed twice. |
| **What it costs if wrong** | The file is derived, so a renamed permission regenerates it. |
| **Who should confirm it** | The document owner. |

### A-17 — `report.read` goes to `procurement_manager` only `[recommended — awaiting the roles owner]`

| | |
|---|---|
| **What was undecided** | Which role holds `report.read`. It was granted to none, so the reports screen was reachable by nobody. |
| **What was decided** | `procurement_manager`, and only that role. |
| **Why** | The reports are cross-organisation aggregates, and `procurement_manager` already holds approval authority over the work they aggregate, so this discloses nothing that role cannot already reach case by case. Not `ministry_viewer`, whose zero-permission default is deliberate under BRULE-086; not `procurement_officer`, who has no cross-organisation remit. |
| **What it costs if wrong** | A seeder line and a role edit. |
| **Who should confirm it** | The roles owner. |
| **Note** | Stays on the first-deploy checklist: per D-30 a new default grant reaches an existing database only through the per-permission marker, so an environment seeded before this change picks it up on next start — but an administrator who had removed it keeps it removed. |
| **Supersedes** | D-14, which recorded the grant as absent and unresolved. |

### D-37 — Creating a child of a versioned aggregate is a mutation of that aggregate, and is guarded `[recommended — awaiting the doc owner]`

| | |
|---|---|
| **What was undecided** | Whether a POST that creates a CHILD of an existing aggregate needs §8.1's `If-Match`. |
| **Where the gap is** | §8.1 describes its guarded mutations as PUT/PATCH and transition POSTs on existing resources. It does not say which side of that line `POST /suppliers/me/contacts` falls on, and a test asserted — reasonably, on the wording — that a creation POST is not guarded. |
| **What was decided** | A POST that creates a child of a versioned aggregate requires `If-Match`; a POST that creates a top-level resource does not. T-030 split (3) applies this to the supplier's 21 child-write routes. |
| **Why** | The aggregate's version moves either way, so the choice is only about whether anyone is told. Without the precondition a caller can add a contact on top of a profile they never saw — one a reviewer has just put back into `InfoRequested`, whose flagged-field rules they are unaware of — and the write succeeds against a state they were not looking at. A top-level create is different in kind: there is no prior version anyone could have read, and requiring one would make authoring impossible. |
| **What it costs if wrong** | It is a filter per route. If the doc owner reads §8.1 as excluding creation, removing it is one line each — and the version still moves, so nothing stored becomes wrong. |
| **Who should confirm it** | The document owner, alongside A-13's other §8.1 readings. |

### D-38 — An RFQ with no owner falls back to the pool everywhere, rather than to nobody `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | What A-7's ownership means for the RFQs that already exist. A-7 gives an RFQ an owning officer set at creation; every RFQ created before it has none, and nothing in the ruling says what happens to them. |
| **Where the gap is** | Three places consult the owner: the six §3.1 notification rules that read "the officer", SCR-400's "Awaiting my action" tile, and the buyer RFQ list's new `?owner=` filter. Each of them needs an answer for `OwnerUserId == null`, and "no owner" could mean either "belongs to nobody" or "belongs to everybody who could act". |
| **What was decided** | Unowned means unclaimed, not orphaned. A notification about an unowned RFQ goes to the whole officer pool — exactly as it did before A-7. Its next action counts on the tile of everyone holding the permission that action needs. The list surfaces it under `?owner=unassigned` and labels the row "Unassigned" in words. Backfilling an owner from the audit trail's `rfq_created` actor was considered and rejected. |
| **Why** | The two readings fail in opposite directions and only one of them is recoverable. "Belongs to nobody" means a live tender transitions and no human being is told — the exact failure A-7 exists to prevent, made worse because it would be introduced by the fix. "Belongs to everybody" is the behaviour that shipped for nine batches; it is merely imprecise. On the backfill: the creator of an RFQ is not necessarily its owner today, and a guess written into an ownership column is indistinguishable from a fact — it would be quoted back in an audit as though somebody had decided it. A visible "Unassigned" that an officer can claim states the truth and asks for the one thing that resolves it. |
| **What it costs if wrong** | Nothing stored is wrong either way; it is a fallback branch in one method (`NotificationRecipients.RfqOwnerAsync`) and one predicate in the dashboard count. If procurement would rather unowned RFQs reached nobody, both are a deletion. If they want the backfill, it is one `UPDATE ... FROM audit_log`, and it can be run at any time. |
| **Who should confirm it** | MOT procurement, alongside A-7 itself. |

### D-39 — The same fallback covers an owner whose account has been deactivated `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | What happens to an RFQ whose owner has left. |
| **Where the gap is** | T-077 (batch 10) made staff deactivation reachable for the first time, and A-7 then made one named person the sole recipient of six notification rules. The two together create a state nothing had to handle before: an RFQ pointing at an account that can no longer read anything. |
| **What was decided** | An inactive owner is treated as no owner: the notification goes to the pool, and the RFQ keeps its stored `OwnerUserId` so the audit trail still says who was responsible at the time. The ownership is not cleared. |
| **Why** | Clearing it would rewrite history to make the present tidy — the RFQ *was* theirs, and a reader asking "who was handling this in March" deserves the answer. Leaving the notification pointed at them would mean a returned-for-edits RFQ that nobody alive is told about, which is the same silent failure as the unowned case. A manager reassigning it is the real fix, and the pool notification is what prompts one. |
| **What it costs if wrong** | One `&& u.IsActive` clause. |
| **Who should confirm it** | MOT procurement. |

### D-40 — Reassignment is the manager's, not the owner's `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | Who may move ownership. A-7 says an RFQ is "reassignable with an audit row" and does not say by whom. |
| **What was decided** | A new permission, `rfq.reassign`, held by `procurement_manager` and `system_admin` and deliberately **not** by `procurement_officer` — so an owner cannot hand their own RFQ away. |
| **Why** | A-7's purpose is to put an individual on record as answerable for a tender. An owner who may reassign at will can stop being answerable without anyone deciding that they should, which returns the accountability to a pool by a different route. An officer who genuinely cannot continue asks their manager, and the audit row then records the request and its reason. |
| **What it costs if wrong** | One entry in `Roles.DefaultPermissions`, and roles are admin-editable at runtime (FR-ADM-002) — so a ministry that disagrees can grant it on SCR-716 without a deploy. |
| **Who should confirm it** | MOT procurement. |

### D-41 — Approval routing stays unnamed; the nomination is optional `[recommended — awaiting procurement]`

| | |
|---|---|
| **What was undecided** | A-7 says "the approver resolved from the assignment rather than from the role claim". It does not say what assigns the approver. |
| **Where the gap is** | `RfqApproval.ApproverUserId` was written at the moment of decision, so before a decision there was nobody to notify and "notify the approver" reached every `procurement_manager` in the organization. Choosing one automatically would require a routing rule, and there is none: BRULE-072/074's amount thresholds are `[ASSUMPTION]`, OQ-004's approval chain is open, and T-075 is the backlog row for it. |
| **What was decided** | The officer submitting for review MAY name the approver, recorded on the pending step as `AssignedApproverUserId` — separate from `ApproverUserId`, which continues to record who actually decided. When a pass names nobody, the manager pool is notified exactly as before. The nominee must hold `rfq.approve` in the RFQ's own organization and be active, refused as 422 `INELIGIBLE_USER` rather than silently ignored. |
| **Why** | This is the half of A-7 that can be built without inventing the half that is undecided. A default here would produce an outcome rather than a posture — an approval routed to a specific manager who was never chosen — so the system decides nothing and surfaces the case to a person, which is the pattern the rest of this batch follows. Keeping the nomination and the decision in separate columns matters because a nominated approver who is unavailable and the colleague who decides in their place are two different people, and a trail that keeps only the second cannot answer who was asked. |
| **What it costs if wrong** | One nullable column and an optional request field. If T-075 later brings a real routing rule, it fills the same column and this stays the manual override. |
| **Who should confirm it** | MOT procurement, alongside T-075. |

### D-42 — A supplier's writes to a buyer's RFQ are not version-guarded `[recommended — awaiting the doc owner]`

| | |
|---|---|
| **What was undecided** | T-030 split (2) guards the RFQ's child writes with §8.1's `If-Match`. Two of the routes on that group belong to the SUPPLIER — asking a clarification question, and declining an invitation — and §8.1 does not distinguish by who is writing. |
| **Where the gap is** | The precondition is not obtainable. `SupplierRfqDto` deliberately carries no `RowVersion`: it is the buyer aggregate's version, and the supplier-facing shape is narrower on purpose (FEAT-08.6). So a guard on those two routes would answer 428 to every invited supplier, on the one screen where they decide whether to bid. Adding the version to the supplier's read would fix the obtainability and create a worse problem, described below. |
| **What was decided** | Both routes stay unguarded, and the supplier's read keeps carrying no version. Recorded as a deliberate exclusion in the route group's own comment and asserted by a test, so a later sweep does not "finish the job". |
| **Why** | Two reasons, and the second is the one that settles it. First, a guard nobody can satisfy refuses every caller — the same lesson T-029 recorded when `SupplierFieldConfig` gained a single-item read in the same change as its guard. Second, and more important: the concurrency it would be guarding is not a lost update. Two invited suppliers asking unrelated questions about the same tender are not overwriting each other's work; neither can see the other's question, and neither's write invalidates the other's. Refusing the second because the first moved a version they cannot observe would make one supplier's participation depend on another supplier's timing — a fairness problem in a public tender, introduced in the name of a safety property that was not at risk. §8.1 exists to stop a writer clobbering a state they were shown; these suppliers were shown nothing that changed. |
| **What it costs if wrong** | If the document owner reads §8.1 as covering every write to a versioned aggregate regardless of actor, the change is two filters plus a version on `SupplierRfqDto` — and the fairness consequence above would need answering first. |
| **Who should confirm it** | The document owner, alongside D-37 and A-13's other §8.1 readings. |
