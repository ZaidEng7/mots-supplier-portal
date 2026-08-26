import { apiFetch } from './auth'
import type { DocumentTypeStatus } from './documents'
import type { SupplierProfile } from './supplier'

export interface ReviewQueueItem {
  referenceCode: string
  displayNameAr: string
  displayNameEn: string
  onboardingState: string
}

export interface ReviewAnnotation {
  id: string
  requestedAt: string
  reason: string
  flaggedProfileFields: string[]
  flaggedDocumentTypeCodes: string[]
  resolvedAt: string | null
}

export interface ReviewerSupplierView {
  supplier: SupplierProfile
  documents: DocumentTypeStatus[]
  annotationHistory: ReviewAnnotation[]
}

export class ReviewApiError extends Error {
  status: number
  constructor(status: number, body: unknown) {
    const b = body as { error?: string } | null
    super(b?.error ?? `Request failed: ${status}`)
    this.status = status
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new ReviewApiError(res.status, body)
  return body as T
}

export async function getOwnActiveAnnotation(): Promise<ReviewAnnotation | null> {
  const res = await apiFetch('/api/v1/suppliers/me/active-annotation')
  return parseOrThrow(res)
}

export async function listReviewQueue(): Promise<ReviewQueueItem[]> {
  const res = await apiFetch('/api/v1/review/queue')
  return parseOrThrow(res)
}

export async function getReviewerSupplierView(referenceCode: string): Promise<ReviewerSupplierView> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}`)
  return parseOrThrow(res)
}

export async function pickUpApplication(referenceCode: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/pickup`, { method: 'POST' })
  return parseOrThrow(res)
}

export async function approveApplication(referenceCode: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/approve`, { method: 'POST' })
  return parseOrThrow(res)
}

export async function rejectApplication(referenceCode: string, reason: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/reject`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  })
  return parseOrThrow(res)
}

export async function requestApplicationInfo(
  referenceCode: string,
  reason: string,
  flaggedProfileFields: string[],
  flaggedDocumentTypeCodes: string[],
): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/review/${referenceCode}/request-info`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason, flaggedProfileFields, flaggedDocumentTypeCodes }),
  })
  return parseOrThrow(res)
}
