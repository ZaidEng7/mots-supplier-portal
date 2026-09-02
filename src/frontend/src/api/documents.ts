import { apiFetch } from './auth'

export interface SupplierDocument {
  id: string
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
    const b = body as { error?: string; message?: string } | null
    super(b?.message ?? b?.error ?? `Request failed: ${status}`)
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
