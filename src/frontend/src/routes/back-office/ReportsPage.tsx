// FEAT-19.1 and FEAT-19.2, at /back-office/reports. Two reports on one screen, in that order.
//
// THE ENTIRE SCREEN DESIGN HERE IS AN INVENTION and is marked as one. The IA gives this route and a report.read gate and
// nothing else - no layout, no states, no component list - and the question to the documentation owner has not come back. So
// it is built on the shape SCR-400 and SCR-120 already established in this product: page header, filter row, results tables,
// export action. A report screen is a filtered table and the design system has every part of one; if a specification arrives
// later the rework is layout, not logic.
//
// NO SCR ID IS CLAIMED. Screen ids are cross-referenced from the specifications, the backlog and the tests, so an invented one
// would corrupt that inventory rather than fill a hole in it. This screen is referred to by its route.
//
// Every number renders through formatNumber, so a count cannot read "14" beside a date reading «٣٠ أغسطس» - the inconsistency
// R-1 was ruled on. State keys render through StatusChip, never as raw enum names.
//
// A FAILED DOWNLOAD says so, because otherwise it is completely silent: the browser simply does nothing, which is
// indistinguishable from a slow one.
//
// THE NO-BUYING-BODY CASE is not an error, and it used to be reported as one. The procurement report counts one buying body's
// tenders, and two personas deliberately belong to none - the bootstrap administrator and the Ministry viewer, whose grant is
// cross-organization by BRULE-086 and would be narrowed by pinning it to one. The endpoint answers 404 for them, correctly,
// and the card said "The report could not be loaded" with a Try again that could never work: a retry of a question this
// account is not able to ask. The compliance report below is unscoped, which is why it loads for the same accounts - and the
// two sitting side by side, one broken and one fine, is what made it read as a fault.
//
// ITS EXPORT BUTTONS WERE LEFT BEHIND by that fix. The card explained the scope while, three lines above, Export PDF and
// Export CSV still offered a download of it - and /reports/procurement/export 404s for the same accounts and the same reason,
// so pressing either one produced "The file could not be downloaded" in red. One card then said both things at once: this is
// not a failure, and here is a failure. So the two buttons are not rendered when the report is out of scope. They are offered
// while the report is still loading and while it is genuinely failing, because neither of those says the account has no
// buying body; only a 404 does, and a 404 is what this state is. The download error itself stays, for the compliance export
// and for a download that really does break.
//
// THE REGISTRY EXPORT IS ON THIS SCREEN but it is NOT a report, and the card says so rather than pretending
// otherwise: it is the registry itself, one row per supplier, for the ministry data lake. It sits here because
// this is where a person already comes to take data out of the product, and a second screen holding one button
// would be worse. It is gated on supplier.registry.export, which only the system administrator holds - NOT on
// report.read, which opens this screen and which the procurement manager and the Ministry viewer also hold. So
// the card is absent for them rather than present and answering 403, which is the defect the procurement
// export above was just fixed for; repeating it one card lower would be hard to excuse.
//
// WHAT IS IN THE FILE IS STATED ON THE CARD, in the colour a warning uses. Tax identifiers, named contacts with
// their email addresses and phone numbers, and bank account holders leave the building in one file, and an
// administrator who has not read the API documentation has no other way to know that before clicking. Saying it
// after the download is saying it too late.
//
// THE MINISTRY FEEDS ARE TWO FILES ON ONE CARD, not two cards, because they are one delivery: feeds 1 and 4 of
// a workbook whose other two feeds belong to the ERP. Their buttons say which file each is rather than both
// saying Export CSV, since the failure this screen already has a history of is somebody sending the wrong one.
//
// THE MINISTRY FEED IS A THIRD EXPORT AND NOT A FOURTH REPORT. It is the supplier registry again, but shaped
// to a workbook the ministry sent: their twenty-two column names, their vocabulary, their file. It sits beside
// the registry export because both are "take the registry out of the product", and it is separate from it
// because the two answer to different people - ours changes when we have something new to say, theirs breaks a
// dashboard if a column moves. The card says which is which, so nobody sends the wrong file.
//
// THE COVERAGE FLOOR is stated on the screen and not only in the export. Cycle time is derived from audit rows, which began
// when that logging was added, so RFQs that moved earlier contribute to nothing and are silently absent - and without that
// line a short history reads as a fast process.
//
// AN UNMEASURED INTERVAL is never a zero. "No RFQ has reached award" and "award takes no time" are different facts and only
// one of them is true.
//
// THE REGISTRY SCOPE is said on the screen too, not only in the export's provenance: these counts cover every supplier in the
// ministry because Supplier carries no organization, and a reader who assumes otherwise reads them as their own
// organization's numbers.
//
// THE STATE TABLE is extracted because there are four of them and the RTL, numeral and chip rules have to hold identically in
// all four - one implementation is the only way that stays true. Table already scrolls wide content inside its own container,
// so the page body still never scrolls sideways and the hand-rolled wrapper that used to say so is gone with it.

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { getComplianceReport, getProcurementReport, downloadReport, downloadSupplierRegistry, downloadMinistrySupplierFeed, downloadMinistryRfqFeed } from '../../api/reports'
import { usePermissions } from '../../lib/authStore'
import type { ReportCount } from '../../api/reports'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { StatusChip } from '../../components/ui/StatusChip'
import { SkeletonList } from '../../components/ui/Skeleton'
import { formatDateTime, formatNumber } from '../../lib/datetime'
import { PageHeading } from '../../components/ui/ListScreen'
import { Field } from '../../components/ui/Field'
import { Input } from '../../components/ui/Input'
import { Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../../components/ui/Table'

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

  const procurementOutOfScope = procurement.isSuccess && procurement.data === null
  const can = usePermissions()

  async function download(kind: 'procurement' | 'compliance', format: 'pdf' | 'csv') {
    setDownloadError(false)
    try {
      await downloadReport(kind, format, from || undefined, to || undefined)
    } catch {
      setDownloadError(true)
    }
  }

  async function downloadRegistry() {
    setDownloadError(false)
    try {
      await downloadSupplierRegistry()
    } catch {
      setDownloadError(true)
    }
  }

  async function downloadFeed(feed: 'suppliers' | 'rfqs') {
    setDownloadError(false)
    try {
      await (feed === 'suppliers' ? downloadMinistrySupplierFeed() : downloadMinistryRfqFeed())
    } catch {
      setDownloadError(true)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <PageHeading title={t('reports.title')} />
        <div className="flex flex-wrap items-end gap-2">
          <Field label={t('reports.from')}>
            {(p) => <Input {...p} type="date" value={from} onChange={(e) => setFrom(e.target.value)} />}
          </Field>
          <Field label={t('reports.to')}>
            {(p) => <Input {...p} type="date" value={to} onChange={(e) => setTo(e.target.value)} />}
          </Field>
        </div>
      </header>

      {downloadError ? (
        <p role="alert" style={{ color: 'var(--color-danger-fg)' }}>{t('reports.downloadFailed')}</p>
      ) : null}

      <Card title={t('reports.procurement.title')}>
        {procurementOutOfScope ? null : (
          <div className="mb-3 flex flex-wrap gap-2">
            <Button size="sm" variant="ghost" onClick={() => download('procurement', 'pdf')}>
              {t('reports.exportPdf')}
            </Button>
            <Button size="sm" variant="ghost" onClick={() => download('procurement', 'csv')}>
              {t('reports.exportCsv')}
            </Button>
          </div>
        )}

        {procurement.isPending ? <SkeletonList label={t('reports.title')} rows={4} /> : null}

        {procurement.isError ? (
          <>
            <p>{t('reports.loadFailed')}</p>
            <Button size="sm" variant="ghost" onClick={() => procurement.refetch()}>{t('reports.retry')}</Button>
          </>
        ) : null}

        {procurement.isSuccess && procurement.data === null ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('reports.procurement.noBuyingBody')}</p>
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
              <h3 className="mb-2 text-[length:var(--text-h4)]">{t('reports.procurement.cycleTime')}</h3>

              <p className="mb-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {procurement.data.coverageFloor
                  ? t('reports.procurement.coverageFloor', {
                      date: formatDateTime(procurement.data.coverageFloor, locale),
                    })
                  : t('reports.procurement.coverageNone')}
              </p>

              <div>
                <Table caption={t('reports.procurement.cycleTime')}>
                  <TableHead labels={[t('reports.interval'), t('reports.sampleSize'), t('reports.medianHours')]} />
                  <TableBody>
                    {procurement.data.cycleTimes.map((interval) => (
                      <TableRow key={interval.key}>
                        <TableHeaderCell scope="row" className="font-[var(--fw-regular)]">
                          {t(`reports.intervals.${interval.key}`)}
                        </TableHeaderCell>
                        <TableCell className="num">{formatNumber(interval.sampleSize, locale, 0)}</TableCell>
                        <TableCell className="num">
                          {interval.medianHours === null
                            ? t('reports.notMeasured')
                            : formatNumber(interval.medianHours, locale, 1)}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
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

      {can('supplier.registry.export') ? (
        <Card title={t('reports.registry.title')}>
          <div className="mb-3 flex flex-wrap gap-2">
            <Button size="sm" variant="ghost" onClick={downloadRegistry}>
              {t('reports.exportCsv')}
            </Button>
          </div>

          <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('reports.registry.what')}
          </p>

          <p className="mt-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-warning-fg)' }}>
            {t('reports.registry.sensitive')}
          </p>
        </Card>
      ) : null}

      {can('supplier.registry.export') ? (
        <Card title={t('reports.feed.title')}>
          <div className="mb-3 flex flex-wrap gap-2">
            <Button size="sm" variant="ghost" onClick={() => downloadFeed('suppliers')}>
              {t('reports.feed.suppliers')}
            </Button>
            <Button size="sm" variant="ghost" onClick={() => downloadFeed('rfqs')}>
              {t('reports.feed.rfqs')}
            </Button>
          </div>

          <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('reports.feed.what')}
          </p>

          <p className="mt-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('reports.feed.shape')}
          </p>
        </Card>
      ) : null}

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
      <h3 className="mb-2 text-[length:var(--text-h4)]">{caption}</h3>

      {rows.length === 0 ? (
        <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {emptyLabel}
        </p>
      ) : (
        <Table caption={caption}>
          <TableHead labels={[stateHeader, countHeader]} />
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.key}>
                <TableHeaderCell scope="row" className="font-[var(--fw-regular)]">
                  <StatusChip machine={machine} value={row.key} />
                </TableHeaderCell>
                <TableCell className="num">{formatNumber(row.count, locale, 0)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </section>
  )
}
