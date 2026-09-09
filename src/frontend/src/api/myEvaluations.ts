import { apiFetch } from './auth'
import { hasProblemProse, problemMessage, type ProblemDetails } from './problem'

/**
 * SCR-500 / FR-DSH-004. The evaluator's own assignments.
 *
 * <p>Scoped server-side by ASSIGNMENT, not by organization - an evaluator need not belong to the
 * procuring organization and may have no organization at all. Nothing here passes an org.</p>
 */
export type MyAssignmentTab = 'Assigned' | 'InProgress' | 'Submitted'

export interface MyAssignment {
  rfqReferenceCode: string
  rfqTitleAr: string
  rfqTitleEn: string
  evaluationState: string
  evaluationTargetDate: string | null
  assignedAt: string
  submittedAt: string | null
  scoresRecorded: number
  scoresExpected: number
  tab: MyAssignmentTab
}

export class MyEvaluationsApiError extends Error {
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

export async function listMyAssignments(tab?: MyAssignmentTab): Promise<MyAssignment[]> {
  const response = await apiFetch(`/api/v1/my-evaluations${tab ? `?tab=${tab}` : ''}`)
  if (!response.ok) throw new MyEvaluationsApiError(response.status, await response.json().catch(() => null))
  return (await response.json()) as MyAssignment[]
}
