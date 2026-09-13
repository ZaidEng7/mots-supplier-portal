// The comparison matrix, and the one write on it: resolving a tie.
//
// The financial fields - items and grandTotal - and every evaluation-derived field are null until the
// two-envelope gate opens for that proposal, meaning Consolidated or later AND technically qualified. The exact
// rule lives in the backend's ComparisonProposalDto. A null here must never be rendered as a zero or an empty
// cell: the whole point of the gate is that the figure is withheld rather than absent.
//
// A rank can carry A-1 and BRULE-069's unresolved-tie marker, which says the rank came from a tie no rule broke.
// The award flow refuses rank 1 while it is set, so the officer has to be able to see it and resolve it from
// here. resolveEvaluationTie is A-1's own act: a person breaks a tie the rules could not, and says why. It is
// gated on evaluation.consolidate - the same permission that produced the ranking.

import { ProblemError, problemMessage, type ProblemDetails } from './problem'
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

export class ComparisonApiError extends ProblemError {
  constructor(status: number, body: unknown) {
    super(status, body)
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
