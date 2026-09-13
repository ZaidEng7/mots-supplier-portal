// The supplier's side of a tender: the invitations they hold, the one they are reading, declining one, and asking
// a clarification.
//
// SupplierRfq is FEAT-08.6 and FR-INV-006's supplier-facing shape, deliberately narrower than the buyer's Rfq -
// no approvals, no organization id - because a non-invited supplier must never even learn the RFQ exists, let
// alone see internal reviewer state. The list row is projected, with the caller's own invitation status resolved
// in SQL. R-9: §12.4's names are authoritative, so the row reads rfqCode, invitationStatus and
// submissionDeadline; titleAr and titleEn stay split, because the document's single `title` is a bilingual
// collapse rather than a rename.
//
// SupplierClarification is FEAT-10.3 and FR-CLR-003's supplier-facing shape, and it deliberately carries no asker
// identity at all - not even for a PublishedToAll item asked by someone else. isMine is the only signal about
// authorship, computed server-side.
//
// The deadline-change reason is A-6's, read here rather than in the notification: BRULE-091 keeps content out of
// notification payloads, so the message points at the RFQ and the reason waits here.

import { ProblemError } from './problem'
import { apiFetch } from './auth'
import type { ListEnvelope } from './listEnvelope'
import type { InvitationStatus, RfqItem, Requirement, RfqAttachment, RfqState, ClarificationVisibility, Addendum } from './rfqs'

export interface SupplierClarification {
  id: string
  question: string
  answer: string | null
  visibility: ClarificationVisibility
  askedAt: string
  answeredAt: string | null
  isMine: boolean
}

export interface SupplierRfqListItem {
  rfqCode: string
  titleAr: string
  titleEn: string
  state: string
  invitationStatus: InvitationStatus
  createdAt: string
  submissionDeadline: string | null
}

export interface SupplierRfq {
  rfqCode: string
  titleAr: string
  titleEn: string
  descriptionAr: string | null
  descriptionEn: string | null
  currencyCode: string
  state: RfqState
  submissionOpensAt: string | null
  submissionDeadline: string | null
  clarificationDeadlineAt: string | null
  items: RfqItem[]
  requirements: Requirement[]
  attachments: RfqAttachment[]
  invitationStatus: InvitationStatus
  clarifications: SupplierClarification[]
  addenda: Addendum[]
  submissionDeadlineChangeReason: string | null
  submissionDeadlineChangedAt: string | null
}

export class SupplierRfqApiError extends ProblemError {
  constructor(status: number, body: unknown) {
    super(status, body)
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
  return parseOrThrow(await apiFetch(`/api/v1/rfqs${qs}`))
}

export async function getInvitedRfq(referenceCode: string): Promise<SupplierRfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}`))
}

export async function declineInvitation(referenceCode: string, reason: string | null): Promise<SupplierRfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/invitations/decline`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  }))
}

export async function postClarification(referenceCode: string, question: string): Promise<SupplierRfq> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${referenceCode}/clarifications`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question }),
  }))
}
