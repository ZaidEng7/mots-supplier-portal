import { apiFetch } from './auth'

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
