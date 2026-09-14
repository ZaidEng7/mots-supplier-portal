// The supplier's own proposal: starting one, editing it, submitting, withdrawing, revising, declining an award
// offer - plus the buyer's request for a clarification, and the supplier's list across every RFQ.
//
// TWO TERMINAL STATES are worth distinguishing (A-9): Lapsed means the window closed on a draft, Cancelled means
// the RFQ was withdrawn beneath it. Two states rather than one, because a supplier reading their list has to be
// able to tell "you ran out of time" from "the tender was withdrawn".
//
// ProposalItem is FEAT-09.1 and FR-PRP-002's FINANCIAL content, under OQ-009's two-envelope rule: only ever
// present in a response to the owning supplier's own request - see ProposalDtoMapper.ToDto for the seal mechanism
// itself. A document's envelope defaults to Commercial, which is what the server stores when the field is not
// sent, so an older client that never learned about envelopes uploads to the gated side rather than the open one
// (T-028 and D-7).
//
// R-9 settled the names: rfqCode replaces rfqReferenceCode, which the server had been emitting under the name
// proposalReferenceCode - a field whose name said proposal and whose value was the RFQ's code (T-058). The
// clarification question is SCR-155's, §4.1's "Reason; specific questions": the aggregate has always held it and
// nothing projected it until now, so the supplier saw the state and not the question. The revision number is
// §4.1's "New revision n+1", and is 1 for the original submission. validityDays is §12.5's, derived from the two
// dates on the server and read-only - see the DTO for why the request half is not accepted.
//
// ProposalApiError carries the xmin RowVersion conflict flag of EPIC-13 and FR-PWF-005 - see RfqApiError.
//
// TWO BASES, because §12-A/C2 gave the API two: creation and discovery hang off the RFQ (§3's
// /rfqs/{rfqCode}/proposals, and §12.5's create), while everything acting on an EXISTING proposal is addressed by
// its own public code (§3's /proposals/{proposalCode}/items, §12.5's POST /proposals/{proposalCode}/submit).
// Callers hold the proposal's own referenceCode from the create or get response.
//
// getProposal files its ETag under the PROPOSAL path as well as its own. Reproduced in the browser before fixing:
// every guarded write in this workspace answered 428 on its first attempt - Save price, Save terms, Submit,
// Withdraw, Decline and Revise alike - with the console carrying "[concurrency] PATCH /api/v1/proposals/PRP-... was
// refused for a missing If-Match". The cause is two paths for one aggregate: this read is /rfqs/{rfqCode}/proposal
// and every write is /proposals/{proposalCode}/..., so etags.ts's prefix walk, which climbs a path and never goes
// sideways, cannot reach the stored version from a write and correctly refuses to invent one. The alias is declared
// HERE rather than taught to the store, because this file is the only thing that knows the two paths name the same
// resource; the store deducing it would mean guessing at resource boundaries, which it cannot do. The proposal
// code comes out of the body rather than the URL, so the alias is only ever filed for a proposal the server
// actually returned.
//
// patchProposal is §12.5's one edit route, replacing the five per-field calls this file used to make. It is an RFC
// 7396 merge patch with its own media type: a member the object omits is left alone, and an explicit null deletes.
// That distinction is why callers build the patch object rather than passing a full DTO - sending
// { warranty: undefined } and { warranty: null } must mean different things, and only the first is "I am not
// editing my warranty". If-Match is attached by apiFetch from the ETag of the last read (§8.1), and a stale one
// comes back as 412 for the editor to reconcile, per SCR-151.
//
// declineAwardOffer is T-064 and §4.1's AwardOffered to Declined. A reason is required, because a declined award
// nobody can explain is the one an audit asks about first.
//
// reviseProposal is SCR-155 and §4.1's ClarificationRequested to Revised. POST /proposals/{code}/revise has
// existed since T-051 and nothing called it, and D-43 meant the persona its own description names could not have
// called it either - proposal.revise was granted to system_admin alone. The grant moved to supplier_admin in this
// batch, and this is the surface. It sends no body, and it does NOT reopen the proposal for editing, which was
// checked in the aggregate rather than assumed: Proposal.Patch and Proposal.Submit both refuse any state but
// Draft, and RecordRevision records why - §4.1's "only permitted fields changed" is BRULE-050, a configurable
// policy whose default is undecided, and §4.1's "snapshot" needs a revision store nothing here has. So the
// transition is the supplier's response and the field-level edit is not claimed by either half; the screen says
// that plainly instead of showing an edit form that would 409. If-Match travels automatically from this proposal's
// read - see api/etags.ts.
//
// requestProposalClarification is B-1 and SCR-433's: the buyer asks a bidder to clarify. POST
// /proposals/{code}/request-clarification has existed since T-051, is permissioned on rfq.clarify, and NOTHING
// called it - the same defect shape as T-067, where the rule permits the action and no surface reaches it.
//
// listMyProposals is SCR-150: the calling supplier's own proposals across every RFQ, scoped server-side by the
// caller's own supplier, so there is no id to pass - which is the point. Drafts are included here and excluded
// from the buyer's view of the same rows (T-082): the two lists answer different questions about the same table.

import { ProblemError, hasCode, type ProblemDetails } from './problem'
import { apiFetch } from './auth'
import { rememberETag } from './etags'

export type ProposalState =
  | 'Draft' | 'Submitted' | 'Withdrawn' | 'UnderReview' | 'ClarificationRequested' | 'Revised'
  | 'Shortlisted' | 'NotSelected' | 'AwardOffered' | 'Awarded' | 'Declined'
  | 'Lapsed' | 'Cancelled'

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
  clarificationReason: string | null
  clarificationRequestedAt: string | null
  revisionNumber: number
  createdAt: string
  totals: ProposalTotals
  validityDays: number | null
  items: ProposalItem[]
  documents: ProposalDocument[]
  requirementAnswers: RequirementAnswer[]
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

export class ProposalApiError extends ProblemError {
  isConcurrencyConflict: boolean
  constructor(status: number, body: unknown) {
    super(status, body)
    this.isConcurrencyConflict = status === 412 && hasCode(body as ProblemDetails | null, 'ETAG_MISMATCH')
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new ProposalApiError(res.status, body)
  return body as T
}

const rfqScoped = (rfqReferenceCode: string) => `/api/v1/rfqs/${rfqReferenceCode}/proposals`
const base = (proposalReferenceCode: string) => `/api/v1/proposals/${proposalReferenceCode}`

export async function startProposal(rfqReferenceCode: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(rfqScoped(rfqReferenceCode), { method: 'POST' }))
}

export async function getProposal(rfqReferenceCode: string): Promise<Proposal> {
  const res = await apiFetch(rfqScoped(rfqReferenceCode))
  const etag = res.headers.get('ETag')
  const proposal = await parseOrThrow<Proposal>(res)
  if (etag) rememberETag(base(proposal.proposalCode), etag)
  return proposal
}

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

export async function declineAwardOffer(proposalReferenceCode: string, reason: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/decline`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  }))
}

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

export async function requestProposalClarification(proposalReferenceCode: string, reason: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/request-clarification`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  }))
}

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
