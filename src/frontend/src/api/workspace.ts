import { apiFetch } from './auth'

/** FEAT-13.1/FR-PWF-001: mirrors WorkspaceStageDto - only the 10 RfqState values any domain method
 * can actually reach are ever listed (see the backend handler's own doc comment). */
export interface WorkspaceStage {
  key: string
  isCurrent: boolean
  isCompleted: boolean
}

/** Mirrors WorkspaceActionDto. `permitted` already reflects BOTH the caller's own permission claim
 * and the domain precondition, resolved server-side - the frontend never re-derives this. */
export interface WorkspaceAction {
  action: string
  labelAr: string
  labelEn: string
  permitted: boolean
  blockedReasonAr: string | null
  blockedReasonEn: string | null
}

/** Mirrors WorkspaceDto - a read-side aggregation over Rfq + Proposal + Evaluation + Award, no new
 * persisted state. Cancelled RFQs carry `isCancelled: true` with empty stages/nextActions rather
 * than a guessed stage position - see the backend handler's own doc comment on why. */
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

export class WorkspaceApiError extends Error {
  status: number
  constructor(status: number, body: unknown) {
    const b = body as { error?: string; message?: string } | null
    super(b?.message ?? b?.error ?? `Request failed: ${status}`)
    this.status = status
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
