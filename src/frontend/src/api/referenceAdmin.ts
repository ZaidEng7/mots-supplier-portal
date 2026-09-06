import { apiFetch } from './auth'
import { SupplierApiError } from './supplier'

/** T-080/SCR-710–712. The five tables an administrator may edit — the same list the server accepts, so a
 * typo is a refusal rather than a silent no-op against the wrong table. */
export const REFERENCE_TABLES = ['categories', 'document-types', 'currencies', 'units-of-measure', 'regions'] as const
export type ReferenceTable = (typeof REFERENCE_TABLES)[number]

export interface ReferenceItem {
  code: string
  nameAr: string
  nameEn: string
  isActive: boolean
  /** DocumentType only. Null on every other table rather than false — "this table has no such flag" and
   * "this row has the flag off" are different facts. */
  isRequired: boolean | null
  expiryTracked: boolean | null
  /** BRULE-023. Null on every table but document-types. Expiry of an award-critical document suspends the
   *  supplier, and this flag is the only thing that decides which types those are. */
  isAwardCritical: boolean | null
}

export interface ReferenceItemPayload {
  nameAr: string
  nameEn: string
  isRequired?: boolean | null
  expiryTracked?: boolean | null
  /** Omit to leave the stored value alone — the server only writes it when it is sent, so editing a name
   *  cannot clear the one flag on this screen that suspends live suppliers. */
  isAwardCritical?: boolean | null
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

/** Inactive rows are hidden by default and reachable by asking — an admin editing the catalogue needs to
 * see what they deactivated, or deactivation reads as deletion and the next administrator recreates the
 * code (D-28's whole point). */
export async function listReferenceItems(table: ReferenceTable, includeInactive = true): Promise<ReferenceItem[]> {
  return parseOrThrow(await apiFetch(`/api/v1/admin/reference/${table}?includeInactive=${includeInactive}`))
}

export async function createReferenceItem(table: ReferenceTable, code: string, payload: ReferenceItemPayload): Promise<ReferenceItem> {
  return parseOrThrow(await apiFetch(`/api/v1/admin/reference/${table}/${encodeURIComponent(code)}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

/** The CODE cannot change — it is the foreign key in every live row that points at this item, with no
 * cascade (D-28). Only the names and the DocumentType flags are editable. */
export async function updateReferenceItem(table: ReferenceTable, code: string, payload: ReferenceItemPayload): Promise<ReferenceItem> {
  return parseOrThrow(await apiFetch(`/api/v1/admin/reference/${table}/${encodeURIComponent(code)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

/** Deactivate, never delete. There is no delete endpoint to call (D-28). */
export async function setReferenceItemActive(table: ReferenceTable, code: string, isActive: boolean): Promise<ReferenceItem> {
  const action = isActive ? 'reactivate' : 'deactivate'
  return parseOrThrow(await apiFetch(`/api/v1/admin/reference/${table}/${encodeURIComponent(code)}/${action}`, { method: 'POST' }))
}

/** BRULE-016. Which categories a document type is required for — recorded, and read by nothing yet. */
export interface DocumentTypeCategoryLinks {
  documentTypeCode: string
  /** Empty means no links recorded, which is NOT the same as "required for nothing" — see the endpoint. */
  categoryCodes: string[]
}

export async function getDocumentTypeCategories(): Promise<DocumentTypeCategoryLinks[]> {
  const response = await apiFetch('/api/v1/admin/document-type-categories')
  if (!response.ok) throw new Error('document_type_categories_unavailable')
  return (await response.json()) as DocumentTypeCategoryLinks[]
}

/** The whole set, because "required for these categories" is one decision, not a sequence of clicks. */
export async function setDocumentTypeCategories(
  documentTypeCode: string,
  categoryCodes: string[],
): Promise<DocumentTypeCategoryLinks> {
  const response = await apiFetch(`/api/v1/admin/document-type-categories/${encodeURIComponent(documentTypeCode)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ categoryCodes }),
  })
  if (!response.ok) throw new Error('document_type_categories_save_failed')
  return (await response.json()) as DocumentTypeCategoryLinks
}
