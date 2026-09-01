import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Badge, Card, Input, Select, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../../components/ui'
import { searchBuyerOfferings } from '../../api/offerings'
import { fetchCategories } from '../../api/reference'

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
    return c ? (isArabic ? c.nameAr : c.nameEn) : code
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('offeringSearch.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('offeringSearch.subtitle')}
        </p>
      </div>

      <div className="flex flex-wrap gap-4">
        <div className="flex flex-col gap-1">
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>{t('offeringSearch.filterCategory')}</span>
          <Select
            value={categoryCode}
            onValueChange={setCategoryCode}
            placeholder={t('offeringSearch.filterCategory')}
            options={[{ value: 'all', label: t('offeringSearch.filterAll') }, ...categories.map((c) => ({ value: c.code, label: isArabic ? c.nameAr : c.nameEn }))]}
          />
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>{t('offeringSearch.searchPlaceholder')}</span>
          <Input value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t('offeringSearch.searchPlaceholder')} />
        </div>
      </div>

      <Card title={t('offeringSearch.title')}>
        {results.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('offeringSearch.empty')}</p>
        ) : (
          <Table caption={t('offeringSearch.title')}>
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
                      ? new Intl.NumberFormat(isArabic ? 'ar-SY-u-nu-latn' : 'en-US', { style: 'currency', currency: o.currencyCode ?? 'USD' }).format(o.priceAmount)
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
        )}
      </Card>
    </div>
  )
}
