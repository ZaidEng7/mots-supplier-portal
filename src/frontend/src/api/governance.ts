import { apiFetch } from './auth'
import type { ListEnvelope } from './listEnvelope'

export interface GovernanceCount {
  key: string
  count: number
}

/** FR-DSH-005/SCR-600. Every figure is an aggregate: BRULE-086 grants the Ministry cross-organization
 * access to "aggregate/governance metrics only", so nothing on this type identifies a row. */
export interface GovernanceOverview {
  totalSuppliers: number
  suppliersByLifecycleState: GovernanceCount[]
  totalRfqs: number
  rfqsByState: GovernanceCount[]
  totalAwards: number
  averageProposalsPerRfq: number
  /** Null when the commercial-visibility policy flag is off, which is its seeded state (D-6/BRULE-087).
   * Null is not zero - "policy withholds this" and "nothing has been awarded" are different facts. */
  totalAwardedValue: number | null
  commercialValuesVisible: boolean
}

export async function getGovernanceOverview(): Promise<GovernanceOverview> {
  const response = await apiFetch('/api/v1/ministry/overview')
  if (!response.ok) throw new Error('governance_unavailable')
  return (await response.json()) as GovernanceOverview
}

/** SCR-604: one category's coverage. Counts only — BRULE-086's aggregate grant, so nothing here names a
 * supplier or a tender, and no figure is commercial. */
export interface CategoryCoverage {
  categoryCode: string
  nameAr: string
  nameEn: string
  /** Suppliers whose onboarding is Approved and who list this category — the invitation pool. */
  approvedSuppliers: number
  /** Of those, the ones currently able to trade. The two differ exactly when somebody is suspended. */
  activeSuppliers: number
  activeOfferings: number
  /** Tenders that reached the market with at least one line in this category. */
  tenders: number
  awardedTenders: number
}

export interface CategoryCoverageOverview {
  categories: CategoryCoverage[]
  /** Computed server-side: counting empty rows by eye is how a dashboard becomes decoration. */
  categoriesWithNoActiveSupplier: number
  /** MSP-54's category list is flat. SCR-604's inventory row says "tree", and drawing one from a flat list
   * would invent a hierarchy nobody has decided — so the screen says flat instead. */
  categoriesAreFlat: boolean
}

export async function getCategoryCoverage(): Promise<CategoryCoverageOverview> {
  const response = await apiFetch('/api/v1/ministry/categories')
  if (!response.ok) throw new Error('category_coverage_unavailable')
  return (await response.json()) as CategoryCoverageOverview
}

/**
 * SCR-601/602/603/606, under D-66.
 *
 * <p><b>These four reads carry what every other governance read deliberately does not:</b> named tenders,
 * named suppliers, named bidders and their numbers — including on a tender that is still open. D-57 relayed
 * that the Ministry may see commercial figures and required written sign-off first; D-66 records that they
 * shipped without it, at the product owner's direction, at the widest scope offered.</p>
 *
 * <p>Every money field is nullable and is populated only while `GovernanceVisibility.commercialValues` is on.
 * Null is not zero — "policy withholds this" and "nothing was bid" are different facts, and each screen says
 * which one it is showing.</p>
 */
export interface MinistryRfqRow {
  referenceCode: string
  titleAr: string
  titleEn: string
  state: string
  organizationNameAr: string
  organizationNameEn: string
  publishedAt: string | null
  submissionClosesAt: string | null
  invitedSuppliers: number
  submittedProposals: number
  awardedValue: number | null
  currencyCode: string | null
}

export interface MinistrySupplierRow {
  supplierCode: string
  displayNameAr: string
  displayNameEn: string
  onboardingState: string
  lifecycleState: string
  categoryCodes: string[]
  registeredAt: string
  submittedProposals: number
  awardsWon: number
  awardedValue: number | null
}

export interface MinistrySpendBucket {
  key: string
  awards: number
  value: number | null
  /**
   * The bucket's name, where its key is a CODE rather than a word.
   *
   * <p>Null for months, whose key is a date, and for buying bodies, whose key is already a name. Set
   * for categories, which group on `CategoryCode` - so a Ministry reader met `tour_operations` on the
   * axis of a ranked chart chosen precisely because a category name is prose.</p>
   */
  nameAr: string | null
  nameEn: string | null
}

export interface MinistryAwardAnalytics {
  totalAwards: number
  totalAwardedValue: number | null
  byMonth: MinistrySpendBucket[]
  byCategory: MinistrySpendBucket[]
  byOrganization: MinistrySpendBucket[]
  commercialValuesVisible: boolean
}

/** One bid, named. Under a narrower scope this would have been a pseudonym until the award. */
export interface MinistryBid {
  proposalCode: string
  supplierCode: string
  supplierDisplayNameAr: string
  supplierDisplayNameEn: string
  state: string
  submittedAt: string | null
  totalValue: number | null
  isAwarded: boolean
}

export interface MinistryRfqDetail {
  summary: MinistryRfqRow
  descriptionAr: string | null
  descriptionEn: string | null
  items: { titleAr: string, titleEn: string, categoryCode: string, quantity: number, unitOfMeasureCode: string }[]
  bids: MinistryBid[]
  commercialValuesVisible: boolean
}

async function ministryRead<T>(path: string): Promise<T> {
  const response = await apiFetch(path)
  if (!response.ok) throw new Error('ministry_read_unavailable')
  return (await response.json()) as T
}

export function listMinistryRfqs(
  cursor?: string | null,
  filters?: { state?: string | null, q?: string | null },
): Promise<ListEnvelope<MinistryRfqRow>> {
  const params = new URLSearchParams()
  if (cursor) params.set('cursor', cursor)
  if (filters?.state) params.set('state', filters.state)
  if (filters?.q) params.set('q', filters.q)
  const qs = params.toString() ? `?${params.toString()}` : ''
  return ministryRead(`/api/v1/ministry/rfqs${qs}`)
}

export function listMinistrySuppliers(
  cursor?: string | null,
  filters?: { lifecycleState?: string | null, q?: string | null },
): Promise<ListEnvelope<MinistrySupplierRow>> {
  const params = new URLSearchParams()
  if (cursor) params.set('cursor', cursor)
  if (filters?.lifecycleState) params.set('lifecycleState', filters.lifecycleState)
  if (filters?.q) params.set('q', filters.q)
  const qs = params.toString() ? `?${params.toString()}` : ''
  return ministryRead(`/api/v1/ministry/suppliers${qs}`)
}

export function getMinistryAwardAnalytics(): Promise<MinistryAwardAnalytics> {
  return ministryRead('/api/v1/ministry/awards')
}

export function getMinistryRfqDetail(referenceCode: string): Promise<MinistryRfqDetail> {
  return ministryRead(`/api/v1/ministry/rfqs/${encodeURIComponent(referenceCode)}`)
}
