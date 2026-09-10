import { useTranslation } from 'react-i18next'
import { Metric, MetricRow } from '../../components/ui'
import { useQuery } from '@tanstack/react-query'
import {Badge, Button, Card, PageHeading, SkeletonList} from '../../components/ui'
import { formatNumber } from '../../lib/datetime'
import { getAdminOverview } from '../../api/admin'

/**
 * SCR-700, `/back-office/admin`, `system_admin`, P1 (FR-DSH-006).
 *
 * <p>`system_admin` could reach the staff, role and reference-data screens but had no landing page,
 * so the operational facts that decide whether the platform is working - is the outbox draining, are
 * the recurring jobs actually registered, does every reference table still have an active code - were
 * visible only in logs.</p>
 *
 * <p>The jobs tile is the one worth explaining: `Jobs:EnableRecurring=false` is a legitimate
 * configuration (it is how the test host runs), and today it announces itself once at startup and
 * never again. An operator looking at a portal that has stopped sending anything has no other way to
 * see it.</p>
 */
export function AdminOverviewPage() {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'
  const n = (value: number) => formatNumber(value, locale, 0)

  const query = useQuery({ queryKey: ['admin-overview'], queryFn: getAdminOverview })

  if (query.isLoading) return <SkeletonList label={t('common.loading')} />

  if (query.isError || !query.data) {
    return (
      <Card title={t('adminOverview.title')}>
        <p>{t('adminOverview.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => void query.refetch()}>{t('adminOverview.retry')}</Button>
      </Card>
    )
  }

  const data = query.data
  const users = data.usersByRole.reduce((total, entry) => total + entry.count, 0)
  const emptyTables = data.referenceData.filter((table) => table.active === 0)

  const jobsMissing = data.jobs.missingJobs.length > 0

  /**
   * Three answers to "are the background jobs running?", in order of how bad they are: the scheduler is
   * off, the scheduler is on but jobs the deployment expects are absent, or everything is registered.
   * A function rather than a chain of ternaries because they are three separate answers, not one
   * decision refined twice.
   */
  function renderJobs() {
    if (!data.jobs.recurringJobsEnabled) {
      return (
        <div className="flex flex-col gap-2">
          <Badge tone="warning">{t('adminOverview.jobsDisabled')}</Badge>
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('adminOverview.jobsDisabledBody')}</p>
        </div>
      )
    }
    if (jobsMissing) {
      return (
        <div className="flex flex-col gap-2">
          <Badge tone="danger">{t('adminOverview.jobsMissing')}</Badge>
          {/* The ids themselves: an operator comparing them against the deployment is the point, so
              these are not translated. */}
          <ul className="flex flex-col gap-1">
            {data.jobs.missingJobs.map((job) => <li key={job}><code>{job}</code></li>)}
          </ul>
        </div>
      )
    }
    return (
      <Badge tone="success">
        {t('adminOverview.jobsHealthy', { value: n(data.jobs.registeredJobs.length) })}
      </Badge>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('adminOverview.title')} />

      {/* 2x2 on phones, widening with the viewport - the KPI-row shape the other dashboards use. */}
      <MetricRow>
        {([
          ['users', n(users)],
          ['roles', n(data.totalRoles)],
          ['outboxPending', n(data.outbox.pending)],
          ['auditRows', n(data.auditRowsLast24Hours)],
        ] as const).map(([key, value]) => (
          <Metric key={key} label={t(`adminOverview.kpis.${key}`)} value={value} />
        ))}
      </MetricRow>

      <Card title={t('adminOverview.outbox')}>
        <ul className="flex flex-col gap-1">
          <li className="flex items-center justify-between gap-2">
            <span>{t('adminOverview.outboxPending')}</span>
            <span className="num">{n(data.outbox.pending)}</span>
          </li>
          <li className="flex items-center justify-between gap-2">
            <span>{t('adminOverview.outboxFailed')}</span>
            <span className="num">{n(data.outbox.failed)}</span>
          </li>
          <li className="flex items-center justify-between gap-2">
            <span>{t('adminOverview.outboxOldest')}</span>
            <span className="num">
              {data.outbox.oldestPendingAgeMinutes === null
                ? t('adminOverview.outboxDrained')
                : t('adminOverview.minutes', { count: data.outbox.oldestPendingAgeMinutes, value: n(data.outbox.oldestPendingAgeMinutes) })}
            </span>
          </li>
        </ul>
        {data.outbox.failed > 0 ? (
          <p className="mt-2"><Badge tone="danger">{t('adminOverview.outboxFailedWarning')}</Badge></p>
        ) : null}
        {/* B-1/BRULE-011. A draining outbox with the logging stand-in registered has delivered nothing,
            and an operator reading a healthy tile would conclude the opposite. */}
        {!data.outbox.erpTransportConfigured ? (
          <div className="mt-2 flex flex-col gap-1">
            <Badge tone="warning">{t('adminOverview.erpNotConfigured')}</Badge>
            <p style={{ color: 'var(--color-text-secondary)' }}>{t('adminOverview.erpNotConfiguredBody')}</p>
          </div>
        ) : null}
      </Card>

      <Card title={t('adminOverview.jobs')}>
        {renderJobs()}
      </Card>

      <Card title={t('adminOverview.referenceData')}>
        {emptyTables.length > 0 ? (
          <p className="mb-2"><Badge tone="danger">{t('adminOverview.referenceEmpty')}</Badge></p>
        ) : null}
        <ul className="flex flex-col gap-1">
          {data.referenceData.map((table) => (
            <li key={table.table} className="flex items-center justify-between gap-2">
              <span>{t(`adminOverview.tables.${table.table}`, { defaultValue: table.table })}</span>
              <span className="num">
                {t('adminOverview.activeOfTotal', {
                  active: n(table.active),
                  total: n(table.active + table.inactive),
                })}
              </span>
            </li>
          ))}
        </ul>
      </Card>
    </div>
  )
}
