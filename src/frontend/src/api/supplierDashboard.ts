import { apiFetch } from './auth'
import { ProblemError } from './problem'

/** SCR-120's KPI row (SCREEN-SPECIFICATIONS.md §1). */
export interface SupplierKpis {
  openInvitations: number
  draftProposals: number
  submittedProposals: number
  documentsNeedingAttention: number
}

/** §1's action-required strip, as counts so a chip can say how many. */
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

/**
 * T-039/FEAT-16.3: one resolved bid, won or lost.
 *
 * The value is the supplier's own priced total, so nothing here crosses the two-envelope line - it is
 * the number they typed. Null when the bid was never priced.
 */
export interface DashboardAward {
  rfqReferenceCode: string
  rfqTitleAr: string
  rfqTitleEn: string
  proposalCode: string
  /** The proposal's own state, so this chip and the one on the bid say the same word. */
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
  /** The document's own name, both languages. The CODE was reaching the screen: a supplier saw
   *  "commercial_registration" on the one line telling them what to do next. */
  nextRequiredDocumentNameAr: string | null
  nextRequiredDocumentNameEn: string | null
}

export interface SupplierDashboard {
  supplierReferenceCode: string
  displayNameAr: string
  displayNameEn: string
  onboardingState: string
  lifecycleState: string
  /** §1's not-yet-approved branch: a different screen, not this one with empty widgets. */
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
