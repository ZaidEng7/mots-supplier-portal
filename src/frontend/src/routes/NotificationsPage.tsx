import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { listNotifications, markAllNotificationsRead, markNotificationRead, type Notification } from '../api/notifications'
import { notificationRoute } from '../lib/notificationRoutes'
import { Button } from '../components/ui/Button'
import { Card } from '../components/ui/Card'
import { SkeletonList } from '../components/ui/Skeleton'
import { formatDateTime } from '../lib/datetime'

/**
 * SCR-900 — the notification centre. `/notifications`, all authenticated personas, P0.
 *
 * <p>States per SCREEN-INVENTORY: auth (the route is behind the shell's guard), load, empty, ok,
 * error, mobile. Loading uses `SkeletonList` rather than a spinner, per DESIGN-SYSTEM.md §6.13 and
 * the component built in Batch 0.</p>
 *
 * <p>Grouped by day with read/unread state and a per-item link to the object, per §6.14's
 * description of the persistent notification list.</p>
 */
export function NotificationsPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'
  const queryClient = useQueryClient()

  const query = useQuery({ queryKey: ['notifications'], queryFn: () => listNotifications() })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['notifications'] })
    void queryClient.invalidateQueries({ queryKey: ['notifications', 'unread-count'] })
  }

  const readMutation = useMutation({ mutationFn: markNotificationRead, onSuccess: invalidate })
  const readAllMutation = useMutation({ mutationFn: markAllNotificationsRead, onSuccess: invalidate })

  if (query.isPending) {
    return (
      <Card title={t('notifications.title')}>
        <SkeletonList label={t('notifications.title')} rows={5} />
      </Card>
    )
  }

  if (query.isError) {
    return (
      <Card title={t('notifications.title')}>
        <p>{t('notifications.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => query.refetch()}>{t('notifications.retry')}</Button>
      </Card>
    )
  }

  const notifications = query.data?.data ?? []

  if (notifications.length === 0) {
    // UX-WRITING.md §4's empty-state formula: title (what this is), one line (why it is empty),
    // and no primary action - there is nothing for a reader to create here, and §4 shows the
    // action column as "—" for exactly that shape (the reviewer's empty queue).
    return (
      <Card title={t('notifications.title')}>
        <div className="py-8 text-center">
          <p className="font-[var(--fw-semibold)]">{t('notifications.emptyTitle')}</p>
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('notifications.emptyBody')}</p>
        </div>
      </Card>
    )
  }

  // Grouped by day (§6.14). The key is the locale-formatted date, so the heading and the grouping
  // can never disagree about which day a row belongs to.
  const groups = new Map<string, Notification[]>()
  for (const notification of notifications) {
    const day = formatDateTime(notification.createdAt, locale).split('،')[0].split(',')[0]
    groups.set(day, [...(groups.get(day) ?? []), notification])
  }

  return (
    <Card
      title={t('notifications.title')}
      action={
        <Button size="sm" variant="ghost" isLoading={readAllMutation.isPending}
          onClick={() => readAllMutation.mutate()}>
          {t('notifications.markAllRead')}
        </Button>
      }
    >
      {[...groups.entries()].map(([day, rows]) => (
        <section key={day} className="mb-4">
          <h3 className="mb-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>{day}</h3>
          <ul className="flex flex-col gap-2">
            {rows.map((notification) => (
              <li key={notification.id}
                className="flex flex-col gap-1 rounded-[var(--radius-md)] p-3"
                style={{
                  // Unread carries weight, read does not. §10: "non-intrusive", and the read ones
                  // are history rather than something demanding attention.
                  background: notification.isRead ? 'transparent' : 'var(--color-surface-raised)',
                  border: '1px solid var(--color-border)',
                }}
              >
                <div className="flex items-start justify-between gap-2">
                  <p className={notification.isRead ? '' : 'font-[var(--fw-semibold)]'}>
                    {isArabic ? notification.titleAr : notification.titleEn}
                  </p>
                  <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                    {formatDateTime(notification.createdAt, locale)}
                  </span>
                </div>
                <p style={{ color: 'var(--color-text-secondary)' }}>
                  {isArabic ? notification.bodyAr : notification.bodyEn}
                </p>
                <div className="flex items-center gap-3">
                  {notificationRoute(notification) ? (
                    <Link to={notificationRoute(notification)!} className="text-[length:var(--text-body-sm)]">
                      {t('notifications.open')}
                    </Link>
                  ) : null}
                  {notification.isRead ? null : (
                    <button type="button" className="text-[length:var(--text-body-sm)]"
                      onClick={() => readMutation.mutate(notification.id)}>
                      {t('notifications.markRead')}
                    </button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </Card>
  )
}
