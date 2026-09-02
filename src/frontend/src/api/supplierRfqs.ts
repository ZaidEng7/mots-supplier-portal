import { apiFetch } from './auth'
import type { ListEnvelope } from './listEnvelope'
import type { InvitationStatus, RfqItem, Requirement, RfqAttachment, RfqState, ClarificationVisibility, Addendum } from './rfqs'

/** FEAT-10.3/FR-CLR-003: the supplier-facing shape - deliberately carries no asker identity at
 * all, not even for a PublishedToAll item asked by someone else. IsMine is the only signal about
 * authorship, computed server-side. */
export interface SupplierClarification {
  id: string
  question: string
  answer: string | null
  visibility: ClarificationVisibility
  askedAt: string
  answeredAt: string | null
  isMine: boolean
}

/** FEAT-08.6/FR-INV-006: the supplier-facing shape - deliberately narrower than the buyer's Rfq
 * (no Approvals, no OrganizationId) since a non-invited supplier must never even learn the RFQ
 * exists, let alone see internal reviewer state. */
/** The supplier list row - projected, with the caller's own invitation status resolved in SQL. */
export interface SupplierRfqListItem {
  referenceCode: string
  titleAr: string
  titleEn: string
  state: string
  myInvitationStatus: InvitationStatus
  createdAt: string
}

export interface SupplierRfq {
  referenceCode: string
  titleAr: string
  titleEn: string
  descriptionAr: string | null
  descriptionEn: string | null
  currencyCode: string
  state: RfqState
  submissionOpensAt: string | null
  submissionClosesAt: string | null
  clarificationDeadlineAt: string | null
  items: RfqItem[]
  requirements: Requirement[]
  attachments: RfqAttachment[]
  myInvitationStatus: InvitationStatus
  clarifications: SupplierClarification[]
  addenda: Addendum[]
}

export class SupplierRfqApiError extends Error {
  status: number
  constructor(status: number, body: unknown) {
    const b = body as { error?: string; message?: string } | null
    super(b?.message ?? b?.error ?? `Request failed: ${status}`)
    this.status = status
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierRfqApiError(res.status, body)
  return body as T
}

export async function listInvitedRfqs(cursor?: string | null): Promise<ListEnvelope<SupplierRfqListItem>> {
  const qs = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''
  return parseOrThrow(await apiFetch(`/api/v1/suppliers/me/rfqs${qs}`))
}

export async function getInvitedRfq(referenceCode: string): Promise<SupplierRfq> {
  return parseOrThrow(await apiFetch(`/api/v1/suppliers/me/rfqs/${referenceCode}`))
}

export async function declineInvitation(referenceCode: string, reason: string | null): Promise<SupplierRfq> {
  return parseOrThrow(await apiFetch(`/api/v1/suppliers/me/rfqs/${referenceCode}/decline`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  }))
}

export async function postClarification(referenceCode: string, question: string): Promise<SupplierRfq> {
  return parseOrThrow(await apiFetch(`/api/v1/suppliers/me/rfqs/${referenceCode}/clarifications`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question }),
  }))
}
