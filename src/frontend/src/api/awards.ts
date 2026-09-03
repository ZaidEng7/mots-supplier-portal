import { hasCode, problemMessage, type ProblemDetails } from './problem'
import { apiFetch } from './auth'

export type AwardState = 'Recommended' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Awarded'
export type ErpSyncStatus = 'NotRequested' | 'Requested' | 'Synced' | 'Failed'
export type ApprovalDecision = 'Approved' | 'Rejected'

export interface AwardApproval {
  stepNo: number
  approverUserId: string | null
  decision: ApprovalDecision | null
  comment: string | null
  decidedAt: string | null
}

/** ComparisonSnapshotJson (FEAT-14.7) is only ever non-null once state is 'Awarded' - the frozen
 * award file, never re-queried live. */
export interface Award {
  id: string
  rfqReferenceCode: string
  state: AwardState
  winningProposalId: string
  justificationAr: string
  justificationEn: string
  recommendedByUserId: string
  recommendedAt: string
  recommendationRevision: number
  approvals: AwardApproval[]
  awardedAt: string | null
  comparisonSnapshotJson: string | null
  erpSyncStatus: ErpSyncStatus
  externalPurchaseOrderRef: string | null
  erpSyncedAt: string | null
  erpRetryCount: number
}

export class AwardApiError extends Error {
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
  if (!res.ok) throw new AwardApiError(res.status, body)
  return body as T
}

export async function getAward(rfqReferenceCode: string): Promise<Award | null> {
  const res = await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/award`)
  if (res.status === 404) return null
  return parseOrThrow(res)
}

export async function recommendAward(
  rfqReferenceCode: string,
  payload: { winningProposalId: string; justificationAr: string; justificationEn: string },
): Promise<Award> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/award/recommend`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function routeAwardForApproval(rfqReferenceCode: string): Promise<Award> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/award/route-for-approval`, { method: 'POST' }))
}

export async function approveAward(rfqReferenceCode: string): Promise<Award> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/award/approve`, { method: 'POST' }))
}

export async function rejectAward(rfqReferenceCode: string, reason: string): Promise<Award> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/award/reject`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  }))
}

export async function executeAward(rfqReferenceCode: string): Promise<Award> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/award/execute`, { method: 'POST' }))
}

export async function retryAwardErpSync(rfqReferenceCode: string): Promise<Award> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/award/retry-erp-sync`, { method: 'POST' }))
}
