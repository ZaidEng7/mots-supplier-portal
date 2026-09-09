import { apiFetch } from './auth'
import type { ListEnvelope } from './listEnvelope'
import { ProblemError } from './problem'

/**
 * SCR-402 and SCR-307: the two reads across the supplier registry.
 *
 * <p>Two functions against two endpoints, not one with a role switch. A buyer browsing for capability and
 * a reviewer auditing compliance ask different questions and get different columns — see
 * `SupplierDirectoryEndpoints` for why the server keeps them apart, and note that neither response carries
 * the other's fields, so a screen cannot quietly render data its persona should not read.</p>
 */

export class SupplierDirectoryApiError extends ProblemError {
  constructor(status: number, body: unknown) {
    super(status, body)
  }
}

/** SCR-402: an approved supplier as a buyer sees it. */
export interface DirectorySupplier {
  supplierCode: string
  displayNameAr: string
  displayNameEn: string
  lifecycleState: string
  categoryCodes: string[]
  offeringCount: number
  city: string | null
  regionCode: string | null
}

/** SCR-307: a supplier as the compliance reviewer sees it, with document health attached. */
export interface ComplianceSupplier {
  supplierCode: string
  displayNameAr: string
  displayNameEn: string
  onboardingState: string
  lifecycleState: string
  createdAt: string
  expiredDocumentCount: number
  expiringDocumentCount: number
  rejectedDocumentCount: number
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierDirectoryApiError(res.status, body)
  return body as T
}

export async function listSupplierDirectory(
  cursor?: string | null,
  filters?: { category?: string | null, lifecycleState?: string | null, q?: string | null },
): Promise<ListEnvelope<DirectorySupplier>> {
  const params = new URLSearchParams()
  if (cursor) params.set('cursor', cursor)
  if (filters?.category) params.set('category', filters.category)
  if (filters?.lifecycleState) params.set('lifecycleState', filters.lifecycleState)
  if (filters?.q) params.set('q', filters.q)
  const qs = params.toString() ? `?${params.toString()}` : ''
  return parseOrThrow(await apiFetch(`/api/v1/supplier-directory${qs}`))
}

export async function listComplianceDirectory(
  cursor?: string | null,
  filters?: { onboardingState?: string | null, documentHealth?: string | null, q?: string | null },
): Promise<ListEnvelope<ComplianceSupplier>> {
  const params = new URLSearchParams()
  if (cursor) params.set('cursor', cursor)
  if (filters?.onboardingState) params.set('onboardingState', filters.onboardingState)
  if (filters?.documentHealth) params.set('documentHealth', filters.documentHealth)
  if (filters?.q) params.set('q', filters.q)
  const qs = params.toString() ? `?${params.toString()}` : ''
  return parseOrThrow(await apiFetch(`/api/v1/review/suppliers${qs}`))
}
