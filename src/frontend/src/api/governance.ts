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
