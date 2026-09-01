import { apiFetch } from './auth'

export type EvaluationState = 'NotStarted' | 'Assigned' | 'InProgress' | 'EvaluatorSubmitted' | 'Consolidated' | 'Finalized'
export type CriterionDimension = 'Technical' | 'Commercial' | 'Compliance' | 'Delivery'
export type ScoringType = 'Numeric' | 'Scale' | 'Boolean' | 'Formula'

export interface EvaluationCriterion {
  id: string
  nameAr: string
  nameEn: string
  dimension: CriterionDimension
  weight: number
  maxScore: number
  threshold: number | null
  scoringType: ScoringType
  isFinancial: boolean
}

export interface EvaluationAssignment {
  evaluatorUserId: string
  assignedAt: string
  submittedAt: string | null
  recusedAt: string | null
  recusalReason: string | null
}

export interface ConsolidatedResult {
  proposalId: string
  technicallyQualified: boolean
  technicalWeightedScore: number
  financialWeightedScore: number | null
  weightedTotal: number
  rank: number | null
}

/** Buyer/manager-facing overview - deliberately never carries a raw EvaluatorScore row (blind
 * scoring, OQ-005/BRULE-058) - see EvaluationDto's own doc comment on the backend. */
export interface Evaluation {
  id: string
  rfqId: string
  rfqReferenceCode: string
  state: EvaluationState
  criteria: EvaluationCriterion[]
  assignments: EvaluationAssignment[]
  results: ConsolidatedResult[]
}

export interface MyScore {
  proposalId: string
  criterionId: string
  rawScore: number
  commentAr: string | null
  commentEn: string | null
  scoredAt: string
}

/** Evaluator-facing view - only ever this evaluator's own scores, see MyEvaluationDto's own doc
 * comment on the backend for why. */
export interface MyEvaluation {
  id: string
  rfqId: string
  rfqReferenceCode: string
  state: EvaluationState
  submittedAt: string | null
  criteria: EvaluationCriterion[]
  proposalIds: string[]
  technicallyQualifiedByProposal: Record<string, boolean>
  myScores: MyScore[]
}

export class EvaluationApiError extends Error {
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
  if (!res.ok) throw new EvaluationApiError(res.status, body)
  return body as T
}

export async function getEvaluation(rfqReferenceCode: string): Promise<Evaluation | null> {
  const res = await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/evaluation`)
  if (res.status === 404) return null
  return parseOrThrow(res)
}

export async function openEvaluation(rfqReferenceCode: string): Promise<Evaluation> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/evaluation/open`, { method: 'POST' }))
}

export async function assignEvaluators(rfqReferenceCode: string, evaluatorUserIds: string[]): Promise<Evaluation> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/evaluation/assignments`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ evaluatorUserIds }),
  }))
}

export async function recuseEvaluator(rfqReferenceCode: string, evaluatorUserId: string, reason: string): Promise<Evaluation> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/evaluation/recuse`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ evaluatorUserId, reason }),
  }))
}

export async function consolidateEvaluation(rfqReferenceCode: string): Promise<Evaluation> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/evaluation/consolidate`, { method: 'POST' }))
}

export async function finalizeEvaluation(rfqReferenceCode: string): Promise<Evaluation> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/evaluation/finalize`, { method: 'POST' }))
}

export async function reopenEvaluation(rfqReferenceCode: string, reason: string): Promise<Evaluation> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/evaluation/reopen`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  }))
}

export async function getMyEvaluation(rfqReferenceCode: string): Promise<MyEvaluation | null> {
  const res = await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/my-evaluation`)
  if (res.status === 404) return null
  return parseOrThrow(res)
}

export async function scoreCriterion(
  rfqReferenceCode: string,
  payload: { proposalId: string; criterionId: string; rawScore: number; commentAr: string | null; commentEn: string | null },
): Promise<MyEvaluation> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/my-evaluation/scores`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function submitMyEvaluation(rfqReferenceCode: string): Promise<MyEvaluation> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/my-evaluation/submit`, { method: 'POST' }))
}
