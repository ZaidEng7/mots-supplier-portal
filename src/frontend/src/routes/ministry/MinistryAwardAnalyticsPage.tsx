// SCR-603 at /ministry/awards, for ministry_viewer.
//
// Award trends and spend, by month, by category and by buying body. The COUNTS are the aggregate grant BRULE-086 always
// gave; the money beside them is D-66's, and it disappears the moment the commercial-visibility flag is switched off -
// leaving a screen that still answers "how much procurement is happening" without answering "for how much".
//
// An award is counted once per category its tender touched. A single award across a multi-category tender cannot be split
// between them without inventing a division nobody recorded, so the count is honest and the total across categories can
// exceed the total number of awards. The screen says so rather than letting a reader add the column up and find the wrong
// number.
//
// THE TWO ORIENTATIONS: 'columns' for a series read along time, where the data's own order is the meaning, and 'ranked' for
// a comparison of unordered things, where the question is which is biggest - and where a category name like "Tour
// operations" is prose that does not fit under a column.
//
// ONE ORDER, used by the chart AND by the table under it. A ranked chart sorts by the measure - that is what makes it
// ranked - and the table beneath kept the server's order, so the tallest bar and the first row were different categories.
// Sorting once here and handing the same array to both is the only arrangement in which the two can be read against each
// other. A withheld figure sorts LAST rather than as zero: it is a number nobody is allowed to see, not a small one.
//
// A CATEGORY ARRIVES AS A CODE and is read as a word. The by-category buckets group on CategoryCode, so this screen used to
// print tour_operations on a chart axis and in a table column - a domain identifier, in one language, with an underscore in
// it - on the one chart whose form was chosen because a category name is prose too long to sit under a column. The endpoint
// now carries both names; months and buying bodies carry none, because a date and an organisation's own name are already
// words.
//
// THE CHART reads the table beneath it and sits above rather than instead of it. It charts VALUE when there is value to
// chart and AWARD COUNT when there is not, saying which in the label: D-57 withholds commercial figures outside a
// demonstration environment, and a chart of nothing but withheld months is a chart of nothing - while the award counts
// beside them are never withheld and are the fact the screen still has. A withheld value is a gap with its category still
// labelled, never a bar of height zero, which would assert that nothing was awarded. Money on the chart is written the way
// money is written in the table beneath it: a bar chart cannot know a currency, so the screen that does supplies the
// formatter.
//
// THE WITHHELD NOTICE is drawn as a panel rather than a strip, and states it as a decision: a reader who meets a blank money
// column needs to know it is withheld on purpose and not missing through a fault. The screen keeps charting award counts
// underneath, which the comp's own version does not.
//
// THE TWO HEADLINE FIGURES are drawn by the component the other five dashboards use. They were cards inside list items with
// the number at heading size, which is the shape every dashboard had before Metric existed - and the last copy of it. The
// withheld case stays a WORD rather than a figure: under the current visibility policy there is no number to show, and a
// zero would say something false.
//
// The two ranked tables sit SIDE BY SIDE, as the comp has them: two answers to the same question asked of different
// dimensions, and reading one after the other down a full-width column made them look like two unrelated screens. One
// column below the layout breakpoint, where side by side would make both unreadable rather than comparable.

import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Card, Metric, MetricRow, PageHeading, SkeletonList, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow} from '../../components/ui'
import { BarChart } from '../../components/charts/BarChart'
import { getMinistryAwardAnalytics, type MinistrySpendBucket } from '../../api/governance'
import { formatCurrency, formatNumber } from '../../lib/datetime'

export function MinistryAwardAnalyticsPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

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

  const bucketTable = (
    title: string,
    unordered: MinistrySpendBucket[],
    keyHeader: string,
    orientation: 'columns' | 'ranked',
    note?: string,
  ) => {
    const ordered = orientation === 'ranked'
      ? [...unordered].sort((a, b) => (b.value ?? b.awards ?? -1) - (a.value ?? a.awards ?? -1))
      : unordered

    const buckets = ordered.map((bucket) => ({
      ...bucket,
      key: (isArabic ? bucket.nameAr : bucket.nameEn) ?? bucket.key,
    }))
    return (
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
          <div className="mb-4">
            {buckets.some((bucket) => bucket.value !== null) ? (
              <BarChart
                data={buckets.map((bucket) => ({ key: bucket.key, value: bucket.value }))}
                valueLabel={t('ministryAwards.fields.value')}
                orientation={orientation}
                formatValue={(value) => formatCurrency(value, null, locale)}
              />
            ) : (
              <BarChart
                data={buckets.map((bucket) => ({ key: bucket.key, value: bucket.awards }))}
                valueLabel={t('ministryAwards.fields.awards')}
                orientation={orientation}
                formatValue={(value) => formatNumber(value, locale, 0)}
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
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <PageHeading title={t('ministryAwards.title')} subtitle={t('ministryAwards.subtitle')} />
      </div>

      {!commercialValuesVisible ? (
        <output
          className="block rounded-[var(--radius-lg)] p-4"
          style={{ backgroundColor: 'var(--color-warning-bg)', border: '1px solid var(--color-warning-fg)' }}
        >
          <p className="font-[var(--fw-semibold)]" style={{ color: 'var(--color-warning-fg)' }}>
            {t('ministryAwards.valuesWithheldTitle')}
          </p>
          <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('ministryAwards.valuesWithheld')}
          </p>
        </output>
      ) : null}

      <MetricRow>
        <Metric label={t('ministryAwards.totalAwards')} value={formatNumber(totalAwards, locale, 0)} />
        <Metric
          label={t('ministryAwards.totalValue')}
          value={totalAwardedValue === null ? t('ministryAwards.withheld') : formatCurrency(totalAwardedValue, null, locale)}
        />
      </MetricRow>

      {bucketTable(t('ministryAwards.byMonth'), byMonth, t('ministryAwards.fields.month'), 'columns')}

      <div className="grid gap-6 lg:grid-cols-2 lg:items-start">
        {bucketTable(t('ministryAwards.byCategory'), byCategory, t('ministryAwards.fields.category'), 'ranked', t('ministryAwards.categoryNote'))}
        {bucketTable(t('ministryAwards.byOrganization'), byOrganization, t('ministryAwards.fields.organization'), 'ranked')}
      </div>
    </div>
  )
}
