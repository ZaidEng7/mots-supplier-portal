// FEAT-13.1 and FR-PWF-001: the tender workspace, a read-side aggregation over Rfq, Proposal, Evaluation and
// Award with no new persisted state.
//
// These interfaces mirror the backend's WorkspaceDto, WorkspaceStageDto and WorkspaceActionDto. Only the 10
// RfqState values any domain method can actually reach are ever listed as stages - see the backend handler, which
// explains why. An action's `permitted` already reflects BOTH the caller's own permission claim and the domain
// precondition, resolved server-side: the frontend never re-derives it.
//
// A cancelled RFQ carries isCancelled true with empty stages and next actions rather than a guessed stage
// position, for the reason the backend handler gives.

import { ProblemError } from './problem'
import { apiFetch } from './auth'

export interface WorkspaceStage {
  key: string
  isCurrent: boolean
  isCompleted: boolean
}

export interface WorkspaceAction {
  action: string
  labelAr: string
  labelEn: string
  permitted: boolean
  blockedReasonAr: string | null
  blockedReasonEn: string | null
}

export interface Workspace {
  rfqReferenceCode: string
  rfqState: string
  isCancelled: boolean
  submittedProposalCount: number
  evaluationState: string | null
  awardState: string | null
  stages: WorkspaceStage[]
  nextActions: WorkspaceAction[]
}

export class WorkspaceApiError extends ProblemError {
  constructor(status: number, body: unknown) {
    super(status, body)
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new WorkspaceApiError(res.status, body)
  return body as T
}

export async function getWorkspace(rfqReferenceCode: string): Promise<Workspace | null> {
  const res = await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/workspace`)
  if (res.status === 404) return null
  return parseOrThrow(res)
}
