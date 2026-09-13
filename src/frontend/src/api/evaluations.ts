// The evaluation, from both sides: the manager's overview and the assigned evaluator's own workspace.
//
// A criterion carries the justification flag of T-021 and BRULE-061. It has been on the wire since EPIC-07 and was
// absent from this interface until SCR-501 needed it - the same shape of drift T-110 found on the supplier
// profile, where the type and the response had disagreed for two batches. The scoring form still does not read it;
// the brief does. It also carries SCR-501's guidance: the template author's instruction for that criterion,
// snapshotted when the RFQ bound the template, and null or absent on a tender that bound one before the field
// existed.
//
// An assignment carries the evaluator's NAME. The table used to render the GUID, so the recuse button beside it
// named nobody. A consolidated result still emits the internal identifier, read by nothing here per D-69, and
// carries §3's opaque public identifier - the table used to render the internal GUID on the screen where a manager
// decides who wins a tender.
//
// Evaluation is the buyer and manager-facing overview, and it deliberately never carries a raw EvaluatorScore row,
// because of blind scoring under OQ-005 and BRULE-058 - see EvaluationDto on the backend. MyEvaluation is the
// evaluator-facing view, and only ever this evaluator's own scores, for the reason MyEvaluationDto gives. It
// carries the specification the bids answer (T-067), because an evaluator holds neither rfq.read nor
// comparison.view and this read is their only window onto it. MyScore identifies its proposal by the public code
// rather than the GUID (T-068).
//
// EvaluatorProposal is T-067's: one bid as an assigned evaluator sees it during scoring - the TECHNICAL envelope
// only. There is no pricing on this type and none on the wire; a commercial figure reaches a human through the
// comparison matrix after consolidation and nowhere else. It carries A-8's stable pseudonym, the name a bid is
// known by while scoring is open - "Bidder A", «مورّد أ» - always present, so a comment can refer to a bid whether
// or not its owner is revealed. The supplier's identity is NULL while this evaluator's scoring is open, and
// present both before scoring opens, at the recusal declaration under BRULE-067, and after consolidation. That
// supersedes D-19.
//
// evaluatorProposalDocumentUrl is T-067's signed URL for one technical document on a bid under evaluation, gated
// on the caller's ACTIVE assignment and on the Technical envelope: a commercial document is the same 404 as one
// that does not exist.
//
// EvaluationApiError carries the xmin RowVersion conflict flag of EPIC-13 and FR-PWF-005 - see RfqApiError, where
// the reasoning is written out.
//
// listEvaluatorCandidates answers who a manager may assign to this evaluation: staff in the RFQ's organisation who
// hold evaluation.score.
//
// getConflictDeclaration is A-8 and BRULE-067's recusal declaration window. The names are here, once, before
// scoring - and this read deliberately does NOT open scoring, unlike getMyEvaluation. A 404 from it is "not
// assigned" per §9.2, which the page treats as nothing to declare.

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
  requiresJustification?: boolean
  guidanceAr?: string | null
  guidanceEn?: string | null
}

export interface EvaluationAssignment {
  evaluatorUserId: string
  evaluatorName: string | null
  assignedAt: string
  submittedAt: string | null
  recusedAt: string | null
  recusalReason: string | null
}

export interface ConsolidatedResult {
  proposalId: string
  proposalCode: string
  technicallyQualified: boolean
  technicalWeightedScore: number
  financialWeightedScore: number | null
  weightedTotal: number
  rank: number | null
}

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

export interface EvaluatorProposal {
  proposalCode: string
  bidderLabelAr: string
  bidderLabelEn: string
  supplierReferenceCode: string | null
  supplierDisplayNameAr: string | null
  supplierDisplayNameEn: string | null
  narrativeAr: string | null
  narrativeEn: string | null
  requirementAnswers: RequirementAnswer[]
  documents: EvaluatorProposalDocument[]
  technicallyQualified: boolean
}

export interface MyEvaluation {
  rfqReferenceCode: string
  state: EvaluationState
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

export interface ConflictDeclaration {
  declarationRequired: boolean
  bidders: { proposalCode: string; supplierDisplayNameAr: string; supplierDisplayNameEn: string }[]
}

export async function getConflictDeclaration(rfqReferenceCode: string): Promise<ConflictDeclaration | null> {
  const response = await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/my-evaluation/bidders`)
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
