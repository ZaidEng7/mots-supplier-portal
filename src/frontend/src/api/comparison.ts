import { problemMessage, type ProblemDetails } from './problem'
import { apiFetch } from './auth'

export interface ComparisonRfqItem {
  id: string
  lineNo: number
  titleAr: string
  titleEn: string
  quantity: number
  unitOfMeasureCode: string
}

export interface ComparisonRequirementAnswer {
  requirementId: string
  textAr: string
  textEn: string
  isMandatory: boolean
  answered: boolean
}

export interface ComparisonItemPrice {
  rfqItemId: string
  quantity: number
  unitPrice: number
  discount: number | null
  lineTotal: number
}

export interface ComparisonCriterionScore {
  criterionId: string
  nameAr: string
  nameEn: string
  isFinancial: boolean
  weight: number
  maxScore: number
  threshold: number | null
  averageScore: number
  metThreshold: boolean | null
}

/** Financial fields (items/grandTotal) and every evaluation-derived field are null until the
 * two-envelope gate opens for this proposal (Consolidated+ AND technically qualified) - see the
 * backend's ComparisonProposalDto for the exact rule. Never render a null here as zero/empty. */
export interface ComparisonProposal {
  proposalReferenceCode: string
  supplierId: string
  supplierDisplayNameAr: string
  supplierDisplayNameEn: string
  currencyCode: string | null
  paymentTerms: string | null
  incotermCode: string | null
  deliveryTermsAr: string | null
  deliveryTermsEn: string | null
  warranty: string | null
  validityEnd: string | null
  submittedAt: string
  requirements: ComparisonRequirementAnswer[]
  items: ComparisonItemPrice[] | null
  grandTotal: number | null
  technicallyQualified: boolean | null
  technicalWeightedScore: number | null
  financialWeightedScore: number | null
  weightedTotal: number | null
  rank: number | null
  /** A-1/BRULE-069: this rank came from a tie no rule broke. The award flow refuses rank 1 while it is
   * set, so the officer has to be able to see it and resolve it here. */
  tieUnresolved: boolean
  tieResolutionReason: string | null
  criterionScores: ComparisonCriterionScore[] | null
}

export type ComparisonEvaluationState = 'NotStarted' | 'Assigned' | 'InProgress' | 'EvaluatorSubmitted' | 'Consolidated' | 'Finalized'

export interface Comparison {
  rfqReferenceCode: string
  rfqTitleAr: string
  rfqTitleEn: string
  evaluationState: ComparisonEvaluationState
  rfqItems: ComparisonRfqItem[]
  proposals: ComparisonProposal[]
}

export class ComparisonApiError extends Error {
  status: number
  constructor(status: number, body: unknown) {
    const b = body as ProblemDetails | null
    super(problemMessage(b, `Request failed: ${status}`))
    this.status = status
  }
}

export async function getComparison(rfqReferenceCode: string): Promise<Comparison | null> {
  const res = await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/comparison`)
  if (res.status === 404) return null
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new ComparisonApiError(res.status, body)
  return body as Comparison
}

/** A-1: a person breaks a tie the rules could not, and says why. `evaluation.consolidate` gates it -
 * the same permission that produced the ranking. */
export async function resolveEvaluationTie(
  rfqReferenceCode: string,
  proposalCode: string,
  reason: string,
): Promise<void> {
  const response = await apiFetch(`/api/v1/rfqs/${rfqReferenceCode}/evaluation/resolve-tie`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ proposalCode, reason }),
  })
  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as ProblemDetails | null
    throw new Error(problemMessage(body, 'tie_resolution_failed'))
  }
}
