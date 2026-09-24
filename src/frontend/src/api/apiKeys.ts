// The credentials other systems authenticate with.
//
// THE SECRET APPEARS IN ONE PLACE IN THIS WHOLE PRODUCT: the response to createApiKey. There is no route that
// reads it back, because the server stores a hash, so the value the screen receives here is the only copy that
// will ever exist. That is why the create result has its own type - a summary and a secret - rather than the
// secret being an optional field on the summary a listing also returns.
//
// REVOKE IS A POST rather than a DELETE, because the key row survives revocation: the audit trail names keys by
// prefix and needs them to keep resolving.

import { apiFetch } from './auth'

export interface ApiKeySummary {
  id: string
  name: string
  prefix: string
  permissions: string[]
  createdAt: string
  expiresAt: string
  lastUsedAt: string | null
  revokedAt: string | null
}

export interface CreatedApiKey {
  key: ApiKeySummary
  secret: string
}

export async function getApiKeys(): Promise<ApiKeySummary[]> {
  const response = await apiFetch('/api/v1/admin/api-keys')
  if (!response.ok) throw new Error('api_keys_unavailable')
  return (await response.json()) as ApiKeySummary[]
}

export async function createApiKey(name: string, lifetimeDays?: number): Promise<CreatedApiKey> {
  const response = await apiFetch('/api/v1/admin/api-keys', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, lifetimeDays: lifetimeDays ?? null }),
  })
  if (!response.ok) throw new Error('api_key_not_created')
  return (await response.json()) as CreatedApiKey
}

export async function revokeApiKey(id: string): Promise<ApiKeySummary> {
  const response = await apiFetch(`/api/v1/admin/api-keys/${encodeURIComponent(id)}/revoke`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({}),
  })
  if (!response.ok) throw new Error('api_key_not_revoked')
  return (await response.json()) as ApiKeySummary
}
