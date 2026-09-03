import { apiFetch } from './auth'
import { problemMessage, type ProblemDetails } from './problem'

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

export interface ProfileHealth {
  completeness: number
  requiredDocumentsTotal: number
  requiredDocumentsSupplied: number
  nextRequiredDocumentTypeCode: string | null
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
}

export class SupplierDashboardApiError extends Error {
  status: number
  constructor(status: number, body: unknown) {
    super(problemMessage(body as ProblemDetails | null, `Request failed: ${status}`))
    this.status = status
  }
}

export async function getSupplierDashboard(): Promise<SupplierDashboard> {
  const response = await apiFetch('/api/v1/suppliers/me/dashboard')
  if (!response.ok) throw new SupplierDashboardApiError(response.status, await response.json().catch(() => null))
  return (await response.json()) as SupplierDashboard
}
