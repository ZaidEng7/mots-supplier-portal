import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { FilterBar, FilterField, ListCard, PageHeading, SearchField, Select, StatusChip, Table, TableBody, TableCell, TableHead, TableRow } from '../../components/ui'
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
        <SearchField
          id="ministry-rfq-search"
          label={t('ministryRfqs.search')}
          placeholder={t('ministryRfqs.searchPlaceholder')}
          value={search}
          onChange={setSearch}
        />
      </FilterBar>

      <ListCard
        title={t('ministryRfqs.title')}
        query={query}
        isEmpty={rfqs.length === 0}
        skeletonRows={6}
        labels={{ loading: t('common.loading'), error: t('ministryRfqs.loadFailed'), empty: t('ministryRfqs.empty'), loadMore: t('ministryRfqs.loadMore') }}
      >
        <Table flush caption={t('ministryRfqs.title')}>
        <TableHead labels={[t('ministryRfqs.fields.tender'), t('ministryRfqs.fields.organization'), t('ministryRfqs.fields.state'), t('ministryRfqs.fields.bids'), t('ministryRfqs.fields.closes'), t('ministryRfqs.fields.awarded')]} />
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
