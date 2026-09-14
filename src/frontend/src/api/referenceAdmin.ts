// T-080 with SCR-710 through 712: the five reference tables an administrator may edit.
//
// The table list is the same one the server accepts, so a typo is a refusal rather than a silent no-op against
// the wrong table.
//
// Two flags live only on document types. The required flag is null on every other table rather than false,
// because "this table has no such flag" and "this row has the flag off" are different facts. The award-critical
// flag is BRULE-023's and is null everywhere but document-types: expiry of an award-critical document suspends
// the supplier, and this flag is the only thing that decides which types those are. On an update both are omitted
// to leave the stored value alone - the server only writes what is sent, so editing a name cannot clear the one
// flag on this screen that suspends live suppliers.
//
// Inactive rows are hidden by default and reachable by asking, because an admin editing the catalogue needs to see
// what they deactivated - otherwise deactivation reads as deletion and the next administrator recreates the code,
// which is D-28's whole point. The CODE cannot change: it is the foreign key in every live row that points at
// this item, with no cascade (D-28), so only the names and the document-type flags are editable. And there is no
// delete endpoint to call - deactivate, never delete.
//
// The document-type category links are BRULE-016's: which categories a document type is required for, read by
// every gate since D-59. An empty array means no links are recorded, which is NOT the same as "required for
// nothing" - see the endpoint. They are written as the whole set, because "required for these categories" is one
// decision rather than a sequence of clicks.

import { apiFetch } from './auth'
import { SupplierApiError } from './supplier'

export const REFERENCE_TABLES = ['categories', 'document-types', 'currencies', 'units-of-measure', 'regions'] as const
export type ReferenceTable = (typeof REFERENCE_TABLES)[number]

export interface ReferenceItem {
  code: string
  nameAr: string
  nameEn: string
  isActive: boolean
  isRequired: boolean | null
  expiryTracked: boolean | null
  isAwardCritical: boolean | null
}

export interface ReferenceItemPayload {
  nameAr: string
  nameEn: string
  isRequired?: boolean | null
  expiryTracked?: boolean | null
  isAwardCritical?: boolean | null
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

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

export async function updateReferenceItem(table: ReferenceTable, code: string, payload: ReferenceItemPayload): Promise<ReferenceItem> {
  return parseOrThrow(await apiFetch(`/api/v1/admin/reference/${table}/${encodeURIComponent(code)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }))
}

export async function setReferenceItemActive(table: ReferenceTable, code: string, isActive: boolean): Promise<ReferenceItem> {
  const action = isActive ? 'reactivate' : 'deactivate'
  return parseOrThrow(await apiFetch(`/api/v1/admin/reference/${table}/${encodeURIComponent(code)}/${action}`, { method: 'POST' }))
}

export interface DocumentTypeCategoryLinks {
  documentTypeCode: string
  categoryCodes: string[]
}

export async function getDocumentTypeCategories(): Promise<DocumentTypeCategoryLinks[]> {
  const response = await apiFetch('/api/v1/admin/document-type-categories')
  if (!response.ok) throw new Error('document_type_categories_unavailable')
  return (await response.json()) as DocumentTypeCategoryLinks[]
}

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
