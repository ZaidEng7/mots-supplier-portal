import { apiFetch } from './auth'
import { ProblemError } from './problem'
import type { ListEnvelope } from './listEnvelope'

/**
 * EPIC-15 / SCR-900. The in-app notification channel.
 *
 * <p>Both languages arrive on every row rather than the caller's one: UX-WRITING §10 requires
 * delivery "bilingual per the user's locale", and the SPA switches language without a round-trip -
 * a server-picked string would be stale the moment someone toggles.</p>
 */
export interface Notification {
  id: string
  type: string
  titleAr: string
  titleEn: string
  bodyAr: string
  bodyEn: string
  /** BRULE-091: identifiers and routes only - never content. */
  data: string
  createdAt: string
  readAt: string | null
  isRead: boolean
}

export class NotificationApiError extends ProblemError {
  constructor(status: number, body: unknown) {
    super(status, body)
  }
}

async function parseOrThrow<T>(response: Response): Promise<T> {
  if (!response.ok) throw new NotificationApiError(response.status, await response.json().catch(() => null))
  return (await response.json()) as T
}

export async function listNotifications(cursor?: string, unreadOnly?: boolean): Promise<ListEnvelope<Notification>> {
  const params = new URLSearchParams()
  if (cursor) params.set('cursor', cursor)
  if (unreadOnly) params.set('unreadOnly', 'true')
  const query = params.toString()

  return parseOrThrow(await apiFetch(`/api/v1/notifications${query ? `?${query}` : ''}`))
}

export async function unreadNotificationCount(): Promise<number> {
  const body = await parseOrThrow<{ count: number }>(await apiFetch('/api/v1/notifications/unread-count'))
  return body.count
}

export async function markNotificationRead(notificationId: string): Promise<Notification> {
  return parseOrThrow(await apiFetch(`/api/v1/notifications/${notificationId}/read`, { method: 'POST' }))
}

export async function markAllNotificationsRead(): Promise<number> {
  const body = await parseOrThrow<{ marked: number }>(await apiFetch('/api/v1/notifications/read-all', { method: 'POST' }))
  return body.marked
}

/**
 * SCR-901/FR-NOT-004, under D-60: which notifications a user may switch off, and which always arrive.
 *
 * <p>The response carries all 32 types, including the ones that cannot be muted — saying what a user will be
 * told REGARDLESS is half of what the screen is for. `muteable` is the classification's answer, not this
 * user's: an actionable type is always `muteable: false, muted: false`.</p>
 */
export interface NotificationPreference {
  type: string
  muteable: boolean
  muted: boolean
  /** The notification's own title from the copy catalogue, including an administrator's rewording. Served
   * rather than duplicated as 32 more interface strings — the words a user recognises are the words they
   * were sent, and a second copy here would drift the moment somebody reworded a template. */
  titleAr: string
  titleEn: string
}

export interface NotificationPreferences {
  types: NotificationPreference[]
}

export async function getNotificationPreferences(): Promise<NotificationPreferences> {
  const res = await apiFetch('/api/v1/notifications/preferences')
  return parseOrThrow(res)
}

/**
 * Sends the WHOLE muted set, replacing what was stored.
 *
 * <p>One request rather than a toggle per type: what a user decides here is "these are the ones I do not
 * want", one decision — and leaving a type out is how it is switched back on. The server refuses an
 * actionable type with `NOTIFICATION_NOT_MUTEABLE` rather than silently dropping it.</p>
 */
export async function setNotificationPreferences(mutedTypes: string[]): Promise<NotificationPreferences> {
  const res = await apiFetch('/api/v1/notifications/preferences', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ mutedTypes }),
  })
  return parseOrThrow(res)
}
