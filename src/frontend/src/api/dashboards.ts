import { apiFetch } from './auth'
import { hasProblemProse, problemMessage, type ProblemDetails } from './problem'

/** SCR-400's KPI row (SCREEN-SPECIFICATIONS.md §10). */
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
  /** §10: "Manager also gets an Approvals card". Decided server-side from the permission. */
  showsApprovals: boolean
}

export interface ApprovalQueueItem {
  rfqReferenceCode: string
  titleAr: string
  titleEn: string
  state: string
  waitingSince: string | null
  /** The API path this row opens. Returned by the server so the queue and the link cannot disagree. */
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
  /**
   * A duration, not a breach. No document defines a review SLA - BUSINESS-PROCESSES §2 names the
   * timer and never its length - so nothing here may imply a threshold.
   */
  oldestOpenCaseAgeDays: number | null
  expiryWatchlist: ExpiringDocument[]
}

export class DashboardApiError extends Error {
  status: number
  /** Read by `errorDetail`: this message is the server's own prose, not a bug's. False when the
   * problem document carried no `title` and no `detail`, because `problemMessage` then falls back to
   * "Request failed: <status>", which is developer text and must not reach a reader. */
  isProblemError: boolean
  constructor(status: number, body: unknown) {
    super(problemMessage(body as ProblemDetails | null, `Request failed: ${status}`))
    this.isProblemError = hasProblemProse(body as ProblemDetails | null)
    this.status = status
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
