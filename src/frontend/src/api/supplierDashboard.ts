import { apiFetch } from './auth'
import { hasProblemProse, problemMessage, type ProblemDetails } from './problem'

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
}

export class SupplierDashboardApiError extends Error {
  status: number
  /** Read by `errorDetail`: this message is the server's own prose, not a bug's. False when the
   * problem document carried no `title` and no `detail`, because `problemMessage` then falls back to
   * "Request failed: <status>", which is developer text and must not reach a reader. */
  isProblemError: boolean
  constructor(status: number, body: unknown) {
    super(problemMessage(body as ProblemDetails | null, `Request failed: ${status}`))
    this.isProblemError = hasProblemProse(body as ProblemDetails | null)
    this.status = status
  }
}

export async function getSupplierDashboard(): Promise<SupplierDashboard> {
  const response = await apiFetch('/api/v1/suppliers/me/dashboard')
  if (!response.ok) throw new SupplierDashboardApiError(response.status, await response.json().catch(() => null))
  return (await response.json()) as SupplierDashboard
}
