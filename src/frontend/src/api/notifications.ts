import { apiFetch } from './auth'
import { problemMessage, type ProblemDetails } from './problem'
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

export class NotificationApiError extends Error {
  status: number
  constructor(status: number, body: unknown) {
    super(problemMessage(body as ProblemDetails | null, `Request failed: ${status}`))
    this.status = status
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
