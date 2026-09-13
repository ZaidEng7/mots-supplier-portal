// SCR-604 at /ministry/categories, for ministry_viewer.
//
// One of the two Ministry screens that were never refused and were absent anyway (T-100). SCR-601, 602, 603 and 606 wait on
// D-57's signature; this one never needed it, because every figure is a count and none of them is commercial.
//
// THE EMPTY ROWS ARE THE CONTENT. A coverage screen is read to find the categories the market is not serving, so a category
// with no suppliers is listed with its zeroes and counted in the summary - not filtered out, which is what a list built from
// the supplier links would have done while looking complete. The headline is the GAP rather than the total: "how many
// categories has nobody registered for" is the question this screen answers.
//
// TWO SUPPLIER COLUMNS, NOT ONE. Approved is the pool a tender can draw on; active is who can actually trade today. They
// differ exactly when somebody is suspended, and a single number would show a category as covered when its only supplier
// cannot bid. A category that was tendered and never awarded is flagged too: that is a market being asked and not answered.
//
// The list is STATED to be flat rather than drawn as a tree. The inventory row calls SCR-604 a category TREE; the reference
// list is flat by design (MSP-54), and a screen that rendered a hierarchy would be inventing one.
//
// The coverage chart sits on the screen whose numbers it draws rather than being moved to a reporting page of its own.

import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Badge, Button, Card, PageHeading, SkeletonTable, Table, TableBody, TableCell, TableHead, TableRow } from '../../components/ui'
import { CoverageChart } from '../../components/charts/CoverageChart'
import { formatNumber } from '../../lib/datetime'
import { getCategoryCoverage } from '../../api/governance'

export function CategoryCoveragePage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  const query = useQuery({ queryKey: ['ministry-categories'], queryFn: getCategoryCoverage })

  if (query.isPending) return <SkeletonTable label={t('common.loading')} rows={6} />

  if (query.isError || !query.data) {
    return (
      <Card title={t('categoryCoverage.title')}>
        <p>{t('categoryCoverage.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => void query.refetch()}>{t('ministry.retry')}</Button>
      </Card>
    )
  }

  const { categories, categoriesWithNoActiveSupplier, categoriesAreFlat } = query.data

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('categoryCoverage.title')} subtitle={t('categoryCoverage.subtitle')} />

      <Card title={t('categoryCoverage.summary')}>
        <p className="text-[length:var(--text-h3)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('categoryCoverage.uncovered', {
            count: categoriesWithNoActiveSupplier,
            total: categories.length,
          })}
        </p>
        {categoriesAreFlat ? (
          <p className="mt-2 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('categoryCoverage.flatNote')}
          </p>
        ) : null}
      </Card>

      <Card title={t('categoryCoverage.title')}>
        {categories.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('categoryCoverage.empty')}</p>
        ) : (
          <>
          <div className="mb-4">
            <CoverageChart
              data={categories.map((category) => ({
                key: category.categoryCode,
                label: isArabic ? category.nameAr : category.nameEn,
                total: category.approvedSuppliers,
                covered: category.activeSuppliers,
              }))}
            />
          </div>
          <Table caption={t('categoryCoverage.title')}>
            <TableHead labels={[t('categoryCoverage.fields.category'), t('categoryCoverage.fields.approved'), t('categoryCoverage.fields.active'), t('categoryCoverage.fields.offerings'), t('categoryCoverage.fields.tenders'), t('categoryCoverage.fields.awarded')]} />
            <TableBody>
              {categories.map((category) => (
                <TableRow key={category.categoryCode}>
                  <TableCell>
                    {isArabic ? category.nameAr : category.nameEn}
                    {category.activeSuppliers === 0 ? (
                      <span className="ms-2">
                        <Badge tone="warning">{t('categoryCoverage.noSupplier')}</Badge>
                      </span>
                    ) : null}
                  </TableCell>
                  <TableCell>{formatNumber(category.approvedSuppliers, locale, 0)}</TableCell>
                  <TableCell>{formatNumber(category.activeSuppliers, locale, 0)}</TableCell>
                  <TableCell>{formatNumber(category.activeOfferings, locale, 0)}</TableCell>
                  <TableCell>{formatNumber(category.tenders, locale, 0)}</TableCell>
                  <TableCell>
                    {formatNumber(category.awardedTenders, locale, 0)}
                    {category.tenders > 0 && category.awardedTenders === 0 ? (
                      <span className="ms-2">
                        <Badge tone="info">{t('categoryCoverage.noAward')}</Badge>
                      </span>
                    ) : null}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          </>
        )}
      </Card>
    </div>
  )
}
