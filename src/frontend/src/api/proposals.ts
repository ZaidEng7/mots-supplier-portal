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
  constructor(status: number, body: unknown) {
    const b = body as { error?: string; message?: string } | null
    super(b?.message ?? b?.error ?? `Request failed: ${status}`)
    this.status = status
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new ProposalApiError(res.status, body)
  return body as T
}

const base = (referenceCode: string) => `/api/v1/suppliers/me/rfqs/${referenceCode}/proposal`

export async function startProposal(referenceCode: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(base(referenceCode), { method: 'POST' }))
}

export async function getProposal(referenceCode: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(base(referenceCode)))
}

export async function setItemPricing(referenceCode: string, rfqItemId: string, payload: ItemPricingPayload): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(referenceCode)}/items/${rfqItemId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function removeItemPricing(referenceCode: string, rfqItemId: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(referenceCode)}/items/${rfqItemId}`, { method: 'DELETE' }))
}

export async function setCommercialTerms(referenceCode: string, payload: CommercialTermsPayload): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(referenceCode)}/terms`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function setNarrative(referenceCode: string, narrativeAr: string | null, narrativeEn: string | null): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(referenceCode)}/narrative`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ narrativeAr, narrativeEn }),
  }))
}

export async function answerRequirement(referenceCode: string, requirementId: string, answerAr: string, answerEn: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(referenceCode)}/requirements/${requirementId}/answer`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ answerAr, answerEn }),
  }))
}

export async function addProposalDocument(referenceCode: string, file: File, caption?: string): Promise<Proposal> {
  const form = new FormData()
  form.append('file', file)
  if (caption) form.append('caption', caption)
  return parseOrThrow(await apiFetch(`${base(referenceCode)}/documents`, { method: 'POST', body: form }))
}

export async function removeProposalDocument(referenceCode: string, documentId: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(referenceCode)}/documents/${documentId}`, { method: 'DELETE' }))
}

export async function submitProposal(referenceCode: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(referenceCode)}/submit`, { method: 'POST' }))
}

export async function withdrawProposal(referenceCode: string, reason: string): Promise<Proposal> {
  return parseOrThrow(await apiFetch(`${base(referenceCode)}/withdraw`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  }))
}
