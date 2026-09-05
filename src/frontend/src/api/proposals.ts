import { hasCode, problemMessage, type ProblemDetails } from './problem'
import { apiFetch } from './auth'

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
  constructor(status: number, body: unknown) {
    const b = body as ProblemDetails | null
    super(problemMessage(b, `Request failed: ${status}`))
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

export async function getProposal(rfqReferenceCode: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(rfqScoped(rfqReferenceCode)))
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
