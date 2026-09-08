import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { Badge, Button, Card, Input, Select, SkeletonTable, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../../components/ui'
import { listSupplierDirectory } from '../../api/supplierDirectory'
import { nextPageParam } from '../../api/listEnvelope'
import { fetchCategories } from '../../api/reference'

/**
 * SCR-402: the supplier directory a buyer browses before deciding whom to invite.
 *
 * <p><b>Why this is not the offering search.</b> That screen searches catalogue ENTRIES, so a registered
 * supplier with no offerings recorded was invisible to the officer choosing invitees — and recording a
 * catalogue is optional. This lists the companies, with their categories and how many live offerings each
 * one has, so "nothing in the catalogue yet" is a fact on the row instead of an absence from the list.</p>
 *
 * <p><b>Suspended suppliers are listed, greyed by their own status chip rather than hidden.</b> A buyer who
 * knows a company is registered must be able to find it; hiding it produces the harder question — where did
 * they go — with no answer on any screen.</p>
 */
export function SupplierDirectoryPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const [category, setCategory] = useState('all')
  const [lifecycleState, setLifecycleState] = useState('all')
  const [search, setSearch] = useState('')

  const filters = {
    category: category === 'all' ? null : category,
    lifecycleState: lifecycleState === 'all' ? null : lifecycleState,
    q: search.trim() || null,
  }

  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: fetchCategories })

  // Paged, not first-page-only. The registry grows without bound and a page-one fetch would hide every
  // supplier after the first twenty with nothing visibly wrong - the defect MSP-84 recorded on the review
  // queue, which is the same shape of list.
  const directoryQuery = useInfiniteQuery({
    queryKey: ['supplier-directory', filters.category, filters.lifecycleState, filters.q],
    queryFn: ({ pageParam }) => listSupplierDirectory(pageParam, filters),
    initialPageParam: null as string | null,
    getNextPageParam: nextPageParam,
  })

  const suppliers = directoryQuery.data?.pages.flatMap((p) => p.data) ?? []
  const categories = categoriesQuery.data ?? []

  const categoryLabel = (code: string) => {
    const match = categories.find((c) => c.code === code)
    return match ? (isArabic ? match.nameAr : match.nameEn) : code
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('supplierDirectory.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('supplierDirectory.subtitle')}
        </p>
      </div>

      <div className="flex flex-wrap gap-4">
        <div className="flex flex-col gap-1">
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('supplierDirectory.filterCategory')}
          </span>
          <Select
            value={category}
            onValueChange={setCategory}
            placeholder={t('supplierDirectory.filterCategory')}
            options={[
              { value: 'all', label: t('supplierDirectory.filterAll') },
              ...categories.map((c) => ({ value: c.code, label: isArabic ? c.nameAr : c.nameEn })),
            ]}
          />
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('supplierDirectory.filterState')}
          </span>
          <Select
            value={lifecycleState}
            onValueChange={setLifecycleState}
            placeholder={t('supplierDirectory.filterState')}
            options={[
              { value: 'all', label: t('supplierDirectory.filterAll') },
              // §7.1 groups onboarding and lifecycle states in one table, and the `onboarding`
              // machine is where both live - there is no separate lifecycle namespace to read from.
              { value: 'Active', label: t('status.onboarding.Active') },
              { value: 'Suspended', label: t('status.onboarding.Suspended') },
            ]}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-[length:var(--text-caption)]" htmlFor="supplier-directory-search" style={{ color: 'var(--color-text-secondary)' }}>
            {t('supplierDirectory.search')}
          </label>
          <Input
            id="supplier-directory-search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t('supplierDirectory.searchPlaceholder')}
          />
        </div>
      </div>

      <Card title={t('supplierDirectory.title')}>
        {directoryQuery.isPending ? (
          <SkeletonTable rows={5} />
        ) : directoryQuery.isError ? (
          <p style={{ color: 'var(--color-danger)' }}>{t('supplierDirectory.error')}</p>
        ) : suppliers.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('supplierDirectory.empty')}</p>
        ) : (
          <>
            <Table caption={t('supplierDirectory.title')}>
              <TableHead>
                <TableHeaderCell>{t('supplierDirectory.fields.name')}</TableHeaderCell>
                <TableHeaderCell>{t('supplierDirectory.fields.code')}</TableHeaderCell>
                <TableHeaderCell>{t('supplierDirectory.fields.categories')}</TableHeaderCell>
                <TableHeaderCell>{t('supplierDirectory.fields.offerings')}</TableHeaderCell>
                <TableHeaderCell>{t('supplierDirectory.fields.location')}</TableHeaderCell>
                <TableHeaderCell>{t('supplierDirectory.fields.state')}</TableHeaderCell>
              </TableHead>
              <TableBody>
                {suppliers.map((s) => (
                  <TableRow key={s.supplierCode}>
                    <TableCell>{isArabic ? s.displayNameAr : s.displayNameEn}</TableCell>
                    <TableCell>{s.supplierCode}</TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-1">
                        {s.categoryCodes.length === 0
                          ? <span style={{ color: 'var(--color-text-secondary)' }}>{t('supplierDirectory.noCategories')}</span>
                          : s.categoryCodes.map((code) => (
                              <Badge key={code} tone="info">{categoryLabel(code)}</Badge>
                            ))}
                      </div>
                    </TableCell>
                    <TableCell>{s.offeringCount}</TableCell>
                    <TableCell>{s.city ?? '—'}</TableCell>
                    <TableCell><StatusChip machine="onboarding" value={s.lifecycleState} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>

            {directoryQuery.hasNextPage ? (
              <div className="mt-4">
                <Button
                  variant="secondary"
                  onClick={() => directoryQuery.fetchNextPage()}
                  disabled={directoryQuery.isFetchingNextPage}
                >
                  {t('supplierDirectory.loadMore')}
                </Button>
              </div>
            ) : null}
          </>
        )}
      </Card>
    </div>
  )
}
