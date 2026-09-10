import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Badge, FilterBar, FilterField, ListCard, PageHeading, SearchField, Select, StatusChip, Table, TableBody, TableCell, TableHead, TableRow } from '../../components/ui'
import { listMinistrySuppliers } from '../../api/governance'
import { nextPageParam } from '../../api/listEnvelope'
import { formatCurrency, formatDate, formatNumber } from '../../lib/datetime'

/**
 * SCR-601, `/ministry/suppliers`, `ministry_viewer`.
 *
 * <p>The registry as an overseer reads it: who is registered, what they are registered for, whether they can
 * currently trade, and what they have won. Under D-66 the last of those carries a value.</p>
 *
 * <p><b>Not the compliance directory.</b> SCR-307 shows a reviewer the state of each supplier's DOCUMENTS,
 * which is that persona's work and nobody else's business. This one shows population and participation.</p>
 */
export function MinistrySupplierRegistryPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'
  const [lifecycleState, setLifecycleState] = useState('all')
  const [search, setSearch] = useState('')

  const filters = {
    lifecycleState: lifecycleState === 'all' ? null : lifecycleState,
    q: search.trim() || null,
  }

  const query = useInfiniteQuery({
    queryKey: ['ministry-suppliers', filters.lifecycleState, filters.q],
    queryFn: ({ pageParam }) => listMinistrySuppliers(pageParam, filters),
    initialPageParam: null as string | null,
    getNextPageParam: nextPageParam,
  })

  const suppliers = query.data?.pages.flatMap((page) => page.data) ?? []

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('ministrySuppliers.title')} subtitle={t('ministrySuppliers.subtitle')} />

      <FilterBar>
        <FilterField label={t('ministrySuppliers.filterState')}>
          <Select
            value={lifecycleState}
            onValueChange={setLifecycleState}
            placeholder={t('ministrySuppliers.filterState')}
            options={[
              { value: 'all', label: t('ministrySuppliers.filterAll') },
              { value: 'Active', label: t('status.onboarding.Active') },
              { value: 'Suspended', label: t('status.onboarding.Suspended') },
              { value: 'Deactivated', label: t('status.onboarding.Deactivated') },
            ]}
          />
        </FilterField>
        <SearchField
          id="ministry-supplier-search"
          label={t('ministrySuppliers.search')}
          placeholder={t('ministrySuppliers.searchPlaceholder')}
          value={search}
          onChange={setSearch}
        />
      </FilterBar>

      <ListCard
        title={t('ministrySuppliers.title')}
        query={query}
        isEmpty={suppliers.length === 0}
        skeletonRows={6}
        labels={{ loading: t('common.loading'), error: t('ministrySuppliers.loadFailed'), empty: t('ministrySuppliers.empty'), loadMore: t('ministrySuppliers.loadMore') }}
      >
        <Table flush caption={t('ministrySuppliers.title')}>
        <TableHead labels={[t('ministrySuppliers.fields.supplier'), t('ministrySuppliers.fields.categories'), t('ministrySuppliers.fields.onboarding'), t('ministrySuppliers.fields.standing'), t('ministrySuppliers.fields.bids'), t('ministrySuppliers.fields.won'), t('ministrySuppliers.fields.registered')]} />
        <TableBody>
          {suppliers.map((supplier) => (
            <TableRow key={supplier.supplierCode}>
              <TableCell>
                {isArabic ? supplier.displayNameAr : supplier.displayNameEn}
                <div className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
                  {supplier.supplierCode}
                </div>
              </TableCell>
              <TableCell>
                <div className="flex flex-wrap gap-1">
                  {supplier.categoryCodes.length === 0
                    ? <span style={{ color: 'var(--color-text-secondary)' }}>—</span>
                    : supplier.categoryCodes.map((code) => <Badge key={code} tone="info">{code}</Badge>)}
                </div>
              </TableCell>
              <TableCell><StatusChip machine="onboarding" value={supplier.onboardingState} /></TableCell>
              <TableCell>
                {supplier.lifecycleState === 'None'
                  ? <span style={{ color: 'var(--color-text-secondary)' }}>—</span>
                  : <StatusChip machine="onboarding" value={supplier.lifecycleState} />}
              </TableCell>
              <TableCell>{formatNumber(supplier.submittedProposals, locale, 0)}</TableCell>
              <TableCell>
                {formatNumber(supplier.awardsWon, locale, 0)}
                {/* The count is the aggregate BRULE-086 always granted; the value beside it is what
                    D-66 added, and it is null while the flag is off rather than zero. */}
                {supplier.awardedValue !== null && supplier.awardsWon > 0 ? (
                  <div className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
                    {formatCurrency(supplier.awardedValue, null, locale)}
                  </div>
                ) : null}
              </TableCell>
              <TableCell>{formatDate(supplier.registeredAt, locale)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
        </Table>
      </ListCard>
    </div>
  )
}
