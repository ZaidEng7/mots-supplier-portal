import { problemMessage, type ProblemDetails } from './problem'
import { apiFetch } from './auth'
import type { ListEnvelope } from './listEnvelope'
import type { DocumentTypeStatus } from './documents'
import type { SupplierProfile } from './supplier'

export interface ReviewQueueItem {
  referenceCode: string
  displayNameAr: string
  displayNameEn: string
  onboardingState: string
  enteredQueueAt: string
  /** A-5: the review TARGET, in working days from `enteredQueueAt` per the configured SLA. A target,
   * never a breach - BUSINESS-PROCESSES.md §5 runs a timer and names no number. */
  reviewTargetAt: string | null
  assignedReviewerId: string | null
  assignedReviewerName: string | null
}


export interface ReviewAnnotation {
  id: string
  requestedAt: string
  reason: string
  flaggedProfileFields: string[]
  flaggedDocumentTypeCodes: string[]
  resolvedAt: string | null
}

export interface ReviewerSupplierView {
  supplier: SupplierProfile
  documents: DocumentTypeStatus[]
  annotationHistory: ReviewAnnotation[]
}

export class ReviewApiError extends Error {
  status: number
  constructor(status: number, body: unknown) {
    const b = body as ProblemDetails | null
    super(problemMessage(b, `Request failed: ${status}`))
    this.status = status
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new ReviewApiError(res.status, body)
  return body as T
}

export async function getOwnActiveAnnotation(): Promise<ReviewAnnotation | null> {
  const res = await apiFetch('/api/v1/suppliers/me/active-annotation')
  return parseOrThrow(res)
}

export interface ReviewQueueFilters {
  state?: string | null
  assignedTo?: string | null
}

export async function listReviewQueue(cursor?: string | null, filters?: ReviewQueueFilters): Promise<ListEnvelope<ReviewQueueItem>> {
  const params = new URLSearchParams()
  if (cursor) params.set('cursor', cursor)
  if (filters?.state) params.set('state', filters.state)
  if (filters?.assignedTo) params.set('assignedTo', filters.assignedTo)
  const qs = params.toString() ? `?${params.toString()}` : ''
  const res = await apiFetch(`/api/v1/review/queue${qs}`)
  return parseOrThrow(res)
}

export async function claimReviewItem(referenceCode: string): Promise<ReviewQueueItem> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/claim`, { method: 'POST' })
  return parseOrThrow(res)
}

export async function unassignReviewItem(referenceCode: string): Promise<ReviewQueueItem> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/unassign`, { method: 'POST' })
  return parseOrThrow(res)
}

export async function getReviewerSupplierView(referenceCode: string): Promise<ReviewerSupplierView> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}`)
  return parseOrThrow(res)
}

export async function pickUpApplication(referenceCode: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/pickup`, { method: 'POST' })
  return parseOrThrow(res)
}

export async function approveApplication(referenceCode: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/approve`, { method: 'POST' })
  return parseOrThrow(res)
}

export async function rejectApplication(referenceCode: string, reason: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/reject`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  })
  return parseOrThrow(res)
}

export async function requestApplicationInfo(
  referenceCode: string,
  reason: string,
  flaggedProfileFields: string[],
  flaggedDocumentTypeCodes: string[],
): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/request-info`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason, flaggedProfileFields, flaggedDocumentTypeCodes }),
  })
  return parseOrThrow(res)
}

/**
 * MSP-63 / FR-ONB-009: post-approval lifecycle. Suspend and reactivate are reversible;
 * deactivate is terminal, and the API refuses it unless the supplier is already suspended.
 *
 * The server returns 409 with the domain's own message for an illegal transition
 * (NFR-CMP-003/BRULE-097). The UI hides actions that do not apply, but hiding is a
 * convenience - the rule is enforced server-side and the message is surfaced, not swallowed.
 */
export type SupplierLifecycleAction = 'suspend' | 'reactivate' | 'deactivate'

export async function changeSupplierLifecycle(
  referenceCode: string,
  action: SupplierLifecycleAction,
  reason: string,
): Promise<{ lifecycleState: string }> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/${action}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  })
  return parseOrThrow(res)
}
