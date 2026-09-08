import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Badge, FilterBar, FilterField, Input, ListCard, PageHeading, Select, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../components/ui'
import { listComplianceDirectory, type ComplianceSupplier } from '../api/supplierDirectory'
import { nextPageParam } from '../api/listEnvelope'
import { formatDate } from '../lib/datetime'

/**
 * SCR-307: the whole registry as the compliance reviewer sees it.
 *
 * <p><b>Why the review queue is not enough.</b> The queue answers "what is waiting for me" and a case
 * leaves it the moment it is decided. Everything that happens to a supplier AFTERWARDS happened on no
 * screen: a certificate expires months later, the daily job moves it to Expired, BRULE-023 may suspend the
 * supplier for an award-critical one — and the only way a reviewer could look at that supplier was to
 * already know their reference code and type it into the address bar. F-6 found the same gap from the other
 * end, for decided applications.</p>
 *
 * <p><b>The three document counts are shown separately, never summed.</b> Expiring is a prompt and expired
 * is a bar; one combined number would tell a reviewer something needs attention without saying whether a
 * supplier is currently unable to hold a contract.</p>
 */
export function ComplianceDirectoryPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const [onboardingState, setOnboardingState] = useState('all')
  const [documentHealth, setDocumentHealth] = useState('all')
  const [search, setSearch] = useState('')

  const filters = {
    onboardingState: onboardingState === 'all' ? null : onboardingState,
    documentHealth: documentHealth === 'all' ? null : documentHealth,
    q: search.trim() || null,
  }

  const directoryQuery = useInfiniteQuery({
    queryKey: ['compliance-directory', filters.onboardingState, filters.documentHealth, filters.q],
    queryFn: ({ pageParam }) => listComplianceDirectory(pageParam, filters),
    initialPageParam: null as string | null,
    getNextPageParam: nextPageParam,
  })

  const suppliers = directoryQuery.data?.pages.flatMap((p) => p.data) ?? []

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('complianceDirectory.title')} subtitle={t('complianceDirectory.subtitle')} />

      <FilterBar>
        <FilterField label={t('complianceDirectory.filterState')}>
          <Select
            value={onboardingState}
            onValueChange={setOnboardingState}
            placeholder={t('complianceDirectory.filterState')}
            options={[
              { value: 'all', label: t('complianceDirectory.filterAll') },
              ...['Draft', 'Submitted', 'UnderReview', 'InfoRequested', 'Approved', 'Rejected']
                .map((state) => ({ value: state, label: t(`status.onboarding.${state}`) })),
            ]}
          />
        </FilterField>
        <FilterField label={t('complianceDirectory.filterHealth')}>
          <Select
            value={documentHealth}
            onValueChange={setDocumentHealth}
            placeholder={t('complianceDirectory.filterHealth')}
            options={[
              { value: 'all', label: t('complianceDirectory.filterAll') },
              { value: 'attention', label: t('complianceDirectory.healthAttention') },
              { value: 'ok', label: t('complianceDirectory.healthOk') },
            ]}
          />
        </FilterField>
        <FilterField label={t('complianceDirectory.search')} htmlFor="compliance-directory-search">
          <Input
            id="compliance-directory-search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t('complianceDirectory.searchPlaceholder')}
          />
        </FilterField>
      </FilterBar>

      <ListCard
        title={t('complianceDirectory.title')}
        isPending={directoryQuery.isPending}
        isError={directoryQuery.isError}
        isEmpty={suppliers.length === 0}
        loadingLabel={t('common.loading')}
        errorText={t('complianceDirectory.error')}
        emptyText={t('complianceDirectory.empty')}
        hasNextPage={directoryQuery.hasNextPage}
        isFetchingNextPage={directoryQuery.isFetchingNextPage}
        onLoadMore={() => directoryQuery.fetchNextPage()}
        loadMoreLabel={t('complianceDirectory.loadMore')}
      >
        <Table caption={t('complianceDirectory.title')}>
          <TableHead>
            <TableHeaderCell>{t('complianceDirectory.fields.name')}</TableHeaderCell>
            <TableHeaderCell>{t('complianceDirectory.fields.onboarding')}</TableHeaderCell>
            <TableHeaderCell>{t('complianceDirectory.fields.lifecycle')}</TableHeaderCell>
            <TableHeaderCell>{t('complianceDirectory.fields.documents')}</TableHeaderCell>
            <TableHeaderCell>{t('complianceDirectory.fields.registered')}</TableHeaderCell>
          </TableHead>
          <TableBody>
            {suppliers.map((s) => (
              <TableRow key={s.supplierCode}>
                <TableCell>
                  {/* The row is the route to the case. Without this the reviewer's only way into an
                      approved supplier was to type the code into the address bar - F-6 again. */}
                  <Link to="/back-office/review/$referenceCode" params={{ referenceCode: s.supplierCode }}>
                    {isArabic ? s.displayNameAr : s.displayNameEn}
                  </Link>
                  <div className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
                    {s.supplierCode}
                  </div>
                </TableCell>
                <TableCell><StatusChip machine="onboarding" value={s.onboardingState} /></TableCell>
                <TableCell>
                  {s.lifecycleState === 'None'
                    ? <span style={{ color: 'var(--color-text-secondary)' }}>—</span>
                    : <StatusChip machine="onboarding" value={s.lifecycleState} />}
                </TableCell>
                <TableCell><DocumentHealth supplier={s} /></TableCell>
                <TableCell>{formatDate(s.createdAt, isArabic ? 'ar' : 'en-GB')}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </ListCard>
    </div>
  )
}

/** The three counts, each with its own tone, and a single word when there is nothing to report. */
function DocumentHealth({ supplier }: { supplier: ComplianceSupplier }) {
  const { t } = useTranslation()
  const { expiredDocumentCount, expiringDocumentCount, rejectedDocumentCount } = supplier

  if (expiredDocumentCount + expiringDocumentCount + rejectedDocumentCount === 0) {
    return <span style={{ color: 'var(--color-text-secondary)' }}>{t('complianceDirectory.healthOk')}</span>
  }

  return (
    <div className="flex flex-wrap gap-1">
      {expiredDocumentCount > 0 ? (
        <Badge tone="danger">{t('complianceDirectory.expired', { count: expiredDocumentCount })}</Badge>
      ) : null}
      {expiringDocumentCount > 0 ? (
        <Badge tone="warning">{t('complianceDirectory.expiring', { count: expiringDocumentCount })}</Badge>
      ) : null}
      {rejectedDocumentCount > 0 ? (
        <Badge tone="danger">{t('complianceDirectory.rejected', { count: rejectedDocumentCount })}</Badge>
      ) : null}
    </div>
  )
}
