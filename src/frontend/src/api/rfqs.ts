import { hasCode, problemMessage, type ProblemDetails } from './problem'
import { apiFetch } from './auth'
import type { ListEnvelope } from './listEnvelope'

/** The buyer list row - the projected shape, not the detail `Rfq`. */
export interface RfqListItem {
  referenceCode: string
  titleAr: string
  titleEn: string
  state: RfqState
  createdAt: string
}

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

export type InvitationStatus = 'Invited' | 'Viewed' | 'Responding' | 'Submitted' | 'Declined'

export interface Invitation {
  id: string
  supplierId: string
  supplierDisplayNameAr: string
  supplierDisplayNameEn: string
  status: InvitationStatus
  invitedAt: string
  viewedAt: string | null
  respondedAt: string | null
  declineReason: string | null
}

export interface InvitationCandidate {
  supplierId: string
  displayNameAr: string
  displayNameEn: string
  matchCount: number
}

export type ClarificationVisibility = 'PrivateToAsker' | 'PublishedToAll'

/** Buyer-facing shape - always carries the real asker (audit). */
export interface Clarification {
  id: string
  askedBySupplierId: string
  askedBySupplierNameAr: string
  askedBySupplierNameEn: string
  question: string
  answer: string | null
  visibility: ClarificationVisibility
  askedAt: string
  answeredAt: string | null
}

export interface Addendum {
  id: string
  titleAr: string
  titleEn: string
  descriptionAr: string
  descriptionEn: string
  issuedAt: string
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
  invitations: Invitation[]
  clarifications: Clarification[]
  addenda: Addendum[]
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
  /** EPIC-13/FR-PWF-005: xmin (RowVersion) conflict - the API's global concurrency-exception
   * handler (Program.cs) returns this same { error: "concurrency_conflict" } 409 shape
   * SupplierApiError's own isConcurrencyConflict already established, so every caller checks the
   * one flag rather than string-matching a message. */
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
  if (!res.ok) throw new RfqApiError(res.status, body)
  return body as T
}

export async function listRfqs(cursor?: string | null): Promise<ListEnvelope<RfqListItem>> {
  const qs = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''
  return parseOrThrow(await apiFetch(`/api/v1/rfqs${qs}`))
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

export async function inviteSupplier(referenceCode: string, supplierId: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/invitations`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ supplierId }),
  }))
}

export async function suggestInvitationCandidates(referenceCode: string): Promise<InvitationCandidate[]> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/invitations/candidates`))
}

export async function answerClarification(referenceCode: string, clarificationId: string, answer: string, publish: boolean): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/clarifications/${clarificationId}/answer`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ answer, publish }),
  }))
}

export async function publishClarification(referenceCode: string, clarificationId: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/clarifications/${clarificationId}/publish`, { method: 'POST' }))
}

export interface AddendumPayload {
  titleAr: string
  titleEn: string
  descriptionAr: string
  descriptionEn: string
}

export async function issueAddendum(referenceCode: string, payload: AddendumPayload): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/addenda`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}
