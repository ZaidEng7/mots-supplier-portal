import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import {Badge, Card, PageHeading, SkeletonList, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow} from '../../components/ui'
import { getMinistryRfqDetail } from '../../api/governance'
import { formatCurrency, formatDate, formatNumber } from '../../lib/datetime'

/**
 * SCR-606, `/ministry/rfqs/:code`, `ministry_viewer` — read-only, and the screen D-66 is really about.
 *
 * <p><b>What this shows that nothing else in the product shows anyone outside the buying body:</b> every bid
 * on a tender, with the bidding supplier named and its total, whatever state the tender is in — including
 * still open for submissions. A-8 anonymises bidders for the evaluators themselves while scoring runs; this
 * screen deliberately does not, because that is the scope D-57 offered and D-66 chose.</p>
 *
 * <p>There is one line it does not cross: a Draft bid is never listed. A draft has been offered to nobody,
 * and no reading of D-57 covers a supplier's unfinished thinking.</p>
 *
 * <p>When the commercial-visibility flag is off, the values render as withheld rather than as zero, and the
 * banner says so. "Policy withholds this" and "they bid nothing" are different facts.</p>
 */
export function MinistryRfqDetailPage({ referenceCode }: { referenceCode: string }) {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  const query = useQuery({
    queryKey: ['ministry-rfq', referenceCode],
    queryFn: () => getMinistryRfqDetail(referenceCode),
  })

  if (query.isPending) return <SkeletonList label={t('common.loading')} />
  if (query.isError || !query.data) {
    return (
      <Card title={t('ministryRfqDetail.title')}>
        <p>{t('ministryRfqDetail.loadFailed')}</p>
      </Card>
    )
  }

  const { summary, descriptionAr, descriptionEn, items, bids, commercialValuesVisible } = query.data

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Link to="/back-office/ministry/rfqs" className="text-[length:var(--text-body-sm)]">
          {t('ministryRfqDetail.backToMonitor')}
        </Link>
        <PageHeading title={isArabic ? summary.titleAr : summary.titleEn} />
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {summary.referenceCode} · {isArabic ? summary.organizationNameAr : summary.organizationNameEn}
        </p>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <StatusChip machine="rfq" value={summary.state} />
          {/* Read-only is a fact about this screen, not a hint: a ministry_viewer holds governance.read and
              nothing else, so every write route in the product answers them 403. Saying so beats letting
              them look for a button that was never there. */}
          <Badge tone="neutral">{t('ministryRfqDetail.readOnly')}</Badge>
        </div>
      </div>

      {!commercialValuesVisible ? (
        <output className="block rounded-[var(--radius-md)] px-4 py-3 text-[length:var(--text-body-sm)]"
           style={{ backgroundColor: 'var(--warning-50)', color: 'var(--warning-600)' }}>
          {t('ministryRfqDetail.valuesWithheld')}
        </output>
      ) : null}

      <Card title={t('ministryRfqDetail.tender')}>
        <p style={{ color: 'var(--color-text-primary)' }}>
          {(isArabic ? descriptionAr : descriptionEn) ?? t('ministryRfqDetail.noDescription')}
        </p>
        <dl className="mt-4 grid grid-cols-2 gap-3 md:grid-cols-4">
          <div>
            <dt className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('ministryRfqs.fields.closes')}
            </dt>
            <dd>{summary.submissionClosesAt ? formatDate(summary.submissionClosesAt, locale) : '—'}</dd>
          </div>
          <div>
            <dt className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('ministryRfqDetail.invited')}
            </dt>
            <dd>{formatNumber(summary.invitedSuppliers, locale, 0)}</dd>
          </div>
          <div>
            <dt className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('ministryRfqDetail.bidsReceived')}
            </dt>
            <dd>{formatNumber(summary.submittedProposals, locale, 0)}</dd>
          </div>
          <div>
            <dt className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('ministryRfqs.fields.awarded')}
            </dt>
            <dd>
              {summary.awardedValue === null
                ? (commercialValuesVisible ? t('ministryRfqDetail.notAwarded') : t('ministryRfqDetail.withheld'))
                : formatCurrency(summary.awardedValue, summary.currencyCode, locale)}
            </dd>
          </div>
        </dl>
      </Card>

      {items.length > 0 ? (
        <Card title={t('ministryRfqDetail.items')}>
          <ul className="list-inside list-disc">
            {items.map((item, index) => (
              <li key={`${item.categoryCode}-${index}`}>
                {isArabic ? item.titleAr : item.titleEn} — {formatNumber(item.quantity, locale, 0)} {item.unitOfMeasureCode}
              </li>
            ))}
          </ul>
        </Card>
      ) : null}

      <Card title={t('ministryRfqDetail.bids')}>
        {bids.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('ministryRfqDetail.noBids')}</p>
        ) : (
          <Table caption={t('ministryRfqDetail.bids')}>
            <TableHead>
              <TableHeaderCell>{t('ministryRfqDetail.fields.supplier')}</TableHeaderCell>
              <TableHeaderCell>{t('ministryRfqDetail.fields.state')}</TableHeaderCell>
              <TableHeaderCell>{t('ministryRfqDetail.fields.submitted')}</TableHeaderCell>
              <TableHeaderCell>{t('ministryRfqDetail.fields.value')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {bids.map((bid) => (
                <TableRow key={bid.proposalCode}>
                  <TableCell>
                    {isArabic ? bid.supplierDisplayNameAr : bid.supplierDisplayNameEn}
                    <div className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
                      {bid.supplierCode} · {bid.proposalCode}
                    </div>
                    {bid.isAwarded ? (
                      <div className="mt-1"><Badge tone="success">{t('ministryRfqDetail.awarded')}</Badge></div>
                    ) : null}
                  </TableCell>
                  <TableCell><StatusChip machine="proposal" value={bid.state} /></TableCell>
                  <TableCell>{bid.submittedAt ? formatDate(bid.submittedAt, locale) : '—'}</TableCell>
                  <TableCell>
                    {bid.totalValue === null
                      ? <span style={{ color: 'var(--color-text-secondary)' }}>{t('ministryRfqDetail.withheld')}</span>
                      : formatCurrency(bid.totalValue, summary.currencyCode, locale)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>
    </div>
  )
}

/** The route's own component: reads the param and hands it down, so the page above is testable as a prop. */
export function MinistryRfqDetailRoute() {
  const { referenceCode } = useParams({ from: '/back-office/ministry/rfqs/$referenceCode' })
  return <MinistryRfqDetailPage referenceCode={referenceCode} />
}
