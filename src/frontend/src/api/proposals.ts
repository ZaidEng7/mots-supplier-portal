import { hasCode, problemMessage, type ProblemDetails } from './problem'
import { apiFetch } from './auth'

export type ProposalState =
  | 'Draft' | 'Submitted' | 'Withdrawn' | 'UnderReview' | 'ClarificationRequested' | 'Revised'
  | 'Shortlisted' | 'NotSelected' | 'AwardOffered' | 'Awarded' | 'Declined'

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

export interface ProposalDocument {
  id: string
  originalFileName: string
  contentType: string
  caption: string | null
  uploadedAt: string
}

export interface RequirementAnswer {
  id: string
  requirementId: string
  answerAr: string
  answerEn: string
}

export interface Proposal {
  referenceCode: string
  rfqReferenceCode: string
  state: ProposalState
  currencyCode: string | null
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
    this.isConcurrencyConflict = status === 409 && hasCode(b, 'CONCURRENCY_CONFLICT')
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

export async function setItemPricing(proposalReferenceCode: string, rfqItemId: string, payload: ItemPricingPayload): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/items/${rfqItemId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function removeItemPricing(proposalReferenceCode: string, rfqItemId: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/items/${rfqItemId}`, { method: 'DELETE' }))
}

export async function setCommercialTerms(proposalReferenceCode: string, payload: CommercialTermsPayload): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/terms`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function setNarrative(proposalReferenceCode: string, narrativeAr: string | null, narrativeEn: string | null): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/narrative`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ narrativeAr, narrativeEn }),
  }))
}

export async function answerRequirement(proposalReferenceCode: string, requirementId: string, answerAr: string, answerEn: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/requirements/${requirementId}/answer`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ answerAr, answerEn }),
  }))
}

export async function addProposalDocument(proposalReferenceCode: string, file: File, caption?: string): Promise<Proposal> {
  const form = new FormData()
  form.append('file', file)
  if (caption) form.append('caption', caption)
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/documents`, { method: 'POST', body: form }))
}

export async function removeProposalDocument(proposalReferenceCode: string, documentId: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/documents/${documentId}`, { method: 'DELETE' }))
}

export async function submitProposal(proposalReferenceCode: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/submit`, { method: 'POST' }))
}

export async function withdrawProposal(proposalReferenceCode: string, reason: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(proposalReferenceCode)}/withdraw`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  }))
}
