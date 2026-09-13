// SCR-120: the supplier's own dashboard.
//
// The KPI row is SCREEN-SPECIFICATIONS.md §1's, and the action-required strip is §1's too, carried as counts so a
// chip can say how many.
//
// An award row is T-039 and FEAT-16.3's: one resolved bid, won or lost. Its value is the supplier's own priced
// total, so nothing here crosses the two-envelope line - it is the number they typed - and it is null when the bid
// was never priced. It also carries the proposal's own state, so this chip and the one on the bid say the same
// word.
//
// A document on the profile-health list carries its own name in both languages. The CODE was reaching the screen,
// so a supplier saw "commercial_registration" on the one line telling them what to do next.
//
// The not-yet-approved flag is §1's own branch: that is a different screen, not this one with empty widgets.

import { apiFetch } from './auth'
import { ProblemError } from './problem'

export interface SupplierKpis {
  openInvitations: number
  draftProposals: number
  submittedProposals: number
  documentsNeedingAttention: number
}

export interface ActionRequired {
  expiringDocuments: number
  rejectedDocuments: number
  invitationsClosingSoon: number
  clarificationsAnswered: number
  awardOffers: number
}

export interface DashboardInvitation {
  rfqReferenceCode: string
  titleAr: string
  titleEn: string
  invitationStatus: string
  submissionClosesAt: string | null
}

export interface DashboardProposal {
  proposalReferenceCode: string
  rfqReferenceCode: string
  titleAr: string
  titleEn: string
  state: string
  validityEnd: string | null
}

export interface DashboardAward {
  rfqReferenceCode: string
  rfqTitleAr: string
  rfqTitleEn: string
  proposalCode: string
  outcome: string
  decidedAt: string | null
  value: number | null
  currencyCode: string | null
}

export interface ProfileHealth {
  completeness: number
  requiredDocumentsTotal: number
  requiredDocumentsSupplied: number
  nextRequiredDocumentTypeCode: string | null
  nextRequiredDocumentNameAr: string | null
  nextRequiredDocumentNameEn: string | null
}

export interface SupplierDashboard {
  supplierReferenceCode: string
  displayNameAr: string
  displayNameEn: string
  onboardingState: string
  lifecycleState: string
  isApproved: boolean
  kpis: SupplierKpis
  actionRequired: ActionRequired
  invitations: DashboardInvitation[]
  proposals: DashboardProposal[]
  profileHealth: ProfileHealth
  erpDegraded: boolean
  awards: DashboardAward[]
}

export class SupplierDashboardApiError extends ProblemError {
  constructor(status: number, body: unknown) {
    super(status, body)
  }
}

export async function getSupplierDashboard(): Promise<SupplierDashboard> {
  const response = await apiFetch('/api/v1/suppliers/me/dashboard')
  if (!response.ok) throw new SupplierDashboardApiError(response.status, await response.json().catch(() => null))
  return (await response.json()) as SupplierDashboard
}
