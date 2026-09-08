import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { FilterBar, FilterField, Input, ListCard, PageHeading, Select, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../../components/ui'
import { listMinistryRfqs } from '../../api/governance'
import { nextPageParam } from '../../api/listEnvelope'
import { formatCurrency, formatDate } from '../../lib/datetime'

/**
 * SCR-602, `/ministry/rfqs`, `ministry_viewer`.
 *
 * <p><b>Every tender in the country, whoever is running it.</b> The row names the buying body and, where a
 * tender has been awarded, what it went for — neither of which any aggregate governance read carried. That
 * is D-66's widening, and `D-57` records the argument it overrode.</p>
 *
 * <p>Newest first: a monitor is read from the top. The review queue sorts the other way for the opposite
 * reason — a queue is worked from the case that has waited longest.</p>
 */
export function MinistryRfqMonitorPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'
  const [state, setState] = useState('all')
  const [search, setSearch] = useState('')

  const filters = { state: state === 'all' ? null : state, q: search.trim() || null }

  const query = useInfiniteQuery({
    queryKey: ['ministry-rfqs', filters.state, filters.q],
    queryFn: ({ pageParam }) => listMinistryRfqs(pageParam, filters),
    initialPageParam: null as string | null,
    getNextPageParam: nextPageParam,
  })

  const rfqs = query.data?.pages.flatMap((page) => page.data) ?? []

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('ministryRfqs.title')} subtitle={t('ministryRfqs.subtitle')} />

      <FilterBar>
        <FilterField label={t('ministryRfqs.filterState')}>
          <Select
            value={state}
            onValueChange={setState}
            placeholder={t('ministryRfqs.filterState')}
            options={[
              { value: 'all', label: t('ministryRfqs.filterAll') },
              ...['Published', 'SubmissionOpen', 'SubmissionClosed', 'UnderEvaluation', 'Awarded', 'Completed', 'Cancelled']
                .map((value) => ({ value, label: t(`status.rfq.${value}`, { defaultValue: value }) })),
            ]}
          />
        </FilterField>
        <FilterField label={t('ministryRfqs.search')} htmlFor="ministry-rfq-search">
          <Input
            id="ministry-rfq-search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('ministryRfqs.searchPlaceholder')}
          />
        </FilterField>
      </FilterBar>

      <ListCard
        title={t('ministryRfqs.title')}
        isPending={query.isPending}
        isError={query.isError}
        isEmpty={rfqs.length === 0}
        loadingLabel={t('common.loading')}
        errorText={t('ministryRfqs.loadFailed')}
        emptyText={t('ministryRfqs.empty')}
        skeletonRows={6}
        hasNextPage={query.hasNextPage}
        isFetchingNextPage={query.isFetchingNextPage}
        onLoadMore={() => query.fetchNextPage()}
        loadMoreLabel={t('ministryRfqs.loadMore')}
      >
        <Table caption={t('ministryRfqs.title')}>
        <TableHead>
          <TableHeaderCell>{t('ministryRfqs.fields.tender')}</TableHeaderCell>
          <TableHeaderCell>{t('ministryRfqs.fields.organization')}</TableHeaderCell>
          <TableHeaderCell>{t('ministryRfqs.fields.state')}</TableHeaderCell>
          <TableHeaderCell>{t('ministryRfqs.fields.bids')}</TableHeaderCell>
          <TableHeaderCell>{t('ministryRfqs.fields.closes')}</TableHeaderCell>
          <TableHeaderCell>{t('ministryRfqs.fields.awarded')}</TableHeaderCell>
        </TableHead>
        <TableBody>
          {rfqs.map((rfq) => (
            <TableRow key={rfq.referenceCode}>
              <TableCell>
                <Link to="/back-office/ministry/rfqs/$referenceCode" params={{ referenceCode: rfq.referenceCode }}>
                  {isArabic ? rfq.titleAr : rfq.titleEn}
                </Link>
                <div className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
                  {rfq.referenceCode}
                </div>
              </TableCell>
              <TableCell>{isArabic ? rfq.organizationNameAr : rfq.organizationNameEn}</TableCell>
              <TableCell><StatusChip machine="rfq" value={rfq.state} /></TableCell>
              <TableCell>
                {t('ministryRfqs.bidsOfInvited', { bids: rfq.submittedProposals, invited: rfq.invitedSuppliers })}
              </TableCell>
              <TableCell>
                {rfq.submissionClosesAt ? formatDate(rfq.submissionClosesAt, locale) : '—'}
              </TableCell>
              <TableCell>
                {/* Null here means one of two different things, and the em dash covers both: this
                    tender has not been awarded, or the commercial-visibility flag is off. The detail
                    page says which - a list row has no room to. */}
                {rfq.awardedValue === null
                  ? '—'
                  : formatCurrency(rfq.awardedValue, rfq.currencyCode, locale)}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
        </Table>
      </ListCard>
    </div>
  )
}
