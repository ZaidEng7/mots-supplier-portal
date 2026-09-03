import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { getProcurementDashboard } from '../../api/dashboards'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { StatusChip } from '../../components/ui/StatusChip'
import { SkeletonGrid, SkeletonList } from '../../components/ui/Skeleton'
import { formatDeadline, formatNumber } from '../../lib/datetime'

/**
 * SCR-400 — the procurement dashboard. `/procurement`, P0, SCREEN-SPECIFICATIONS.md §10.
 *
 * <p>§10's regions in order: PageHeader with the period filter and a "New RFQ" primary action, the
 * five-tile KPI row, the pipeline board, and a two-column lower body of deadlines and activity. The
 * activity column is EPIC-15's notification centre linked rather than rebuilt - §10 names SCR-900
 * for it, and a second feed would be a second thing to keep correct.</p>
 *
 * <p>Every number renders through `formatNumber`, so a KPI tile cannot read "14" beside a date
 * reading «٣٠ أغسطس». That inconsistency is the reason R-1 was ruled on.</p>
 */
export function ProcurementDashboardPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  // §10's "period filter". Empty means all time; the server keeps never-published RFQs either way,
  // so choosing a period narrows what was published without emptying the board's left columns.
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
        <h1 className="text-[length:var(--text-heading-lg)]">{t('procurementDashboard.title')}</h1>
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
        // §10's KPI row. 2×2 on phones per its mobile note, widening with the viewport.
        <ul className="grid grid-cols-2 gap-3 md:grid-cols-5">
          {([
            ['activeRfqs', kpis.activeRfqs],
            ['closingThisWeek', kpis.closingThisWeek],
            ['awaitingMyAction', kpis.awaitingMyAction],
            ['pendingApprovals', kpis.pendingApprovals],
            ['awardsInProgress', kpis.awardsInProgress],
          ] as const).map(([key, value]) => (
            <li key={key} className="rounded-[var(--radius-md)] p-3" style={{ border: '1px solid var(--color-border)' }}>
              <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {t(`procurementDashboard.kpis.${key}`)}
              </p>
              <p className="num text-[length:var(--text-heading-md)]">{formatNumber(value, locale, 0)}</p>
            </li>
          ))}
        </ul>
      ) : null}

      {query.data ? (
        <Card title={t('procurementDashboard.pipeline')}>
          {query.data.pipeline.length === 0 ? (
            <div className="py-6 text-center">
              <p className="font-[var(--fw-semibold)]">{t('procurementDashboard.emptyTitle')}</p>
              <p style={{ color: 'var(--color-text-secondary)' }}>{t('procurementDashboard.emptyBody')}</p>
            </div>
          ) : (
            // §10's mobile note: "the pipeline board becomes a horizontally scrollable stage strip".
            // The scroll lives on THIS container, not the page, so the board reflows at 320px without
            // the document itself scrolling sideways.
            <div className="overflow-x-auto">
              <ul className="flex gap-3" style={{ minWidth: 'min-content' }}>
                {query.data.pipeline.map((column) => (
                  <li key={column.state} className="min-w-[9rem] rounded-[var(--radius-md)] p-3"
                    style={{ border: '1px solid var(--color-border)' }}>
                    <StatusChip machine="rfq" value={column.state} />
                    <p className="num mt-2 text-[length:var(--text-heading-md)]">
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
          {/* §10 names SCR-900 for this column. Linked, not rebuilt. */}
          <Card title={t('procurementDashboard.activity')}>
            <Link to="/back-office/notifications">{t('procurementDashboard.openNotifications')}</Link>
          </Card>

          {/* §10: "Manager also gets an Approvals card → SCR-401." */}
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
