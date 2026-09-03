import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { getComplianceReport, getProcurementReport, downloadReport } from '../../api/reports'
import type { ReportCount } from '../../api/reports'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { StatusChip } from '../../components/ui/StatusChip'
import { SkeletonList } from '../../components/ui/Skeleton'
import { formatDateTime, formatNumber } from '../../lib/datetime'

/**
 * FEAT-19.1 and FEAT-19.2, at `/back-office/reports`.
 *
 * <p><b>The entire screen design here is an INVENTION and is marked as one.</b> The IA gives this
 * route and a `report.read` gate and nothing else - no layout, no states, no component list - and
 * the question to the documentation owner has not come back. So it is built on the shape SCR-400 and
 * SCR-120 already established in this product: page header, filter row, results tables, export
 * action. A report screen is a filtered table and the design system has every part of one; if a
 * specification arrives later the rework is layout, not logic.</p>
 *
 * <p><b>No SCR id is claimed.</b> Screen ids are cross-referenced from the specifications, the
 * backlog and the tests, so an invented one would corrupt that inventory rather than fill a hole in
 * it. This screen is referred to by its route.</p>
 *
 * <p>Every number renders through `formatNumber`, so a count cannot read "14" beside a date reading
 * «٣٠ أغسطس» - the inconsistency R-1 was ruled on. State keys render through StatusChip, never as
 * raw enum names.</p>
 */
export function ReportsPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [downloadError, setDownloadError] = useState(false)

  const procurement = useQuery({
    queryKey: ['procurement-report', from, to],
    queryFn: () => getProcurementReport(from || undefined, to || undefined),
  })

  const compliance = useQuery({
    queryKey: ['compliance-report'],
    queryFn: () => getComplianceReport(),
  })

  async function download(kind: 'procurement' | 'compliance', format: 'pdf' | 'csv') {
    setDownloadError(false)
    try {
      await downloadReport(kind, format, from || undefined, to || undefined)
    } catch {
      // A failed download is otherwise completely silent - the browser simply does nothing, which
      // is indistinguishable from a slow one.
      setDownloadError(true)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <h1 className="text-[length:var(--text-heading-lg)]">{t('reports.title')}</h1>
        <div className="flex flex-wrap items-end gap-2">
          <label className="flex flex-col text-[length:var(--text-body-sm)]">
            {t('reports.from')}
            <input
              type="date"
              value={from}
              onChange={(e) => setFrom(e.target.value)}
              className="rounded-[var(--radius-sm)] border p-1"
              style={{ borderColor: 'var(--color-border)' }}
            />
          </label>
          <label className="flex flex-col text-[length:var(--text-body-sm)]">
            {t('reports.to')}
            <input
              type="date"
              value={to}
              onChange={(e) => setTo(e.target.value)}
              className="rounded-[var(--radius-sm)] border p-1"
              style={{ borderColor: 'var(--color-border)' }}
            />
          </label>
        </div>
      </header>

      {downloadError ? (
        <p role="alert" style={{ color: 'var(--color-danger)' }}>{t('reports.downloadFailed')}</p>
      ) : null}

      {/* ---------------------------------------------------------------- FEAT-19.1 */}
      <Card title={t('reports.procurement.title')}>
        <div className="mb-3 flex flex-wrap gap-2">
          <Button size="sm" variant="ghost" onClick={() => download('procurement', 'pdf')}>
            {t('reports.exportPdf')}
          </Button>
          <Button size="sm" variant="ghost" onClick={() => download('procurement', 'csv')}>
            {t('reports.exportCsv')}
          </Button>
        </div>

        {procurement.isPending ? <SkeletonList label={t('reports.title')} rows={4} /> : null}

        {procurement.isError ? (
          <>
            <p>{t('reports.loadFailed')}</p>
            <Button size="sm" variant="ghost" onClick={() => procurement.refetch()}>{t('reports.retry')}</Button>
          </>
        ) : null}

        {procurement.data ? (
          <div className="flex flex-col gap-5">
            <CountTable
              caption={t('reports.procurement.rfqsByState')}
              machine="rfq"
              rows={procurement.data.rfqsByState}
              locale={locale}
              stateHeader={t('reports.state')}
              countHeader={t('reports.count')}
              emptyLabel={t('reports.noRows')}
            />

            <section>
              <h3 className="mb-2 text-[length:var(--text-body-md)]">{t('reports.procurement.cycleTime')}</h3>

              {/*
                The coverage floor, stated on the screen and not only in the export. Cycle time is
                derived from audit rows, which began when that logging was added - RFQs that moved
                earlier contribute to nothing and are silently absent. Without this line a short
                history reads as a fast process.
              */}
              <p className="mb-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {procurement.data.coverageFloor
                  ? t('reports.procurement.coverageFloor', {
                      date: formatDateTime(procurement.data.coverageFloor, locale),
                    })
                  : t('reports.procurement.coverageNone')}
              </p>

              <div className="overflow-x-auto">
                <table className="w-full text-start">
                  <caption className="sr-only">{t('reports.procurement.cycleTime')}</caption>
                  <thead>
                    <tr>
                      <th scope="col" className="text-start">{t('reports.interval')}</th>
                      <th scope="col" className="text-start">{t('reports.sampleSize')}</th>
                      <th scope="col" className="text-start">{t('reports.medianHours')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {procurement.data.cycleTimes.map((interval) => (
                      <tr key={interval.key}>
                        <th scope="row" className="text-start font-normal">
                          {t(`reports.intervals.${interval.key}`)}
                        </th>
                        <td className="num">{formatNumber(interval.sampleSize, locale, 0)}</td>
                        <td className="num">
                          {/*
                            Never a zero for an unmeasured interval. "No RFQ has reached award" and
                            "award takes no time" are different facts and only one of them is true.
                          */}
                          {interval.medianHours === null
                            ? t('reports.notMeasured')
                            : formatNumber(interval.medianHours, locale, 1)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>

            <CountTable
              caption={t('reports.procurement.awardsByState')}
              machine="award"
              rows={procurement.data.awardsByState}
              locale={locale}
              stateHeader={t('reports.state')}
              countHeader={t('reports.count')}
              emptyLabel={t('reports.noRows')}
            />
          </div>
        ) : null}
      </Card>

      {/* ---------------------------------------------------------------- FEAT-19.2 */}
      <Card title={t('reports.compliance.title')}>
        <div className="mb-3 flex flex-wrap gap-2">
          <Button size="sm" variant="ghost" onClick={() => download('compliance', 'pdf')}>
            {t('reports.exportPdf')}
          </Button>
          <Button size="sm" variant="ghost" onClick={() => download('compliance', 'csv')}>
            {t('reports.exportCsv')}
          </Button>
        </div>

        {compliance.isPending ? <SkeletonList label={t('reports.title')} rows={3} /> : null}

        {compliance.isError ? (
          <>
            <p>{t('reports.loadFailed')}</p>
            <Button size="sm" variant="ghost" onClick={() => compliance.refetch()}>{t('reports.retry')}</Button>
          </>
        ) : null}

        {compliance.data ? (
          <div className="flex flex-col gap-5">
            {/*
              Said on the screen, not only in the export's provenance. These counts cover every
              supplier in the ministry because Supplier carries no organization - a reader who
              assumes otherwise reads them as their own organization's numbers.
            */}
            <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('reports.compliance.registryScope')}
            </p>

            <CountTable
              caption={t('reports.compliance.suppliersByState')}
              machine="onboarding"
              rows={compliance.data.suppliersByLifecycleState}
              locale={locale}
              stateHeader={t('reports.state')}
              countHeader={t('reports.count')}
              emptyLabel={t('reports.noRows')}
            />

            <CountTable
              caption={t('reports.compliance.documentsByState')}
              machine="document"
              rows={compliance.data.documentsByState}
              locale={locale}
              stateHeader={t('reports.state')}
              countHeader={t('reports.count')}
              emptyLabel={t('reports.noRows')}
            />
          </div>
        ) : null}
      </Card>
    </div>
  )
}

/**
 * A state/count table. Extracted because there are four of them and the RTL, numeral and chip rules
 * have to hold identically in all four - one implementation is the only way that stays true.
 */
function CountTable({
  caption, machine, rows, locale, stateHeader, countHeader, emptyLabel,
}: {
  caption: string
  machine: 'rfq' | 'award' | 'onboarding' | 'document'
  rows: ReportCount[]
  locale: string
  stateHeader: string
  countHeader: string
  emptyLabel: string
}) {
  return (
    <section>
      <h3 className="mb-2 text-[length:var(--text-body-md)]">{caption}</h3>

      {rows.length === 0 ? (
        <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {emptyLabel}
        </p>
      ) : (
        // Wide content scrolls inside its own container; the page body never scrolls sideways.
        <div className="overflow-x-auto">
          <table className="w-full text-start">
            <caption className="sr-only">{caption}</caption>
            <thead>
              <tr>
                <th scope="col" className="text-start">{stateHeader}</th>
                <th scope="col" className="text-start">{countHeader}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.key}>
                  <th scope="row" className="text-start font-normal">
                    <StatusChip machine={machine} value={row.key} />
                  </th>
                  <td className="num">{formatNumber(row.count, locale, 0)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
