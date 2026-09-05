import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Badge, Button, Card, SkeletonList } from '../../components/ui'
import { formatNumber } from '../../lib/datetime'
import { getGovernanceOverview } from '../../api/governance'

/**
 * SCR-600, `/ministry`, `ministry_viewer`, P1.
 *
 * <p>Before this the persona held an EMPTY permission set - it could log in and reach nothing.</p>
 *
 * <p>Every figure is an aggregate. BRULE-086 grants "aggregate/governance metrics only", so this
 * screen has no row to click into and no name to show - which is why there is no table of suppliers
 * or RFQs here even though both would be the obvious thing to add.</p>
 */
export function MinistryOverviewPage() {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'

  const query = useQuery({ queryKey: ['ministry-overview'], queryFn: getGovernanceOverview })

  if (query.isLoading) return <SkeletonList label={t('common.loading')} />

  // Same shape the procurement dashboard uses for a failed load - a Card with the reason and a retry,
  // rather than a new ErrorPanel component this codebase does not have.
  if (query.isError || !query.data) {
    return (
      <Card title={t('ministry.title')}>
        <p>{t('ministry.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => void query.refetch()}>{t('ministry.retry')}</Button>
      </Card>
    )
  }

  const data = query.data

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
        {t('ministry.title')}
      </h1>

      {/* 2x2 on phones, widening with the viewport - the same KPI-row shape the procurement
          dashboard uses, for the 320px reflow reason recorded there. */}
      <ul className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {([
          ['suppliers', formatNumber(data.totalSuppliers, locale, 0)],
          ['rfqs', formatNumber(data.totalRfqs, locale, 0)],
          ['awards', formatNumber(data.totalAwards, locale, 0)],
          ['participation', formatNumber(data.averageProposalsPerRfq, locale, 1)],
        ] as const).map(([key, value]) => (
          <li key={key} className="rounded-[var(--radius-md)] p-3" style={{ border: '1px solid var(--color-border)' }}>
            <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t(`ministry.kpis.${key}`)}
            </p>
            <p className="num text-[length:var(--text-heading-md)]">{value}</p>
          </li>
        ))}
      </ul>

      {/*
        The one commercial figure, and the reason it says WHY rather than rendering blank: a viewer who
        sees an empty tile cannot tell policy from an empty ministry.
      */}
      <Card title={t('ministry.awardedValue')}>
        {data.commercialValuesVisible && data.totalAwardedValue !== null ? (
          <p className="text-[length:var(--text-h3)]">{formatNumber(data.totalAwardedValue, locale, 2)}</p>
        ) : (
          <div className="flex flex-col gap-2">
            <Badge tone="info">{t('ministry.commercialWithheld')}</Badge>
            <p style={{ color: 'var(--color-text-secondary)' }}>{t('ministry.commercialWithheldBody')}</p>
          </div>
        )}
      </Card>

      <Card title={t('ministry.suppliersByState')}>
        <CountList counts={data.suppliersByLifecycleState} machine="supplierLifecycle" locale={locale} />
      </Card>

      <Card title={t('ministry.rfqsByState')}>
        <CountList counts={data.rfqsByState} machine="rfq" locale={locale} />
      </Card>
    </div>
  )
}

function CountList({
  counts, machine, locale,
}: {
  counts: { key: string; count: number }[]
  machine: string
  locale: string
}) {
  const { t } = useTranslation()

  if (counts.length === 0) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('ministry.empty')}</p>
  }

  return (
    <ul className="flex flex-col gap-1">
      {counts.map((entry) => (
        <li key={entry.key} className="flex items-center justify-between gap-2">
          {/* Catalogue labels, not the raw enum name - the state machines already have authored copy. */}
          <span>{t(`status.${machine}.${entry.key}`, { defaultValue: entry.key })}</span>
          <span className="num">{formatNumber(entry.count, locale, 0)}</span>
        </li>
      ))}
    </ul>
  )
}
