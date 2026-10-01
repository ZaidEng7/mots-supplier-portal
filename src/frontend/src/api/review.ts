// The onboarding review queue, a reviewer's read of one application, the four decisions, and the post-approval
// lifecycle.
//
// A queue row carries A-5's review TARGET, in working days from enteredQueueAt per the configured SLA. It is a
// target and never a breach: BUSINESS-PROCESSES.md §5 runs a timer and names no number.
//
// getReviewerSupplierView files its ETag under the SUPPLIER's path as well as its own. The reviewer READS the
// application at /review/{code} and writes its documents at /suppliers/{code}/documents/{doc}/approve. Both
// address the same supplier aggregate and both document transitions declare RequireIfMatch, but the ETag store
// walks a path UPWARDS and never sideways, so a version filed under /review/... is unreachable from a write under
// /suppliers/... Every document approval and rejection answered 428 for that reason, on the one screen where a
// reviewer decides on documents. It is filed here rather than in the transport because this function is the only
// thing that knows the two paths name one resource - the same reasoning as getProposal (D-47) and profileFrom
// (D-60).
//
// THE LIFECYCLE is MSP-63 and FR-ONB-009's post-approval half: suspend and reactivate are reversible, and
// deactivate is terminal, which the API refuses unless the supplier is already suspended. The server returns 409
// with the domain's own message for an illegal transition (NFR-CMP-003 and BRULE-097). The UI hides actions that
// do not apply, but hiding is a convenience - the rule is enforced server-side, and the message is surfaced
// rather than swallowed.
//
// THE PUSH TO THE ERP is read on the same view. erpPushStatus is how far creating in the ERP a supplier that registered
// here has got, as the server's SupplierErpPushStatus names it, and erpPushLastError what the last failed attempt said,
// which stays through a retry. retryErpPush starts a failed push again; the server holds it to
// admin.integrations.manage, not to a reviewer's permission, and answers 409 with the domain's sentence for a push
// that has not failed or a supplier out of service.

import { ProblemError } from './problem'
import { rememberETag } from './etags'
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

export type ErpPushStatus = 'NotRequested' | 'Requested' | 'Linked' | 'Created' | 'Failed'

export interface ReviewerErpSync {
  externalId: string | null
  syncStatus: string
  lastSyncedAt: string | null
  liftsWhenErpApproves: boolean
  erpPushStatus: ErpPushStatus
  erpPushLastError: string | null
}

export interface ReviewerSupplierView {
  supplier: SupplierProfile
  erpSync?: ReviewerErpSync
  documents: DocumentTypeStatus[]
  annotationHistory: ReviewAnnotation[]
}

export class ReviewApiError extends ProblemError {
  constructor(status: number, body: unknown) {
    super(status, body)
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
  rememberETag(`/api/v1/suppliers/${referenceCode}`, res.headers.get('ETag'))
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

export async function retryErpPush(referenceCode: string): Promise<ReviewerErpSync> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/retry-erp-push`, { method: 'POST' })
  return parseOrThrow(res)
}
