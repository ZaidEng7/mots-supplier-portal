// The buyer's side of a tender: authoring it, its items, requirements and attachments, the review and publish
// chain, ownership, invitations, clarifications, addenda and the deadline.
//
// RfqListItem is the buyer LIST row - the projected shape, not the detail Rfq. Its owner is A-7's: null means
// unassigned, a row anyone holding the permission may claim, which is where every RFQ created before ownership
// existed lives. The owner's name is null when unowned and also null when the id points at a user row that is
// gone; the two are told apart by whether ownerUserId is set.
//
// A requirement carries A-2's expected envelope: which envelope a document answering it belongs in. It is
// advisory - it tells the supplier what the buyer expects and does NOT override the tag on the file, because what
// a file contains is known by whoever attached it.
//
// Clarification here is the BUYER-facing shape and always carries the real asker, for the audit. The supplier's
// shape, which carries no asker at all, is in api/supplierRfqs.ts.
//
// The detail Rfq carries A-7's two people: the owning officer, and the manager the CURRENT review pass is waiting
// on - the latter null when the pass named nobody, which is the normal case while approval routing is undecided.
//
// RFQAPIERROR reads both refusal shapes: the backend returns { error, message } for domain-invariant refusals
// (RfqMutationResult.InvalidState) and a bare { error } for reference-data validation - the same shape and
// reasoning as EvaluationTemplateApiError. Its concurrency flag is EPIC-13 and FR-PWF-005's xmin RowVersion
// conflict; §8.1 under T3-34 moved that from the API's own { error: "concurrency_conflict" } 409 to the documented
// 412 ETAG_MISMATCH, because a lost update is a failed precondition rather than one of §7.1's three conflicts.
// Every caller still checks the one flag rather than string-matching a message.
//
// The owner FILTER mirrors the review queue's assignedTo: "me" resolves server-side, so this client never needs to
// know the caller's own user id (A-7).
//
// getRfqAttachmentDownloadUrl is SCR-142 and SCR-414's. The route is rfq.read, which BOTH supplier roles hold
// (§12-A/C1), so an invited supplier downloads the tender documents through the same path a buyer does -
// row-scoped by the handler rather than by which client is asking.
//
// submitRfqForReview takes an optional approver, because there is no routing rule to fall back on - see
// Rfq.SubmitForReview - and omitting the body entirely keeps the pool notified, as before (A-7).
//
// listRfqAssignees answers who this RFQ may be handed to, in the two senses it can be: an owner and an approver.
// Id and name only; nothing else about a colleague is needed to choose between them. reassignRfq hands the RFQ to
// another officer, and its reason is mandatory - the audit row is the point (A-7).
//
// changeSubmissionDeadline serves both directions, because T-018 and BRULE-035 make extension the officer's and
// shortening the manager's: the server decides which from the direction, so one function covers both and a 403
// means "not your direction". Its reason is mandatory (A-6), because BRULE-035 leaves an extension uncapped, so
// the reason is what makes it defensible - and the supplier reads it on the RFQ, where the deadline is.
//
// answerClarification publishes to every invitee with the asker anonymised (A-4), so there is no publish argument
// to pass. publishClarification is the legacy-row path.

import { ProblemError, hasCode, type ProblemDetails } from './problem'
import { apiFetch } from './auth'
import type { ListEnvelope } from './listEnvelope'

export interface RfqListItem {
  referenceCode: string
  titleAr: string
  titleEn: string
  state: RfqState
  createdAt: string
  ownerUserId: string | null
  ownerName: string | null
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
  expectedEnvelope: 'Technical' | 'Commercial' | null
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
  ownerUserId: string | null
  ownerName: string | null
  assignedApproverUserId: string | null
  assignedApproverName: string | null
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

export class RfqApiError extends ProblemError {
  isConcurrencyConflict: boolean
  constructor(status: number, body: unknown) {
    super(status, body)
    this.isConcurrencyConflict = status === 412 && hasCode(body as ProblemDetails | null, 'ETAG_MISMATCH')
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new RfqApiError(res.status, body)
  return body as T
}

export type RfqOwnerFilter = 'me' | 'unassigned'

export async function listRfqs(cursor?: string | null, owner?: RfqOwnerFilter): Promise<ListEnvelope<RfqListItem>> {
  const params = new URLSearchParams()
  if (cursor) params.set('cursor', cursor)
  if (owner) params.set('owner', owner)
  const qs = params.size > 0 ? `?${params}` : ''
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

export async function updateRfqItem(referenceCode: string, itemId: string, payload: RfqItemPayload): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/items/${itemId}`, {
    method: 'PUT',
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

export async function updateRequirement(referenceCode: string, requirementId: string, payload: RequirementPayload): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/requirements/${requirementId}`, {
    method: 'PUT',
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

export async function getRfqAttachmentDownloadUrl(referenceCode: string, attachmentId: string): Promise<string> {
  const res = await apiFetch(`/api/v1/rfqs/${referenceCode}/attachments/${attachmentId}/download-url`)
  const body = await parseOrThrow<{ url: string }>(res)
  return body.url
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

export async function submitRfqForReview(referenceCode: string, assignedApproverUserId?: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/submit-review`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ assignedApproverUserId: assignedApproverUserId ?? null }),
  }))
}

export interface RfqAssignee {
  userId: string
  fullName: string
}

export interface RfqAssignees {
  owners: RfqAssignee[]
  approvers: RfqAssignee[]
}

export async function listRfqAssignees(referenceCode: string): Promise<RfqAssignees> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/assignees`))
}

export async function reassignRfq(referenceCode: string, newOwnerUserId: string, reason: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/reassign`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ newOwnerUserId, reason }),
  }))
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

export async function changeSubmissionDeadline(referenceCode: string, submissionDeadline: string, reason: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/deadline`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ submissionDeadline, reason }),
  }))
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

export async function answerClarification(referenceCode: string, clarificationId: string, answer: string): Promise<Rfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/clarifications/${clarificationId}/answer`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ answer }),
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
