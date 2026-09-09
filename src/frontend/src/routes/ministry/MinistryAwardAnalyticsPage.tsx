import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import {Card, PageHeading, SkeletonList, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow} from '../../components/ui'
import { BarChart } from '../../components/charts/BarChart'
import { getMinistryAwardAnalytics, type MinistrySpendBucket } from '../../api/governance'
import { formatCurrency, formatNumber } from '../../lib/datetime'

/**
 * SCR-603, `/ministry/awards`, `ministry_viewer`.
 *
 * <p>Award trends and spend, by month, by category and by buying body. The COUNTS are the aggregate grant
 * BRULE-086 always gave; the money beside them is D-66's, and it disappears the moment the
 * commercial-visibility flag is switched off — leaving a screen that still answers "how much procurement is
 * happening" without answering "for how much".</p>
 *
 * <p>An award is counted once per category its tender touched. A single award across a multi-category tender
 * cannot be split between them without inventing a division nobody recorded, so the count is honest and the
 * total across categories can exceed the total number of awards. The screen says so rather than letting a
 * reader add the column up and find the wrong number.</p>
 */
export function MinistryAwardAnalyticsPage() {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'

  const query = useQuery({ queryKey: ['ministry-awards'], queryFn: getMinistryAwardAnalytics })

  if (query.isPending) return <SkeletonList label={t('common.loading')} />
  if (query.isError || !query.data) {
    return (
      <Card title={t('ministryAwards.title')}>
        <p>{t('ministryAwards.loadFailed')}</p>
      </Card>
    )
  }

  const { totalAwards, totalAwardedValue, byMonth, byCategory, byOrganization, commercialValuesVisible } = query.data

  const bucketTable = (title: string, buckets: MinistrySpendBucket[], keyHeader: string, note?: string) => (
    <Card title={title}>
      {note ? (
        <p className="mb-3 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
          {note}
        </p>
      ) : null}
      {buckets.length === 0 ? (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('ministryAwards.empty')}</p>
      ) : (
        <>
          {/*
            The chart reads the table beneath it, and sits above rather than instead of it.

            It charts VALUE when there is value to chart and AWARD COUNT when there is not, saying which
            in the label. D-57 withholds commercial figures outside a demonstration environment, and a
            chart of nothing but withheld months is a chart of nothing - while the award counts beside
            them are never withheld and are the fact the screen still has. A withheld value is a gap with
            its category still labelled, never a bar of height zero, which would assert that nothing was
            awarded.
          */}
          <div className="mb-4">
            {buckets.some((bucket) => bucket.value !== null) ? (
              <BarChart
                data={buckets.map((bucket) => ({ key: bucket.key, value: bucket.value }))}
                valueLabel={t('ministryAwards.fields.value')}
              />
            ) : (
              <BarChart
                data={buckets.map((bucket) => ({ key: bucket.key, value: bucket.awards }))}
                valueLabel={t('ministryAwards.fields.awards')}
              />
            )}
          </div>
        <Table caption={title}>
          <TableHead>
            <TableHeaderCell>{keyHeader}</TableHeaderCell>
            <TableHeaderCell>{t('ministryAwards.fields.awards')}</TableHeaderCell>
            <TableHeaderCell>{t('ministryAwards.fields.value')}</TableHeaderCell>
          </TableHead>
          <TableBody>
            {buckets.map((bucket) => (
              <TableRow key={bucket.key}>
                <TableCell>{bucket.key}</TableCell>
                <TableCell>{formatNumber(bucket.awards, locale, 0)}</TableCell>
                <TableCell>
                  {bucket.value === null
                    ? <span style={{ color: 'var(--color-text-secondary)' }}>{t('ministryAwards.withheld')}</span>
                    : formatCurrency(bucket.value, null, locale)}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        </>
      )}
    </Card>
  )

  return (
    <div className="flex flex-col gap-6">
      <div>
        <PageHeading title={t('ministryAwards.title')} subtitle={t('ministryAwards.subtitle')} />
      </div>

      {!commercialValuesVisible ? (
        <output className="block rounded-[var(--radius-md)] px-4 py-3 text-[length:var(--text-body-sm)]"
           style={{ backgroundColor: 'var(--warning-50)', color: 'var(--warning-600)' }}>
          {t('ministryAwards.valuesWithheld')}
        </output>
      ) : null}

      <ul className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <li>
          <Card title={t('ministryAwards.totalAwards')}>
            <p className="text-[length:var(--text-h3)]">{formatNumber(totalAwards, locale, 0)}</p>
          </Card>
        </li>
        <li>
          <Card title={t('ministryAwards.totalValue')}>
            <p className="text-[length:var(--text-h3)]">
              {totalAwardedValue === null ? t('ministryAwards.withheld') : formatCurrency(totalAwardedValue, null, locale)}
            </p>
          </Card>
        </li>
      </ul>

      {bucketTable(t('ministryAwards.byMonth'), byMonth, t('ministryAwards.fields.month'))}
      {bucketTable(t('ministryAwards.byCategory'), byCategory, t('ministryAwards.fields.category'), t('ministryAwards.categoryNote'))}
      {bucketTable(t('ministryAwards.byOrganization'), byOrganization, t('ministryAwards.fields.organization'))}
    </div>
  )
}
