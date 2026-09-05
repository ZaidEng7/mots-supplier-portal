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

