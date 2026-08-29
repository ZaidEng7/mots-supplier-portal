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
    const b = body as { error?: string } | null
    super(b?.error ?? `Request failed: ${status}`)
    this.status = status
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new DocumentApiError(res.status, body)
  return body as T
}

export async function listOwnDocuments(): Promise<DocumentTypeStatus[]> {
  const res = await apiFetch('/api/v1/suppliers/me/documents')
  return parseOrThrow(res)
}

export async function uploadDocument(
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

  const res = await apiFetch('/api/v1/suppliers/me/documents', { method: 'POST', body: form })
  return parseOrThrow(res)
}

export async function getDocumentDownloadUrl(documentId: string): Promise<string> {
  const res = await apiFetch(`/api/v1/documents/${documentId}/download-url`)
  const body = await parseOrThrow<{ url: string }>(res)
  return body.url
}

export async function approveDocument(documentId: string): Promise<SupplierDocument> {
  const res = await apiFetch(`/api/v1/documents/${documentId}/approve`, { method: 'POST' })
  return parseOrThrow(res)
}

export async function rejectDocument(documentId: string, reason: string): Promise<SupplierDocument> {
  const res = await apiFetch(`/api/v1/documents/${documentId}/reject`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  })
  return parseOrThrow(res)
}

// MSP-94 CI PROBE - reverted in the next commit.
const ciProbe: number = "this is a string, not a number"
export { ciProbe }
