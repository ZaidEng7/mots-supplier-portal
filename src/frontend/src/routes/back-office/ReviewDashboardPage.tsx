import { useTranslation } from 'react-i18next'
import { Metric, MetricRow } from '../../components/ui'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { getReviewDashboard } from '../../api/dashboards'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { StatusChip } from '../../components/ui/StatusChip'
import { SkeletonGrid, SkeletonList } from '../../components/ui/Skeleton'
import { formatNumber } from '../../lib/datetime'
import { PageHeading } from '../../components/ui/ListScreen'

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
/**
 * The figures here that mean an application is sitting with nobody, or with this reader. Same rule as
 * the procurement dashboard: toned only while the count is above zero.
 */
const WAITING = new Set(['pending', 'unassigned'])

export function ReviewDashboardPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  const query = useQuery({ queryKey: ['review-dashboard'], queryFn: getReviewDashboard })
  const data = query.data

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <PageHeading title={t('reviewDashboard.title')} />
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
        <MetricRow>
          {([
            ['pending', data.pending],
            ['underReview', data.underReview],
            ['infoRequested', data.infoRequested],
            ['unassigned', data.unassigned],
            ['assignedToMe', data.assignedToMe],
          ] as const).map(([key, value]) => (
            <Metric
              key={key}
              label={t(`reviewDashboard.kpis.${key}`)}
              value={formatNumber(value, locale, 0)}
              tone={WAITING.has(key) && value > 0 ? 'warning' : 'neutral'}
            />
          ))}
        </MetricRow>
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
