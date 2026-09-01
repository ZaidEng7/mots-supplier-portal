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
    const b = body as { error?: string; message?: string } | null
    super(b?.message ?? b?.error ?? `Request failed: ${status}`)
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
