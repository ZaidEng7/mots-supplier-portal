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

