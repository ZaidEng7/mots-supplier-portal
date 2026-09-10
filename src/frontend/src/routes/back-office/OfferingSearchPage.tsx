import { formatCurrency } from '../../lib/datetime'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import {Badge, FilterBar, FilterField, ListCard, PageHeading, SearchField, Select, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow} from '../../components/ui'
import { searchBuyerOfferings } from '../../api/offerings'
import { fetchCategories } from '../../api/reference'
import { localisedName } from '../../lib/localised'

/** FEAT-06.3/FR-OFF-004/FR-SRCH-001: procurement staff searching offerings across all suppliers
 * for RFQ invitation candidates. Results are already lifecycle-filtered server-side (FEAT-06.4) -
 * this page trusts the API's Active-only guarantee rather than re-deriving it client-side. */
export function OfferingSearchPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const [categoryCode, setCategoryCode] = useState<string>('all')
  const [query, setQuery] = useState('')

  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: fetchCategories })
  const resultsQuery = useQuery({
    queryKey: ['offering-search', categoryCode, query],
    queryFn: () => searchBuyerOfferings({ categoryCode: categoryCode === 'all' ? undefined : categoryCode, query: query || undefined }),
  })

  const categories = categoriesQuery.data ?? []
  const results = resultsQuery.data ?? []

  const categoryLabel = (code: string) => {
    const c = categories.find((c) => c.code === code)
    return localisedName(c, isArabic, code)
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <PageHeading title={t('offeringSearch.title')} subtitle={t('offeringSearch.subtitle')} />
      </div>

      <FilterBar>
        <FilterField label={t('offeringSearch.filterCategory')}>
          <Select
            value={categoryCode}
            onValueChange={setCategoryCode}
            placeholder={t('offeringSearch.filterCategory')}
            options={[{ value: 'all', label: t('offeringSearch.filterAll') }, ...categories.map((c) => ({ value: c.code, label: isArabic ? c.nameAr : c.nameEn }))]}
          />
        </FilterField>
        {/* The search box had a caption reading "Search by name…" - the placeholder, said twice, and
            attached to nothing. `SearchField` gives it a real `<label for>` and keeps the placeholder
            as the example it always was. */}
        <SearchField
          id="offering-search"
          label={t('offeringSearch.filterSearch')}
          placeholder={t('offeringSearch.searchPlaceholder')}
          value={query}
          onChange={setQuery}
        />
      </FilterBar>

      <ListCard
        title={t('offeringSearch.title')}
        query={resultsQuery}
        isEmpty={results.length === 0}
        labels={{ loading: t('common.loading'), error: t('common.loadFailed'), empty: t('offeringSearch.empty') }}
      >
          <Table flush caption={t('offeringSearch.title')}>
            <TableHead>
              <TableHeaderCell>{t('offeringSearch.fields.name')}</TableHeaderCell>
              <TableHeaderCell>{t('offeringSearch.supplier')}</TableHeaderCell>
              <TableHeaderCell>{t('offeringSearch.fields.category')}</TableHeaderCell>
              <TableHeaderCell>{t('offeringSearch.fields.price')}</TableHeaderCell>
              <TableHeaderCell>{t('offeringSearch.fields.attributes')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {results.map((o) => (
                <TableRow key={o.id}>
                  <TableCell>{isArabic ? o.nameAr : o.nameEn}</TableCell>
                  <TableCell>{isArabic ? o.supplierDisplayNameAr : o.supplierDisplayNameEn}</TableCell>
                  <TableCell>{categoryLabel(o.categoryCode)}</TableCell>
                  <TableCell>
                    {o.priceAmount !== null
                      ? formatCurrency(o.priceAmount, o.currencyCode, isArabic ? 'ar' : 'en-GB')
                      : '—'}
                  </TableCell>
                  <TableCell>
                    <div className="flex flex-wrap gap-1">
                      {o.attributes
                        ? Object.entries(o.attributes).map(([key, value]) => (
                            <Badge key={key} tone="info">{key}: {value}</Badge>
                          ))
                        : '—'}
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
      </ListCard>
    </div>
  )
}
