import { useTranslation } from 'react-i18next'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Badge, Button, Table, TableHead, TableHeaderCell, TableBody, TableRow, TableCell } from '../components/ui'
import { listReviewQueue } from '../api/review'

/** FEAT-03.6/FR-ONB-012 [ASSUMPTION]: no SLA window is defined anywhere in the product docs
 * (BACKLOG.md's STORY-03.6.1 only says "shows age/SLA", no numbers) - 48h "at risk" / 120h
 * (5 business days) "overdue" is a reasonable default for a compliance review queue, not a
 * confirmed business requirement. Revisit once Product/Procurement actually sets a real window,
 * same as ASSUMPTIONS.md's other interim defaults (e.g. ASM-010's registration-mode default). */
const AT_RISK_HOURS = 48
const OVERDUE_HOURS = 120

function ageTone(hours: number): 'success' | 'warning' | 'danger' {
  if (hours >= OVERDUE_HOURS) return 'danger'
  if (hours >= AT_RISK_HOURS) return 'warning'
  return 'success'
}

function formatAge(hours: number, isArabic: boolean): string {
  const days = Math.floor(hours / 24)
  if (days >= 1) return isArabic ? `${days} يوم` : `${days}d`
  return isArabic ? `${Math.max(0, Math.floor(hours))} ساعة` : `${Math.max(0, Math.floor(hours))}h`
}

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
            <TableHeaderCell>{t('review.age')}</TableHeaderCell>
          </TableHead>
          <TableBody>
            {items.map((item) => {
              const ageHours = (Date.now() - new Date(item.enteredQueueAt).getTime()) / (1000 * 60 * 60)
              return (
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
                <TableCell>
                  <Badge tone={ageTone(ageHours)}>{formatAge(ageHours, isArabic)}</Badge>
                </TableCell>
              </TableRow>
              )
            })}
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
