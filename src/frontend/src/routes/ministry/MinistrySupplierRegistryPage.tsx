import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Badge, FilterBar, FilterField, Input, ListCard, PageHeading, Select, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../../components/ui'
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
        <FilterField label={t('ministrySuppliers.search')} htmlFor="ministry-supplier-search">
          <Input
            id="ministry-supplier-search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('ministrySuppliers.searchPlaceholder')}
          />
        </FilterField>
      </FilterBar>

      <ListCard
        title={t('ministrySuppliers.title')}
        isPending={query.isPending}
        isError={query.isError}
        isEmpty={suppliers.length === 0}
        loadingLabel={t('common.loading')}
        errorText={t('ministrySuppliers.loadFailed')}
        emptyText={t('ministrySuppliers.empty')}
        skeletonRows={6}
        hasNextPage={query.hasNextPage}
        isFetchingNextPage={query.isFetchingNextPage}
        onLoadMore={() => query.fetchNextPage()}
        loadMoreLabel={t('ministrySuppliers.loadMore')}
      >
        <Table caption={t('ministrySuppliers.title')}>
        <TableHead>
          <TableHeaderCell>{t('ministrySuppliers.fields.supplier')}</TableHeaderCell>
          <TableHeaderCell>{t('ministrySuppliers.fields.categories')}</TableHeaderCell>
          <TableHeaderCell>{t('ministrySuppliers.fields.onboarding')}</TableHeaderCell>
          <TableHeaderCell>{t('ministrySuppliers.fields.standing')}</TableHeaderCell>
          <TableHeaderCell>{t('ministrySuppliers.fields.bids')}</TableHeaderCell>
          <TableHeaderCell>{t('ministrySuppliers.fields.won')}</TableHeaderCell>
          <TableHeaderCell>{t('ministrySuppliers.fields.registered')}</TableHeaderCell>
        </TableHead>
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
