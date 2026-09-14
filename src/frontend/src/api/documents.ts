// A supplier's documents: the checklist, the upload, the download link, a reviewer's decision, and one type's
// history.
//
// The document identifier is the public code, DOC-2026-000001, and it is the ONLY identifier the API accepts. It
// is named documentId because that is what the server sends: T-010 took internal GUIDs out of payloads, and R-9
// then settled the spelling as documentId across both document DTOs. This interface said `id`, so every
// latestDocument.id read undefined and every call built from it addressed /api/v1/documents/undefined/... -
// download, approve and reject alike, on three screens. TypeScript could not catch it, because the type was
// simply wrong about the wire.
//
// DocumentApiError takes ProblemError's preference for the human-readable explanation - why an expiry date was
// rejected, say - over the short machine code. Preferring the code left every validation failure showing the same
// opaque string, "invalid_expiry", regardless of which of several distinct rules actually failed.
//
// listOwnDocuments is addressed by supplier code per §12-A/C3 and §12.3's GET /suppliers/{supplierCode}/documents.
// The server still answers a supplier with their own checklist and a reviewer with §12.3's paged document list,
// decided by the caller's scope; this is the supplier's own view.
//
// getDocumentHistory is SCR-132: every version of one document type, newest first. It is keyed by TYPE code
// rather than by a document id, because the history is the type's story - "what happened to my commercial
// registration" - rather than one file's. An empty array is a real answer, meaning nothing uploaded yet, and is
// not the same as a 404, which means no such type.

import { ProblemError } from './problem'
import { apiFetch } from './auth'

export interface SupplierDocument {
  documentId: string
  version: number
  state: string
  originalFileName: string
  contentType: string
  sizeBytes: number
  issueDate: string | null
  expiryDate: string | null
  rejectReason: string | null
  uploadedAt: string
  reviewedAt: string | null
}

export interface DocumentTypeStatus {
  documentTypeId: string
  code: string
  nameAr: string
  nameEn: string
  isRequired: boolean
  expiryTracked: boolean
  latestDocument: SupplierDocument | null
}

export class DocumentApiError extends ProblemError {
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new DocumentApiError(res.status, body)
  return body as T
}

export async function listOwnDocuments(supplierCode: string): Promise<DocumentTypeStatus[]> {
  const res = await apiFetch(`/api/v1/suppliers/${supplierCode}/documents`)
  return parseOrThrow(res)
}

export async function uploadDocument(
  supplierCode: string,
  documentTypeId: string,
  file: File,
  issueDate?: string,
  expiryDate?: string,
): Promise<SupplierDocument> {
  const form = new FormData()
  form.append('documentTypeId', documentTypeId)
  form.append('file', file)
  if (issueDate) form.append('issueDate', issueDate)
  if (expiryDate) form.append('expiryDate', expiryDate)

  const res = await apiFetch(`/api/v1/suppliers/${supplierCode}/documents`, { method: 'POST', body: form })
  return parseOrThrow(res)
}

export async function getDocumentDownloadUrl(documentId: string): Promise<string> {
  const res = await apiFetch(`/api/v1/documents/${documentId}/download-url`)
  const body = await parseOrThrow<{ url: string }>(res)
  return body.url
}

export async function approveDocument(supplierCode: string, documentId: string): Promise<SupplierDocument> {
  const res = await apiFetch(`/api/v1/suppliers/${supplierCode}/documents/${documentId}/approve`, { method: 'POST' })
  return parseOrThrow(res)
}

export async function rejectDocument(supplierCode: string, documentId: string, reason: string): Promise<SupplierDocument> {
  const res = await apiFetch(`/api/v1/suppliers/${supplierCode}/documents/${documentId}/reject`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  })
  return parseOrThrow(res)
}

export async function getDocumentHistory(supplierCode: string, documentTypeCode: string): Promise<SupplierDocument[]> {
  return parseOrThrow(await apiFetch(
    `/api/v1/suppliers/${supplierCode}/documents/types/${encodeURIComponent(documentTypeCode)}/history`))
}
