import { apiFetch } from './auth'

export type RfqState =
  | 'Draft' | 'InternalReview' | 'Approved' | 'Published' | 'SubmissionOpen' | 'SubmissionClosed'
  | 'UnderEvaluation' | 'Clarification' | 'Shortlisting' | 'Recommendation' | 'AwardApproval'
  | 'Awarded' | 'Completed' | 'Cancelled'

export interface RfqItem {
  id: string
  lineNo: number
  titleAr: string
  titleEn: string
  specificationAr: string | null
  specificationEn: string | null
  categoryCode: string
  quantity: number
  unitOfMeasureCode: string
  isUnitPrice: boolean
  isOptional: boolean
}

export interface Requirement {
  id: string
  textAr: string
  textEn: string
  isMandatory: boolean
  documentTypeCode: string | null
}

export interface RfqAttachment {
  id: string
  originalFileName: string
  contentType: string
  caption: string | null
  uploadedAt: string
}

export interface RfqApproval {
  stepNo: number
  approverUserId: string | null
  decision: 'Approved' | 'Rejected' | null
  comment: string | null
  decidedAt: string | null
}

export interface Rfq {
  referenceCode: string
  organizationId: string
  titleAr: string
  titleEn: string
  descriptionAr: string | null
  descriptionEn: string | null
  currencyCode: string
  state: RfqState
  publishAt: string | null
  submissionOpensAt: string | null
  submissionClosesAt: string | null
  clarificationDeadlineAt: string | null
  evaluationTargetDate: string | null
  evaluationTemplateId: string | null
  evaluationTemplateVersion: number | null
  cancelReason: string | null
  items: RfqItem[]
  requirements: Requirement[]
  attachments: RfqAttachment[]
  approvals: RfqApproval[]
}

export interface RfqBasicsPayload {
  titleAr: string
  titleEn: string
  descriptionAr: string | null
  descriptionEn: string | null
  currencyCode: string
  publishAt: string | null
  submissionOpensAt: string | null
  submissionClosesAt: string | null
  clarificationDeadlineAt: string | null
  evaluationTargetDate: string | null
}

export interface RfqItemPayload {
  titleAr: string
  titleEn: string
  specificationAr: string | null
  specificationEn: string | null
  categoryCode: string
  quantity: number
  unitOfMeasureCode: string
  isUnitPrice: boolean
  isOptional: boolean
}

export interface RequirementPayload {
  textAr: string
  textEn: string
  isMandatory: boolean
  documentTypeCode: string | null
}

/** Backend returns { error, message } for domain-invariant refusals (RfqMutationResult.InvalidState)
 * and a bare { error } for reference-data validation - same shape/reasoning as
 * EvaluationTemplateApiError. */
export class RfqApiError extends Error {
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
  if (!res.ok) throw new RfqApiError(res.status, body)
  return body as T
}

export async function listRfqs(): Promise<Rfq[]> {
  return parseOrThrow(await apiFetch('/api/v1/rfqs'))
}

export async function getRfq(referenceCode: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}`))
}

export async function createRfq(payload: RfqBasicsPayload): Promise<Rfq> {
  return parseOrThrow(await apiFetch('/api/v1/rfqs', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function updateRfqBasics(referenceCode: string, payload: RfqBasicsPayload): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function addRfqItem(referenceCode: string, payload: RfqItemPayload): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/items`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function removeRfqItem(referenceCode: string, itemId: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/items/${itemId}`, { method: 'DELETE' }))
}

export async function addRequirement(referenceCode: string, payload: RequirementPayload): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/requirements`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function removeRequirement(referenceCode: string, requirementId: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/requirements/${requirementId}`, { method: 'DELETE' }))
}

export async function addRfqAttachment(referenceCode: string, file: File, caption?: string): Promise<Rfq> {
  const form = new FormData()
  form.append('file', file)
  if (caption) form.append('caption', caption)
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/attachments`, { method: 'POST', body: form }))
}

export async function removeRfqAttachment(referenceCode: string, attachmentId: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/attachments/${attachmentId}`, { method: 'DELETE' }))
}

export async function bindEvaluationTemplate(referenceCode: string, evaluationTemplateId: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/evaluation-template`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ evaluationTemplateId }),
  }))
}

export async function submitRfqForReview(referenceCode: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/submit-review`, { method: 'POST' }))
}

export async function returnRfqForEdits(referenceCode: string, comments: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/return`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ comments }),
  }))
}

export async function approveRfq(referenceCode: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/approve`, { method: 'POST' }))
}

export async function publishRfq(referenceCode: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/publish`, { method: 'POST' }))
}

export async function closeRfqSubmission(referenceCode: string, reason: string | null): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/close`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  }))
}

export async function cancelRfq(referenceCode: string, reason: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/cancel`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  }))
}
