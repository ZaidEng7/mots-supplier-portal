// The systems this product talks to, and where to find them.
//
// THERE IS NO WAY TO READ A SECRET HERE, and that is deliberate rather than an oversight: the server has no route
// that returns one. The view says whether a secret is set and when it changed, which is what somebody needs to
// decide whether to replace it.
//
// SENDING null FOR THE SECRET MEANS "KEEP THE ONE YOU HAVE". The form cannot prefill a value it is never given,
// so an untouched field is empty, and sending that as an empty string would clear the credential every time
// somebody corrected the address.
//
// THE SUPPLIER WRITES TO THE ERP ride on the same save. createSuppliersInErp is the switch that lets the portal
// create an approved supplier in the ERP, and defaultSupplierGroup the ERP group it is filed under. Both are optional
// on the way in and the server reads a missing one as "leave it as it is", so a caller that sends neither cannot turn
// the writes off by saving an address. suppliersWaitingForErp is the number of approved suppliers the switch would
// send to the ERP once it is on, and it is zero on any connection but the ERP's.
//
// THE ERP'S SUPPLIER GROUPS ARE READ FROM THE ERP when the screen asks, so the list can fail the way the ERP fails:
// 503 when no connection is configured, 502 when the ERP refused or did not answer. Those and a refused save throw
// IntegrationApiError, which carries the server's own sentence - "Creating suppliers in the ERP needs a default ERP
// supplier group", or the ERP's words - because that sentence is what the administrator acts on.

import { apiFetch } from './auth'
import { ProblemError } from './problem'

export interface Integration {
  key: string
  displayName: string
  baseUrl: string
  apiKey: string
  hasSecret: boolean
  secretSetAt: string | null
  isEnabled: boolean
  source: 'Database' | 'Configuration' | 'None'
  updatedAt: string | null
  lastTestedAt: string | null
  lastTestSucceeded: boolean | null
  lastTestDetail: string | null
  lastSyncAt: string | null
  lastSyncOutcome: 'Succeeded' | 'NeedsAttention' | 'Failed' | null
  lastSyncSummary: string | null
  createSuppliersInErp: boolean
  defaultSupplierGroup: string | null
  suppliersWaitingForErp: number
}

export interface IntegrationTestResult {
  succeeded: boolean
  detail: string
}

export interface IntegrationUpdate {
  baseUrl: string
  apiKey: string
  apiSecret: string | null
  isEnabled: boolean
  createSuppliersInErp?: boolean
  defaultSupplierGroup?: string
}

export class IntegrationApiError extends ProblemError {
  constructor(status: number, body: unknown) {
    super(status, body)
  }
}

async function bodyOf(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    return null
  }
}

export async function getIntegrations(): Promise<Integration[]> {
  const response = await apiFetch('/api/v1/admin/integrations')
  if (!response.ok) throw new Error('integrations_unavailable')
  return (await response.json()) as Integration[]
}

export async function updateIntegration(key: string, values: IntegrationUpdate): Promise<Integration> {
  const response = await apiFetch(`/api/v1/admin/integrations/${encodeURIComponent(key)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(values),
  })

  if (!response.ok) throw new IntegrationApiError(response.status, await bodyOf(response))
  return (await response.json()) as Integration
}

export async function testIntegration(key: string): Promise<IntegrationTestResult> {
  const response = await apiFetch(`/api/v1/admin/integrations/${encodeURIComponent(key)}/test`, {
    method: 'POST',
  })

  if (!response.ok) throw new Error('integration_not_tested')
  return (await response.json()) as IntegrationTestResult
}

export async function getErpSupplierGroups(key: string): Promise<string[]> {
  const response = await apiFetch(`/api/v1/admin/integrations/${encodeURIComponent(key)}/supplier-groups`)

  if (!response.ok) throw new IntegrationApiError(response.status, await bodyOf(response))
  return ((await response.json()) as { groups: string[] }).groups
}
