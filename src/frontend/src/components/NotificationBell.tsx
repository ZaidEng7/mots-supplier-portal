import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Bell } from 'lucide-react'
import { unreadNotificationCount } from '../api/notifications'
import { formatNumber } from '../lib/datetime'

/**
 * INFORMATION-ARCHITECTURE.md §2: "Notifications bell | inline-end | Unread count badge; opens a
 * panel grouped by *Actionable* / *Informational*; deep-links to the source entity; full history at
 * `…/notifications`".
 *
 * <p><b>This is the badge and the link, not the panel.</b> §2 describes a panel; the bell opens
 * SCR-900, where the full history lives and where - since T-037 - the Actionable and Informational
 * grouping §2 asks for is rendered. A panel here would be a second copy of that screen hanging off a
 * 24px target, and the badge is what §2 makes load-bearing: it is how anyone knows to look.</p>
 *
 * <p>This comment used to say the grouping could not be built because nothing classified the
 * notification types. D-60 then classified all of them, for a different reason - deciding which a
 * person may switch off - and the refusal outlived its reason by two batches. The classification is
 * on the wire now, so the screen reads it rather than inventing one.</p>
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
      // WCAG 2.5.8 Target Size (Minimum), which axe checks under `wcag22aa`. The icon is 18px; the
      // link is padded out to a 24x24 target so the thing you click is bigger than the thing you see.
      // The emoji this replaced happened to render larger in the reader's system font, so the target
      // was adequate by accident and stopped being adequate the moment it became a real icon.
      className="relative inline-flex min-h-6 min-w-6 items-center justify-center"
      aria-label={count > 0
        ? t('notifications.bellWithCount', { count })
        : t('notifications.bell')}
    >
      {/* An emoji renders in the reader's system font, at their system's idea of the size, in a
          palette this product does not control - and it was the one dated marker the design audit found
          in an otherwise trend-free interface. lucide is what every other icon here uses. */}
      <Bell size={18} aria-hidden="true" />
      {count > 0 ? (
        <span
          aria-hidden="true"
          className="ms-1 rounded-full px-2 text-[length:var(--text-body-sm)]"
          style={{ background: 'var(--color-danger-solid)', color: 'var(--color-text-inverse)' }}
        >
          {formatNumber(count, locale, 0)}
        </span>
      ) : null}
    </Link>
  )
}
