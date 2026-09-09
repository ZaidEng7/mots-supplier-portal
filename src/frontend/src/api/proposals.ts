import { hasCode, hasProblemProse, problemMessage, type ProblemDetails } from './problem'
import { apiFetch } from './auth'
import { rememberETag } from './etags'

export type ProposalState =
  | 'Draft' | 'Submitted' | 'Withdrawn' | 'UnderReview' | 'ClarificationRequested' | 'Revised'
  | 'Shortlisted' | 'NotSelected' | 'AwardOffered' | 'Awarded' | 'Declined'
  // A-9: both terminal. Lapsed = the window closed on a draft; Cancelled = the RFQ was withdrawn
  // beneath it. Two states rather than one because a supplier reading their list has to be able to
  // tell "you ran out of time" from "the tender was withdrawn".
  | 'Lapsed' | 'Cancelled'

/** FEAT-09.1/FR-PRP-002, OQ-009 two-envelope: the FINANCIAL content. Only ever present in a
 * response to the owning supplier's own request - see backend ProposalDtoMapper.ToDto's own
 * doc comment for the actual seal mechanism. */
export interface ProposalItem {
  id: string
  rfqItemId: string
  quantity: number
  unitPrice: number
  discount: number | null
  lineTotal: number
  leadTimeDays: number | null
  notesAr: string | null
  notesEn: string | null
}

/** T-028/D-7. Commercial is what the server stores when the field is not sent, so an older client
 * that never learned about envelopes uploads to the gated side rather than the open one. */
export type ProposalDocumentEnvelope = 'Commercial' | 'Technical'

export interface ProposalDocument {
  id: string
  originalFileName: string
  contentType: string
  caption: string | null
  uploadedAt: string
  envelope: ProposalDocumentEnvelope
}

export interface RequirementAnswer {
  id: string
  requirementId: string
  answerAr: string
  answerEn: string
}

/** R-9: §12.5's names. `rfqCode` replaces `rfqReferenceCode`, which the server had been emitting
 * under the name `proposalReferenceCode` - a field whose name said proposal and whose value was the
 * RFQ's code (T-058). */
export interface ProposalTotals {
  currency: string | null
  grandTotal: number
}

export interface Proposal {
  proposalCode: string
  rfqCode: string
  state: ProposalState
  currency: string | null
  paymentTerms: string | null
  incotermCode: string | null
  deliveryTermsAr: string | null
  deliveryTermsEn: string | null
  warranty: string | null
  validityStart: string | null
  validityEnd: string | null
  narrativeAr: string | null
  narrativeEn: string | null
  submittedAt: string | null
  withdrawnAt: string | null
  withdrawReason: string | null
  /** SCR-155: §4.1's "Reason; specific questions". The aggregate has always held it; nothing
   *  projected it until now, so the supplier saw the state and not the question. */
  clarificationReason: string | null
  clarificationRequestedAt: string | null
  /** §4.1's "New revision n+1" - 1 for the original submission. */
  revisionNumber: number
  createdAt: string
  totals: ProposalTotals
  /** §12.5's validityDays, derived from the two dates on the server and read-only - see the DTO's
   * own note on why the request half is not accepted. */
  validityDays: number | null
  items: ProposalItem[]
  documents: ProposalDocument[]
  requirementAnswers: RequirementAnswer[]
}

export interface ItemPricingPayload {
  quantity: number
  unitPrice: number
  discount: number | null
  leadTimeDays: number | null
  notesAr: string | null
  notesEn: string | null
}

export interface CommercialTermsPayload {
  currencyCode: string
  paymentTerms: string | null
  incotermCode: string | null
  deliveryTermsAr: string | null
  deliveryTermsEn: string | null
  warranty: string | null
  validityStart: string | null
  validityEnd: string | null
}

export class ProposalApiError extends Error {
  status: number
  /** EPIC-13/FR-PWF-005: xmin (RowVersion) conflict - see RfqApiError's own doc comment. */
  isConcurrencyConflict: boolean
  /** Read by `errorDetail`: this message is the server's own prose, not a bug's. False when the
   * problem document carried no `title` and no `detail`, because `problemMessage` then falls back to
   * "Request failed: <status>", which is developer text and must not reach a reader. */
  isProblemError: boolean
  constructor(status: number, body: unknown) {
    const b = body as ProblemDetails | null
    super(problemMessage(b, `Request failed: ${status}`))
    this.isProblemError = hasProblemProse(b)
    this.status = status
    this.isConcurrencyConflict = status === 412 && hasCode(b, 'ETAG_MISMATCH')
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new ProposalApiError(res.status, body)
  return body as T
}

/**
 * §12-A/C2: two bases, because the API now has two.
 *  - creation and discovery hang off the RFQ (§3 `/rfqs/{rfqCode}/proposals`, §12.5's create);
 *  - everything acting on an EXISTING proposal is addressed by its own public code
 *    (§3 `/proposals/{proposalCode}/items`, §12.5 `POST /proposals/{proposalCode}/submit`).
 * Callers hold the proposal's own `referenceCode` from the create/get response.
 */
const rfqScoped = (rfqReferenceCode: string) => `/api/v1/rfqs/${rfqReferenceCode}/proposals`
const base = (proposalReferenceCode: string) => `/api/v1/proposals/${proposalReferenceCode}`

export async function startProposal(rfqReferenceCode: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(rfqScoped(rfqReferenceCode), { method: 'POST' }))
}

/**
 * The supplier's own proposal on an RFQ.
 *
 * <p><b>The read files its ETag under the PROPOSAL path as well as its own.</b> Reproduced in the
 * browser before fixing: every guarded write in this workspace answered 428 on its first attempt -
 * Save price, Save terms, Submit, Withdraw, Decline and Revise alike - and the console carried
 * `[concurrency] PATCH /api/v1/proposals/PRP-... was refused for a missing If-Match`.
 *
 * <p>The cause is two paths for one aggregate: this read is `/rfqs/{rfqCode}/proposal` and every
 * write is `/proposals/{proposalCode}/...`, so etags.ts's prefix walk - which climbs a path and never
 * sideways - cannot reach the stored version from a write, and correctly refuses to invent one. The
 * alias is declared HERE rather than taught to the store, because this file is the only thing that
 * knows the two paths name the same resource; the store deducing it would mean guessing at resource
 * boundaries, which its own doc comment explains it cannot do.
 *
 * <p>The proposal code comes out of the body, not the URL, so the alias is only ever filed for a
 * proposal the server actually returned.</p>
 */
export async function getProposal(rfqReferenceCode: string): Promise<Proposal> {
  const res = await apiFetch(rfqScoped(rfqReferenceCode))
  const etag = res.headers.get('ETag')
  const proposal = await parseOrThrow<Proposal>(res)
  if (etag) rememberETag(base(proposal.proposalCode), etag)
  return proposal
}

/**
 * §12.5's one edit route, replacing the five per-field calls this file used to make.
 *
 * <p>RFC 7396 merge patch, with its own media type: a member the object omits is left alone, and an
 * explicit `null` deletes. That distinction is the reason callers build the patch object rather than
 * passing a full DTO - sending `{ warranty: undefined }` and `{ warranty: null }` must mean
 * different things, and only the first is "I am not editing my warranty".</p>
 *
 * <p>`If-Match` is attached by apiFetch from the ETag of the last read (§8.1). A stale one comes back
 * as 412 and the editor reconciles, per SCR-151.</p>
 */
export async function patchProposal(proposalReferenceCode: string, patch: ProposalPatch): Promise<Proposal> {
  return parseOrThrow(await apiFetch(base(proposalReferenceCode), {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/merge-patch+json' },
    body: JSON.stringify(patch),
  }))
}

export interface ProposalPatch {
  items?: ItemPatch[]
  commercialTerms?: Partial<CommercialTermsPayload>
  technicalResponse?: {
    narrativeAr?: string | null
    narrativeEn?: string | null
    answers?: { requirementId: string; answerAr: string; answerEn: string }[]
  }
}

export interface ItemPatch {
  rfqItemId: string
  quantity: number
  unitPrice: number
  discount?: number | null
  leadTimeDays?: number | null
  notesAr?: string | null
  notesEn?: string | null
}

export async function addProposalDocument(
  proposalReferenceCode: string,
  file: File,
  caption?: string,
  envelope?: ProposalDocumentEnvelope,
): Promise<Proposal> {
  const form = new FormData()
  form.append('file', file)
  if (caption) form.append('caption', caption)
  if (envelope) form.append('envelope', envelope)
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/documents`, { method: 'POST', body: form }))
}

export async function removeProposalDocument(proposalReferenceCode: string, documentId: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/documents/${documentId}`, { method: 'DELETE' }))
}

export async function submitProposal(proposalReferenceCode: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/submit`, { method: 'POST' }))
}

/** T-064/§4.1: AwardOffered -> Declined. A reason is required - a declined award nobody can explain
 * is the one an audit asks about first. */
export async function declineAwardOffer(proposalReferenceCode: string, reason: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/decline`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  }))
}

/** SCR-155/§4.1: ClarificationRequested -> Revised. `POST /proposals/{code}/revise` has existed
 * since T-051 and nothing called it, and D-43 meant the persona its own comment names could not have
 * called it either - `proposal.revise` was granted to `system_admin` alone. The grant moved to
 * supplier_admin in this batch; this is the surface.
 *
 * No body, and it does NOT reopen the proposal for editing. Checked in the aggregate rather than
 * assumed: `Proposal.Patch` and `Proposal.Submit` both refuse any state but Draft, and
 * `RecordRevision`'s own doc records why - §4.1's "only permitted fields changed" is BRULE-050, a
 * configurable policy whose default is undecided, and §4.1's "snapshot" needs a revision store
 * nothing here has. So the transition is the supplier's response and the field-level edit is not
 * claimed by either half. The screen says that plainly instead of showing an edit form that would
 * 409. If-Match travels automatically from this proposal's read - see api/etags.ts. */
export async function reviseProposal(proposalReferenceCode: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/revise`, { method: 'POST' }))
}

export async function withdrawProposal(proposalReferenceCode: string, reason: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/withdraw`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  }))
}

/** B-1/SCR-433: the buyer asks a bidder to clarify. `POST /proposals/{code}/request-clarification` has
 * existed since T-051, is permissioned on `rfq.clarify`, and NOTHING called it - the same defect shape as
 * T-067: the rule permits the action and no surface reaches it. */
export async function requestProposalClarification(proposalReferenceCode: string, reason: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/request-clarification`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  }))
}

/**
 * SCR-150: the calling supplier's own proposals across every RFQ.
 *
 * <p>Scoped server-side by the caller's own supplier — there is no id to pass, which is the point.
 * Drafts are included here and excluded from the buyer's view of the same rows (T-082): the two
 * lists answer different questions about the same table.</p>
 */
export interface MyProposalListItem {
  proposalCode: string
  rfqCode: string
  rfqTitleAr: string
  rfqTitleEn: string
  state: string
  submittedAt: string | null
  submissionDeadline: string | null
  currencyCode: string | null
  totalValue: number | null
  itemCount: number
}

export async function listMyProposals(): Promise<MyProposalListItem[]> {
  const res = await apiFetch('/api/v1/proposals')
  if (!res.ok) throw new ProposalApiError(res.status, null)
  return (await res.json()) as MyProposalListItem[]
}
