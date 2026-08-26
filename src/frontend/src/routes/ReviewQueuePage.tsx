import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Badge, Table, TableHead, TableHeaderCell, TableBody, TableRow, TableCell } from '../components/ui'
import { listReviewQueue } from '../api/review'

export function ReviewQueuePage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const queueQuery = useQuery({ queryKey: ['review-queue'], queryFn: listReviewQueue })
  const items = queueQuery.data ?? []

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
        {t('review.queue')}
      </h1>
      {items.length === 0 && !queueQuery.isLoading ? (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('review.noItems')}</p>
      ) : (
        <Table>
          <TableHead>
            <TableHeaderCell>{isArabic ? 'الاسم' : 'Name'}</TableHeaderCell>
            <TableHeaderCell>{isArabic ? 'رقم المرجع' : 'Reference'}</TableHeaderCell>
            <TableHeaderCell>{isArabic ? 'الحالة' : 'State'}</TableHeaderCell>
          </TableHead>
          <TableBody>
            {items.map((item) => (
              <TableRow key={item.referenceCode}>
                <TableCell>
                  <Link
                    to="/back-office/review/$referenceCode"
                    params={{ referenceCode: item.referenceCode }}
                    style={{ color: 'var(--color-text-brand)' }}
                  >
                    {isArabic ? item.displayNameAr : item.displayNameEn}
                  </Link>
                </TableCell>
                <TableCell>{item.referenceCode}</TableCell>
                <TableCell>
                  <Badge tone={item.onboardingState === 'InfoRequested' ? 'warning' : 'info'}>{item.onboardingState}</Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  )
}
