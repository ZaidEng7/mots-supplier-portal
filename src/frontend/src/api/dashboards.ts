// The three buyer-side dashboards: SCR-400's procurement dashboard, the approval queues, and the review
// dashboard.
//
// The KPI row is SCREEN-SPECIFICATIONS.md §10's. The approvals card is §10's "Manager also gets an Approvals
// card", decided server-side from the permission so the affordance and the API agree about who may approve. Each
// queue row carries the API path it opens, returned by the server so the queue and the link cannot disagree -
// which is the defect a queue listing work the caller cannot open reproduces.
//
// The review dashboard's aging figure is a DURATION, not a breach. No document defines a review SLA -
// BUSINESS-PROCESSES §2 names the timer and never its length - so nothing here may imply a threshold.

import { apiFetch } from './auth'
import { ProblemError } from './problem'

export interface ProcurementKpis {
  activeRfqs: number
  closingThisWeek: number
  awaitingMyAction: number
  pendingApprovals: number
  awardsInProgress: number
}

export interface PipelineColumn {
  state: string
  count: number
  nearestDeadline: string | null
}

export type DashboardTaskKind = 'SubmissionClosing' | 'EvaluationDue' | 'RecommendationPending'

export interface DashboardTask {
  rfqReferenceCode: string
  titleAr: string
  titleEn: string
  kind: DashboardTaskKind
  due: string | null
}

export interface ProcurementDashboard {
  kpis: ProcurementKpis
  pipeline: PipelineColumn[]
  tasks: DashboardTask[]
  showsApprovals: boolean
}

export interface ApprovalQueueItem {
  rfqReferenceCode: string
  titleAr: string
  titleEn: string
  state: string
  waitingSince: string | null
  href: string
}

export interface ApprovalQueues {
  rfqPublishApprovals: ApprovalQueueItem[]
  awardApprovals: ApprovalQueueItem[]
}

export interface ExpiringDocument {
  supplierReferenceCode: string
  supplierDisplayNameAr: string
  supplierDisplayNameEn: string
  documentTypeCode: string
  state: string
  expiryDate: string | null
}

export interface ReviewDashboard {
  pending: number
  underReview: number
  infoRequested: number
  unassigned: number
  assignedToMe: number
  oldestOpenCaseAgeDays: number | null
  expiryWatchlist: ExpiringDocument[]
}

export class DashboardApiError extends ProblemError {
  constructor(status: number, body: unknown) {
    super(status, body)
  }
}

async function get<T>(path: string): Promise<T> {
  const response = await apiFetch(path)
  if (!response.ok) throw new DashboardApiError(response.status, await response.json().catch(() => null))
  return (await response.json()) as T
}

export function getProcurementDashboard(from?: string, to?: string): Promise<ProcurementDashboard> {
  const params = new URLSearchParams()
  if (from) params.set('from', from)
  if (to) params.set('to', to)
  const query = params.toString()

  return get<ProcurementDashboard>(`/api/v1/procurement/dashboard${query ? `?${query}` : ''}`)
}

export const getApprovalQueues = () => get<ApprovalQueues>('/api/v1/procurement/approvals')
export const getReviewDashboard = () => get<ReviewDashboard>('/api/v1/review/dashboard')
