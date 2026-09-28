// The systems this product talks to, and where to find them.
//
// THERE IS NO WAY TO READ A SECRET HERE, and that is deliberate rather than an oversight: the server has no route
// that returns one. The view says whether a secret is set and when it changed, which is what somebody needs to
// decide whether to replace it.
//
// SENDING null FOR THE SECRET MEANS "KEEP THE ONE YOU HAVE". The form cannot prefill a value it is never given,
// so an untouched field is empty, and sending that as an empty string would clear the credential every time
// somebody corrected the address.

import { apiFetch } from './auth'

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
}

export interface IntegrationTestResult {
  succeeded: boolean
  detail: string
}

export async function getIntegrations(): Promise<Integration[]> {
  const response = await apiFetch('/api/v1/admin/integrations')
  if (!response.ok) throw new Error('integrations_unavailable')
  return (await response.json()) as Integration[]
}

export async function updateIntegration(
  key: string,
  values: { baseUrl: string; apiKey: string; apiSecret: string | null; isEnabled: boolean },
): Promise<Integration> {
  const response = await apiFetch(`/api/v1/admin/integrations/${encodeURIComponent(key)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(values),
  })

  if (!response.ok) throw new Error('integration_not_saved')
  return (await response.json()) as Integration
}

export async function testIntegration(key: string): Promise<IntegrationTestResult> {
  const response = await apiFetch(`/api/v1/admin/integrations/${encodeURIComponent(key)}/test`, {
    method: 'POST',
  })

  if (!response.ok) throw new Error('integration_not_tested')
  return (await response.json()) as IntegrationTestResult
}
