import { apiFetch } from './auth'

/** FR-ADM-007/SCR-715. The shipped words travel with the current ones so the screen can show what an
 * override replaced and offer a revert that is not guesswork. */
export interface NotificationTemplate {
  type: string
  titleAr: string
  titleEn: string
  bodyAr: string
  bodyEn: string
  shippedTitleAr: string
  shippedTitleEn: string
  shippedBodyAr: string
  shippedBodyEn: string
  isOverridden: boolean
  updatedAt: string | null
  /** Tokens this type's payload can fill. A template may use any subset and no others. */
  availableTokens: string[]
}

export async function listNotificationTemplates(): Promise<NotificationTemplate[]> {
  const response = await apiFetch('/api/v1/admin/notification-templates')
  if (!response.ok) throw new Error('templates_unavailable')
  return (await response.json()) as NotificationTemplate[]
}

export interface NotificationTemplateDraft {
  titleAr: string
  titleEn: string
  bodyAr: string
  bodyEn: string
}

export async function updateNotificationTemplate(
  type: string,
  draft: NotificationTemplateDraft,
): Promise<NotificationTemplate> {
  const response = await apiFetch(`/api/v1/admin/notification-templates/${encodeURIComponent(type)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(draft),
  })
  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as { tokens?: string[] }
    throw Object.assign(new Error('template_update_failed'), { tokens: body.tokens ?? [] })
  }
  return (await response.json()) as NotificationTemplate
}

/** Removes the override, restoring the shipped copy. */
export async function revertNotificationTemplate(type: string): Promise<NotificationTemplate> {
  const response = await apiFetch(`/api/v1/admin/notification-templates/${encodeURIComponent(type)}`, {
    method: 'DELETE',
  })
  if (!response.ok) throw new Error('template_revert_failed')
  return (await response.json()) as NotificationTemplate
}
