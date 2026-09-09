import { apiFetch } from './auth'
import type { ListEnvelope } from './listEnvelope'
import { hasProblemProse, problemMessage, type ProblemDetails } from './problem'

/**
 * SCR-402 and SCR-307: the two reads across the supplier registry.
 *
 * <p>Two functions against two endpoints, not one with a role switch. A buyer browsing for capability and
 * a reviewer auditing compliance ask different questions and get different columns — see
 * `SupplierDirectoryEndpoints` for why the server keeps them apart, and note that neither response carries
 * the other's fields, so a screen cannot quietly render data its persona should not read.</p>
 */

export class SupplierDirectoryApiError extends Error {
  status: number
  /** Read by `errorDetail`: this message is the server's own prose, not a bug's. False when the
   * problem document carried no `title` and no `detail`, because `problemMessage` then falls back to
   * "Request failed: <status>", which is developer text and must not reach a reader. */
  isProblemError: boolean
  constructor(status: number, body: unknown) {
    super(problemMessage(body as ProblemDetails | null, `Request failed: ${status}`))
    this.isProblemError = hasProblemProse(body as ProblemDetails | null)
    this.status = status
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
