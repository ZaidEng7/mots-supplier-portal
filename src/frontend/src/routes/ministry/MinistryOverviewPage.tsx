import { useTranslation } from 'react-i18next'
import { Metric, MetricRow } from '../../components/ui'
import { useQuery } from '@tanstack/react-query'
import {Badge, Button, Card, PageHeading, SkeletonList} from '../../components/ui'
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
      <PageHeading title={t('ministry.title')} />

      {/* 2x2 on phones, widening with the viewport - the same KPI-row shape the procurement
          dashboard uses, for the 320px reflow reason recorded there. */}
      <MetricRow>
        {([
          ['suppliers', formatNumber(data.totalSuppliers, locale, 0)],
          ['rfqs', formatNumber(data.totalRfqs, locale, 0)],
          ['awards', formatNumber(data.totalAwards, locale, 0)],
          ['participation', formatNumber(data.averageProposalsPerRfq, locale, 1)],
        ] as const).map(([key, value]) => (
          <Metric key={key} label={t(`ministry.kpis.${key}`)} value={value} />
        ))}
      </MetricRow>

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
        {/*
          "onboarding", not "supplierLifecycle". There is no status.supplierLifecycle machine in the
          catalogue - §7.1 groups onboarding and lifecycle in one table and the labels live under
          `onboarding` - so this asked for a machine that does not exist and the defaultValue fell through to
          the RAW ENUM NAME. The Ministry's screen was showing "Active", "Suspended" and "None" in an Arabic
          interface, beside RFQ states that were localised correctly.
        */}
        <CountList
          counts={data.suppliersByLifecycleState}
          machine="onboarding"
          locale={locale}
          fallback={t('ministry.unlabelledLifecycle')}
        />
      </Card>

      <Card title={t('ministry.rfqsByState')}>
        <CountList counts={data.rfqsByState} machine="rfq" locale={locale} />
      </Card>
    </div>
  )
}

function CountList({
  counts, machine, locale, fallback,
}: {
  counts: { key: string; count: number }[]
  machine: string
  locale: string
  /** Shown for a member the state-label catalogue has no row for. Without it the raw enum name reaches the
   *  reader, which is how "None" ended up on the Ministry's dashboard. */
  fallback?: string
}) {
  const { t } = useTranslation()

  if (counts.length === 0) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('ministry.empty')}</p>
  }

  return (
    <ul className="flex flex-col gap-1">
      {counts.map((entry) => (
        <li key={entry.key} className="flex items-center justify-between gap-2">
          {/*
            Catalogue labels, and a NAMED fallback rather than the raw enum name.

            This screen was rendering "Active", "Suspended" and "None" in an Arabic interface: it asked for
            status.supplierLifecycle.* and no such machine exists (§7.1 groups onboarding and lifecycle under
            `onboarding`), so every key fell through to itself. Pointing at the real machine fixes two of the
            three; SupplierLifecycleState.None has no §7.1 row at all, and StatusChip's coverage guard refuses
            an authored label for exactly that reason - a state label is the document's to write.

            So the fallback is this SCREEN's own caption, which is this file's to write. It says what the group
            counts without claiming to name a status.
          */}
          <span>{t(`status.${machine}.${entry.key}`, { defaultValue: fallback ?? entry.key })}</span>
          <span className="num">{formatNumber(entry.count, locale, 0)}</span>
        </li>
      ))}
    </ul>
  )
}
