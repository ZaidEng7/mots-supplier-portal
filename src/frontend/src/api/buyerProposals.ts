// T-082 with SCR-430 and SCR-431: the buyer's read of the bids received against their own RFQ.
//
// `visibility` is on the wire deliberately. The screen has to say WHY a figure is missing rather than render a
// blank cell, and the three tiers - Sealed, Technical, Commercial - are a rule the server owns. The client never
// decides what to hide; it only explains what the server withheld. Below the Commercial tier the totals are
// null: absent, never zero.

import { apiFetch } from './auth'

export type BuyerProposalVisibility = 'Sealed' | 'Technical' | 'Commercial'

export interface BuyerProposalListItem {
  proposalId: string
  proposalCode: string
  supplierNameAr: string
  supplierNameEn: string
  state: string
  submittedAt: string | null
  itemCount: number
  documentCount: number
  totalValue: number | null
  currencyCode: string | null
}

export interface BuyerProposalList {
  visibility: BuyerProposalVisibility
  submittedCount: number
  proposals: BuyerProposalListItem[]
}

export interface BuyerProposalItem {
  rfqItemId: string
  titleAr: string
  titleEn: string
  quantity: number
  unitPrice: number | null
  lineTotal: number | null
  leadTimeDays: number | null
  notesAr: string | null
  notesEn: string | null
}

export interface BuyerProposalAnswer {
  requirementId: string
  textAr: string
  textEn: string
  answerAr: string
  answerEn: string
}

export interface BuyerProposalDetail {
  visibility: BuyerProposalVisibility
  proposalId: string
  proposalCode: string
  supplierNameAr: string
  supplierNameEn: string
  state: string
  submittedAt: string | null
  narrativeAr: string | null
  narrativeEn: string | null
  answers: BuyerProposalAnswer[]
  items: BuyerProposalItem[]
  documentCount: number
  totalValue: number | null
  currencyCode: string | null
  paymentTerms: string | null
  incotermCode: string | null
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  if (!res.ok) throw new Error(`received proposals: ${res.status}`)
  return (await res.json()) as T
}

export async function listReceivedProposals(rfqCode: string): Promise<BuyerProposalList> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqCode}/received-proposals`))
}

export async function getReceivedProposal(rfqCode: string, proposalId: string): Promise<BuyerProposalDetail> {
  return parseOrThrow(await apiFetch(`/api/v1/rfqs/${rfqCode}/received-proposals/${proposalId}`))
}
