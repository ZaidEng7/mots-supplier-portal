import { problemMessage, type ProblemDetails } from './problem'
import { apiFetch } from './auth'

export interface SupplierDocument {
  /**
   * The public document code (DOC-2026-000001), and the ONLY identifier the API accepts.
   *
   * Named `documentId` because that is what the server sends: T-010 took internal GUIDs out of
   * payloads, and R-9 then settled the spelling as `documentId` across both document DTOs. This
   * interface said `id`, so every `latestDocument.id` read `undefined` and every call built from it
   * addressed `/api/v1/documents/undefined/...` - download, approve and reject alike, on three
   * screens. TypeScript could not catch it: the type was simply wrong about the wire.
   */
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

export class DocumentApiError extends Error {
  status: number
  constructor(status: number, body: unknown) {
    // `message` is the human-readable explanation (e.g. why an expiry date was rejected);
    // `error` is just the short machine code. Preferring the code left every validation
    // failure showing the same opaque string ("invalid_expiry") regardless of which of several
    // distinct rules actually failed.
    const b = body as ProblemDetails | null
    super(problemMessage(b, `Request failed: ${status}`))
    this.status = status
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new DocumentApiError(res.status, body)
  return body as T
}

/**
 * §12-A/C3: addressed by supplier code now (§12.3 `GET /suppliers/{supplierCode}/documents`).
 * The server still answers a supplier with their own checklist and a reviewer with §12.3's paged
 * document list, decided by the caller's scope - this is the supplier's own view.
 */
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

/**
 * SCR-132: every version of one document type, newest first.
 *
 * <p>Keyed by TYPE code, not by a document id: the history is the type's story — "what happened to
 * my commercial registration" — rather than one file's. An empty array is a real answer (nothing
 * uploaded yet) and is not the same as a 404 (no such type).</p>
 */
export async function getDocumentHistory(supplierCode: string, documentTypeCode: string): Promise<SupplierDocument[]> {
  return parseOrThrow(await apiFetch(
    `/api/v1/suppliers/${supplierCode}/documents/types/${encodeURIComponent(documentTypeCode)}/history`))
}
