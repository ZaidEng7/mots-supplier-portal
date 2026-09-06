import { apiFetch } from './auth'

/** SCR-716. */
export interface UiStringOverride {
  key: string
  language: string
  value: string
  updatedAt: string
}

export async function listUiStringOverrides(): Promise<UiStringOverride[]> {
  const response = await apiFetch('/api/v1/admin/ui-strings/')
  if (!response.ok) throw new Error('ui_strings_unavailable')
  return (await response.json()) as UiStringOverride[]
}

/** The key travels as a path segment because i18n keys contain dots, not slashes. */
export async function upsertUiStringOverride(language: string, key: string, value: string): Promise<UiStringOverride> {
  const response = await apiFetch(`/api/v1/admin/ui-strings/${language}/${key}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ value }),
  })
  if (!response.ok) throw new Error('ui_string_save_failed')
  return (await response.json()) as UiStringOverride
}

/** Removing the override is how a shipped string comes back — there is no separate reset. */
export async function deleteUiStringOverride(language: string, key: string): Promise<void> {
  const response = await apiFetch(`/api/v1/admin/ui-strings/${language}/${key}`, { method: 'DELETE' })
  if (!response.ok) throw new Error('ui_string_delete_failed')
}
