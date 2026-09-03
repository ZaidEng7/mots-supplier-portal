import { apiFetch } from './auth'
import { problemMessage, type ProblemDetails } from './problem'

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
  constructor(status: number, body: unknown) {
    super(problemMessage(body as ProblemDetails | null, `Request failed: ${status}`))
    this.status = status
  }
}

export async function listMyAssignments(tab?: MyAssignmentTab): Promise<MyAssignment[]> {
  const response = await apiFetch(`/api/v1/my-evaluations${tab ? `?tab=${tab}` : ''}`)
  if (!response.ok) throw new MyEvaluationsApiError(response.status, await response.json().catch(() => null))
  return (await response.json()) as MyAssignment[]
}
