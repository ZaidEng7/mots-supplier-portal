import { ProblemError, hasCode, type ProblemDetails } from './problem'
import { apiFetch } from './auth'
import type { RfqItem, Requirement } from './rfqs'

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
  /** T-021/BRULE-061. On the wire since EPIC-07 and absent from this interface until SCR-501 needed it —
   * the same shape of drift T-110 found on the supplier profile, where the type and the response had
   * disagreed for two batches. The scoring form still does not read it; the brief does. */
  requiresJustification?: boolean
  /** SCR-501: the template author's instruction for this criterion, snapshotted when the RFQ bound the
   * template. Null or absent on a tender that bound one before the field existed. */
  guidanceAr?: string | null
  guidanceEn?: string | null
}

export interface EvaluationAssignment {
  evaluatorUserId: string
  /** The evaluator's name. The table used to render the GUID, so the recuse button beside it named nobody. */
  evaluatorName: string | null
  assignedAt: string
  submittedAt: string | null
  recusedAt: string | null
  recusalReason: string | null
}

export interface ConsolidatedResult {
  /** Still emitted, read by nothing here - see D-69. */
  proposalId: string
  proposalCode: string
  /** §3's opaque public identifier. The table used to render the internal GUID on the screen where a
   *  manager decides who wins a tender. */
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
  /** T-068: the proposal's public code, not its GUID. */
  proposalCode: string
  criterionId: string
  rawScore: number
  commentAr: string | null
  commentEn: string | null
  scoredAt: string
}

export interface EvaluatorProposalDocument {
  id: string
  originalFileName: string
  contentType: string
  caption: string | null
  uploadedAt: string
}

export interface RequirementAnswer {
  id: string
  requirementId: string
  answerAr: string
  answerEn: string
}

/** T-067: one bid as an assigned evaluator sees it during scoring - the TECHNICAL envelope only.
 * There is no pricing on this type and none on the wire; a commercial figure reaches a human through
 * the comparison matrix after consolidation and nowhere else. */
export interface EvaluatorProposal {
  proposalCode: string
  /** A-8: the stable pseudonym a bid is known by while scoring is open - "Bidder A", «مورّد أ». Always
   * present, so a comment can refer to a bid whether or not its owner is revealed. */
  bidderLabelAr: string
  bidderLabelEn: string
  /** A-8: NULL while this evaluator's scoring is open. Present before scoring opens (the recusal
   * declaration, BRULE-067) and after consolidation. Supersedes D-19. */
  supplierReferenceCode: string | null
  supplierDisplayNameAr: string | null
  supplierDisplayNameEn: string | null
  narrativeAr: string | null
  narrativeEn: string | null
  requirementAnswers: RequirementAnswer[]
  documents: EvaluatorProposalDocument[]
  technicallyQualified: boolean
}

/** Evaluator-facing view - only ever this evaluator's own scores, see MyEvaluationDto's own doc
 * comment on the backend for why. */
export interface MyEvaluation {
  rfqReferenceCode: string
  state: EvaluationState
  /** T-067: the specification the bids answer. An evaluator holds neither rfq.read nor
   * comparison.view, so this read is their only window onto it. */
  rfqTitleAr: string
  rfqTitleEn: string
  rfqDescriptionAr: string | null
  rfqDescriptionEn: string | null
  rfqItems: RfqItem[]
  rfqRequirements: Requirement[]
  submittedAt: string | null
  criteria: EvaluationCriterion[]
  proposals: EvaluatorProposal[]
  myScores: MyScore[]
}

/** T-067: the signed URL for one technical document on a bid under evaluation. Gated on the caller's
 * ACTIVE assignment and on the Technical envelope; a commercial document is the same 404 as one that
 * does not exist. */
export async function evaluatorProposalDocumentUrl(
  rfqReferenceCode: string,
  proposalCode: string,
  documentId: string,
): Promise<string> {
  const response = await apiFetch(
    `/api/v1/rfqs/${rfqReferenceCode}/my-evaluation/proposals/${proposalCode}/documents/${documentId}/download-url`,
  )
  if (!response.ok) throw new Error('document_unavailable')
  const body = (await response.json()) as { url: string }
  return body.url
}

export class EvaluationApiError extends ProblemError {
  /** EPIC-13/FR-PWF-005: xmin (RowVersion) conflict - see RfqApiError's own doc comment. */
  isConcurrencyConflict: boolean
  constructor(status: number, body: unknown) {
    super(status, body)
    this.isConcurrencyConflict = status === 412 && hasCode(body as ProblemDetails | null, 'ETAG_MISMATCH')
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

/** Who a manager may assign to this evaluation — staff in the RFQ's organisation who hold evaluation.score. */
export interface EvaluatorCandidate {
  userId: string
  fullName: string
  email: string
}

export async function listEvaluatorCandidates(rfqReferenceCode: string): Promise<EvaluatorCandidate[]> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/evaluation/candidates`))
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
  payload: { proposalCode: string; criterionId: string; rawScore: number; commentAr: string | null; commentEn: string | null },
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

/** A-8/BRULE-067: the recusal declaration window. The names are here, once, before scoring - and this
 * read deliberately does NOT open scoring, unlike getMyEvaluation. */
export interface ConflictDeclaration {
  declarationRequired: boolean
  bidders: { proposalCode: string; supplierDisplayNameAr: string; supplierDisplayNameEn: string }[]
}

export async function getConflictDeclaration(rfqReferenceCode: string): Promise<ConflictDeclaration | null> {
  const response = await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/my-evaluation/bidders`)
  // 404 is "not assigned" (§9.2), which the page treats as nothing to declare.
  if (response.status === 404) return null
  if (!response.ok) throw new Error('declaration_unavailable')
  return (await response.json()) as ConflictDeclaration
}

export async function declareConflict(
  rfqReferenceCode: string,
  hasConflict: boolean,
  reason?: string,
): Promise<void> {
  const response = await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/my-evaluation/declare`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ hasConflict, reason: reason ?? null }),
  })
  if (!response.ok) throw new Error('declaration_failed')
}
