import { problemMessage, type ProblemDetails } from './problem'
import { apiFetch } from './auth'

/** Backend returns { error: "invalid_state", message: "<precise domain message>" } for every
 * EvaluationTemplate invariant refusal (EvaluationTemplateMutationResult.InvalidState) - unlike
 * SupplierApiError's `.error`-only convention, the precise message is the useful part here since
 * these are dynamic domain-exception texts, not a small enum of known codes. */
export class EvaluationTemplateApiError extends Error {
  status: number
  constructor(status: number, body: unknown) {
    const b = body as ProblemDetails | null
    super(problemMessage(b, `Request failed: ${status}`))
    this.status = status
  }
}

export type CriterionDimension = 'Technical' | 'Commercial' | 'Compliance' | 'Delivery'
export type ScoringType = 'Numeric' | 'Scale' | 'Boolean' | 'Formula'
export type EvaluationTemplateStatus = 'Draft' | 'Active' | 'Archived'

export interface Criterion {
  id: string
  nameAr: string
  nameEn: string
  dimension: CriterionDimension
  weight: number
  maxScore: number
  threshold: number | null
  scoringType: ScoringType
  guidanceAr: string | null
  guidanceEn: string | null
  sortOrder: number
}

export interface EvaluationTemplate {
  id: string
  familyId: string
  version: number
  nameAr: string
  nameEn: string
  status: EvaluationTemplateStatus
  isReferenced: boolean
  criteria: Criterion[]
}

export interface CriterionPayload {
  nameAr: string
  nameEn: string
  dimension: CriterionDimension
  weight: number
  maxScore: number
  threshold: number | null
  scoringType: ScoringType
  guidanceAr: string | null
  guidanceEn: string | null
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new EvaluationTemplateApiError(res.status, body)
  return body as T
}

export async function listEvaluationTemplates(): Promise<EvaluationTemplate[]> {
  return parseOrThrow(await apiFetch('/api/v1/evaluation-templates'))
}

export async function createEvaluationTemplate(nameAr: string, nameEn: string): Promise<EvaluationTemplate> {
  return parseOrThrow(await apiFetch('/api/v1/evaluation-templates', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ nameAr, nameEn }),
  }))
}

export async function addCriterion(templateId: string, payload: CriterionPayload): Promise<EvaluationTemplate> {
  return parseOrThrow(await apiFetch(`/api/v1/evaluation-templates/${templateId}/criteria`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function activateEvaluationTemplate(templateId: string): Promise<EvaluationTemplate> {
  return parseOrThrow(await apiFetch(`/api/v1/evaluation-templates/${templateId}/activate`, { method: 'POST' }))
}

export async function archiveEvaluationTemplate(templateId: string): Promise<EvaluationTemplate> {
  return parseOrThrow(await apiFetch(`/api/v1/evaluation-templates/${templateId}/archive`, { method: 'POST' }))
}

export async function forkEvaluationTemplate(templateId: string): Promise<EvaluationTemplate> {
  return parseOrThrow(await apiFetch(`/api/v1/evaluation-templates/${templateId}/fork`, { method: 'POST' }))
}
