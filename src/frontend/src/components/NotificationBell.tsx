import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { unreadNotificationCount } from '../api/notifications'
import { formatNumber } from '../lib/datetime'

/**
 * INFORMATION-ARCHITECTURE.md §2: "Notifications bell | inline-end | Unread count badge; opens a
 * panel grouped by *Actionable* / *Informational*; deep-links to the source entity; full history at
 * `…/notifications`".
 *
 * <p><b>This is the badge and the link, not the panel.</b> §2 describes a panel grouped into
 * Actionable and Informational - and nothing in the documents says which notification types fall
 * into which group. Inventing that split would be inventing product policy in a component, so the
 * bell links to SCR-900, where the full history already lives, and the grouping is reported as an
 * open question. The badge itself is what §2 makes load-bearing: it is how anyone knows to look.</p>
 *
 * <p>The count is a count, not a list length: the badge is on every page of the app for every
 * persona, and shipping rows to render a number only becomes visibly wrong once there are many.</p>
 */
export function NotificationBell({ to = '/notifications' }: { to?: string }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'

  const query = useQuery({
    queryKey: ['notifications', 'unread-count'],
    queryFn: unreadNotificationCount,
    // Polling rather than a socket: EPIC-15 has no realtime channel, and a stale badge is a much
    // smaller problem than a connection this app does not otherwise need.
    refetchInterval: 60_000,
  })

  const count = query.data ?? 0

  return (
    <Link
      to={to}
      className="relative inline-flex items-center"
      aria-label={count > 0
        ? t('notifications.bellWithCount', { count })
        : t('notifications.bell')}
    >
      <span aria-hidden="true">🔔</span>
      {count > 0 ? (
        <span
          aria-hidden="true"
          className="ms-1 rounded-full px-1.5 text-[length:var(--text-body-sm)]"
          style={{ background: 'var(--color-danger)', color: 'var(--color-on-danger, #fff)' }}
        >
          {formatNumber(count, locale, 0)}
        </span>
      ) : null}
    </Link>
  )
}
