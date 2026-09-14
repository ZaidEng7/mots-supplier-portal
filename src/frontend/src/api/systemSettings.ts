// FR-ADM-006 and SCR-724: the system settings an administrator may change.
//
// Each item carries its own rules, so the screen renders the right control and refuses the wrong value without
// keeping a second copy of the catalogue. isSet is false when no row exists, meaning what is shown is the
// deployment's configuration or the built-in default: "nobody has decided" and "an administrator chose this" are
// different facts.
//
// A refusal is machine-readable - value_not_allowed, value_out_of_range, value_has_duplicates, value_required,
// reference_code_not_active - so the screen can say which rule was broken rather than "invalid".
//
// getPublicSettings is the allow-listed public subset, and it is unauthenticated because the registration form
// itself has to know whether it should be offered.

import { apiFetch } from './auth'

export interface SystemSetting {
  key: string
  kind: 'Choice' | 'Integer' | 'IntegerList' | 'ReferenceCode'
  value: string
  defaultValue: string
  isOverridden: boolean
  updatedAt: string | null
  allowedValues: string[] | null
  minimum: number | null
  maximum: number | null
}

export async function getSystemSettings(): Promise<SystemSetting[]> {
  const response = await apiFetch('/api/v1/admin/settings')
  if (!response.ok) throw new Error('settings_unavailable')
  return (await response.json()) as SystemSetting[]
}

export async function updateSystemSetting(key: string, value: string): Promise<SystemSetting> {
  const response = await apiFetch(`/api/v1/admin/settings/${encodeURIComponent(key)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ value }),
  })
  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as { reason?: string }
    throw Object.assign(new Error('setting_update_failed'), { reason: body.reason ?? 'unknown' })
  }
  return (await response.json()) as SystemSetting
}

export async function getPublicSettings(): Promise<Record<string, string>> {
  const response = await apiFetch('/api/v1/reference/settings')
  if (!response.ok) throw new Error('public_settings_unavailable')
  return (await response.json()) as Record<string, string>
}
