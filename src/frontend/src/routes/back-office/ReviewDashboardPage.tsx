import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { getReviewDashboard } from '../../api/dashboards'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { StatusChip } from '../../components/ui/StatusChip'
import { SkeletonGrid, SkeletonList } from '../../components/ui/Skeleton'
import { formatNumber } from '../../lib/datetime'

/**
 * SCR-300 — the onboarding review dashboard. `/review`, P0, FR-DSH-002.
 *
 * <p>Presentation over the queue PR #80 built: the list itself is still SCR-301, and this is the
 * KPI layer plus FR-DSH-002's document-expiry watchlist.</p>
 *
 * <p><b>Aging is a duration and says nothing about lateness.</b> No document defines a review SLA -
 * BUSINESS-PROCESSES §2 names the timer and never its length - so the tile reports how long the
 * oldest open case has waited and stops there. Calling it "overdue" would invent a commitment.</p>
 */
export function ReviewDashboardPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  const query = useQuery({ queryKey: ['review-dashboard'], queryFn: getReviewDashboard })
  const data = query.data

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h1 className="text-[length:var(--text-heading-lg)]">{t('reviewDashboard.title')}</h1>
        <Link to="/back-office/review">{t('reviewDashboard.openQueue')}</Link>
      </div>

      {query.isPending ? <SkeletonGrid label={t('reviewDashboard.title')} items={5} columns={5} /> : null}

      {query.isError ? (
        <Card title={t('reviewDashboard.title')}>
          <p>{t('reviewDashboard.loadFailed')}</p>
          <Button size="sm" variant="ghost" onClick={() => query.refetch()}>{t('reviewDashboard.retry')}</Button>
        </Card>
      ) : null}

      {data ? (
        <ul className="grid grid-cols-2 gap-3 md:grid-cols-5">
          {([
            ['pending', data.pending],
            ['underReview', data.underReview],
            ['infoRequested', data.infoRequested],
            ['unassigned', data.unassigned],
            ['assignedToMe', data.assignedToMe],
          ] as const).map(([key, value]) => (
            <li key={key} className="rounded-[var(--radius-md)] p-3" style={{ border: '1px solid var(--color-border)' }}>
              <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {t(`reviewDashboard.kpis.${key}`)}
              </p>
              <p className="num text-[length:var(--text-heading-md)]">{formatNumber(value, locale, 0)}</p>
            </li>
          ))}
        </ul>
      ) : null}

      {data ? (
        <Card title={t('reviewDashboard.aging')}>
          <p>
            {data.oldestOpenCaseAgeDays === null
              ? t('reviewDashboard.noOpenCases')
              : t('reviewDashboard.oldestCase', {
                  days: formatNumber(data.oldestOpenCaseAgeDays, locale, 0),
                })}
          </p>
        </Card>
      ) : null}

      <Card title={t('reviewDashboard.watchlist')}>
        {query.isPending ? <SkeletonList label={t('reviewDashboard.watchlist')} rows={3} /> : null}
        {data?.expiryWatchlist.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('reviewDashboard.noExpiring')}</p>
        ) : null}
        <ul className="flex flex-col gap-2">
          {(data?.expiryWatchlist ?? []).map((doc) => (
            <li key={`${doc.supplierReferenceCode}-${doc.documentTypeCode}`}
              className="flex flex-wrap items-center justify-between gap-2">
              <span>{isArabic ? doc.supplierDisplayNameAr : doc.supplierDisplayNameEn}</span>
              <span className="flex items-center gap-2">
                <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                  {doc.documentTypeCode}
                </span>
                <StatusChip machine="document" value={doc.state} />
              </span>
            </li>
          ))}
        </ul>
      </Card>
    </div>
  )
}
