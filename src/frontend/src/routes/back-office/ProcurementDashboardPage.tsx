// SCR-400, the procurement dashboard, at /procurement, P0, per SCREEN-SPECIFICATIONS.md §10.
//
// §10's regions in order: a PageHeader with the period filter and a "New RFQ" primary action, the five-tile KPI row, the
// pipeline board, and a two-column lower body of deadlines and activity. The activity column is EPIC-15's notification centre
// LINKED rather than rebuilt - §10 names SCR-900 for it, and a second feed would be a second thing to keep correct. §10 also
// says "Manager also gets an Approvals card -> SCR-401", which is what the last card is.
//
// Every number renders through formatNumber, so a KPI tile cannot read "14" beside a date reading «٣٠ أغسطس». That
// inconsistency is the reason R-1 was ruled on.
//
// THE TONED TILES are the two figures on the row that mean somebody is WAITING rather than reporting how things stand, and
// they are toned only while the count is above zero: an amber nought is not a warning, it is the absence of one, and a row
// where every tile is coloured is a row where colour has stopped meaning anything.
//
// The period filter is §10's. Empty means all time, and the server keeps never-published RFQs either way, so choosing a
// period narrows what was published without emptying the board's left columns.
//
// The KPI row is 2x2 on phones per §10's mobile note, widening with the viewport. The pipeline board becomes "a horizontally
// scrollable stage strip" at that width, also §10's - and the scroll lives on THAT container rather than the page, so the
// board reflows at 320px without the document itself scrolling sideways.

import { useState } from 'react'
import { Metric, MetricRow } from '../../components/ui'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { getProcurementDashboard } from '../../api/dashboards'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { StatusChip } from '../../components/ui/StatusChip'
import { SkeletonGrid, SkeletonList } from '../../components/ui/Skeleton'
import { formatDeadline, formatNumber } from '../../lib/datetime'
import { PageHeading } from '../../components/ui/ListScreen'

const WAITING = new Set(['awaitingMyAction', 'pendingApprovals'])

export function ProcurementDashboardPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')

  const query = useQuery({
    queryKey: ['procurement-dashboard', from, to],
    queryFn: () => getProcurementDashboard(from || undefined, to || undefined),
  })

  const kpis = query.data?.kpis

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <PageHeading title={t('procurementDashboard.title')} />
        <div className="flex flex-wrap items-end gap-2">
          <label className="flex flex-col text-[length:var(--text-body-sm)]">
            {t('procurementDashboard.from')}
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)}
              className="rounded-[var(--radius-sm)] border p-1" style={{ borderColor: 'var(--color-border)' }} />
          </label>
          <label className="flex flex-col text-[length:var(--text-body-sm)]">
            {t('procurementDashboard.to')}
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)}
              className="rounded-[var(--radius-sm)] border p-1" style={{ borderColor: 'var(--color-border)' }} />
          </label>
          <Link to="/back-office/rfqs">
            <Button size="sm">{t('procurementDashboard.newRfq')}</Button>
          </Link>
        </div>
      </header>

      {query.isPending ? <SkeletonGrid label={t('procurementDashboard.title')} items={5} columns={5} /> : null}

      {query.isError ? (
        <Card title={t('procurementDashboard.title')}>
          <p>{t('procurementDashboard.loadFailed')}</p>
          <Button size="sm" variant="ghost" onClick={() => query.refetch()}>{t('procurementDashboard.retry')}</Button>
        </Card>
      ) : null}

      {kpis ? (
        <MetricRow>
          {([
            ['activeRfqs', kpis.activeRfqs],
            ['closingThisWeek', kpis.closingThisWeek],
            ['awaitingMyAction', kpis.awaitingMyAction],
            ['pendingApprovals', kpis.pendingApprovals],
            ['awardsInProgress', kpis.awardsInProgress],
          ] as const).map(([key, value]) => (
            <Metric
              key={key}
              label={t(`procurementDashboard.kpis.${key}`)}
              value={formatNumber(value, locale, 0)}
              tone={WAITING.has(key) && value > 0 ? 'warning' : 'neutral'}
            />
          ))}
        </MetricRow>
      ) : null}

      {query.data ? (
        <Card title={t('procurementDashboard.pipeline')}>
          {query.data.pipeline.length === 0 ? (
            <div className="py-6 text-center">
              <p className="font-[var(--fw-semibold)]">{t('procurementDashboard.emptyTitle')}</p>
              <p style={{ color: 'var(--color-text-secondary)' }}>{t('procurementDashboard.emptyBody')}</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <ul className="flex gap-3" style={{ minWidth: 'min-content' }}>
                {query.data.pipeline.map((column) => (
                  <li key={column.state} className="min-w-[9rem] rounded-[var(--radius-md)] p-3"
                    style={{ border: '1px solid var(--color-border)' }}>
                    <StatusChip machine="rfq" value={column.state} />
                    <p className="num mt-2 text-[length:var(--text-h2)]">
                      {formatNumber(column.count, locale, 0)}
                    </p>
                    {column.nearestDeadline ? (
                      <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                        {formatDeadline(column.nearestDeadline, locale)}
                      </p>
                    ) : null}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </Card>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title={t('procurementDashboard.tasks')}>
          {query.isPending ? <SkeletonList label={t('procurementDashboard.tasks')} rows={3} /> : null}
          {query.data?.tasks.length === 0 ? (
            <p style={{ color: 'var(--color-text-secondary)' }}>{t('procurementDashboard.noTasks')}</p>
          ) : null}
          <ul className="flex flex-col gap-2">
            {(query.data?.tasks ?? []).map((task) => (
              <li key={`${task.rfqReferenceCode}-${task.kind}`} className="flex flex-wrap justify-between gap-2">
                <Link to="/back-office/rfqs/$referenceCode" params={{ referenceCode: task.rfqReferenceCode }}>
                  {isArabic ? task.titleAr : task.titleEn}
                </Link>
                <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                  {t(`procurementDashboard.taskKinds.${task.kind}`)}
                  {task.due ? ` · ${formatDeadline(task.due, locale)}` : ''}
                </span>
              </li>
            ))}
          </ul>
        </Card>

        <div className="flex flex-col gap-4">
          <Card title={t('procurementDashboard.activity')}>
            <Link to="/back-office/notifications">{t('procurementDashboard.openNotifications')}</Link>
          </Card>

          {query.data?.showsApprovals ? (
            <Card title={t('procurementDashboard.approvals')}>
              <Link to="/back-office/procurement/approvals">{t('procurementDashboard.openApprovals')}</Link>
            </Card>
          ) : null}
        </div>
      </div>
    </div>
  )
}
