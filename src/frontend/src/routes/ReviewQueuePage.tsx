import { useTranslation } from 'react-i18next'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Badge, Button, Table, TableHead, TableHeaderCell, TableBody, TableRow, TableCell } from '../components/ui'
import { listReviewQueue } from '../api/review'

export function ReviewQueuePage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  // MSP-84: the queue is a table applications are inserted into continuously - a page-one-only
  // fetch would silently hide everything after the first 50, no error, no empty state, nothing
  // visibly wrong. useInfiniteQuery + the Load more button below is the consumer half of the
  // keyset-paged backend; loading page one and stopping there would recreate exactly that bug.
  const queueQuery = useInfiniteQuery({
    queryKey: ['review-queue'],
    queryFn: ({ pageParam }) => listReviewQueue(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : undefined),
  })
  const items = queueQuery.data?.pages.flatMap((p) => p.items) ?? []

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
      {queueQuery.hasNextPage ? (
        <Button
          variant="secondary"
          isLoading={queueQuery.isFetchingNextPage}
          onClick={() => queueQuery.fetchNextPage()}
        >
          {t('review.loadMore')}
        </Button>
      ) : null}
    </div>
  )
}
