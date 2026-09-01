import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Badge, Button, Select, Table, TableHead, TableHeaderCell, TableBody, TableRow, TableCell, useToast } from '../components/ui'
import { listReviewQueue, claimReviewItem, unassignReviewItem, type ReviewQueueItem } from '../api/review'
import { useAuthStore } from '../lib/authStore'
import { invalidateQuietly } from '../lib/queryClient'

/** FEAT-03.6/FR-ONB-012 [ASSUMPTION]: no SLA window is defined anywhere in the product docs
 * (BACKLOG.md's STORY-03.6.1 only says "shows age/SLA", no numbers) - 48h "at risk" / 120h
 * (5 business days) "overdue" is a reasonable default for a compliance review queue, not a
 * confirmed business requirement. Revisit once Product/Procurement actually sets a real window,
 * same as ASSUMPTIONS.md's other interim defaults (e.g. ASM-010's registration-mode default). */
export const AT_RISK_HOURS = 48
export const OVERDUE_HOURS = 120

export function ageTone(hours: number): 'success' | 'warning' | 'danger' {
  if (hours >= OVERDUE_HOURS) return 'danger'
  if (hours >= AT_RISK_HOURS) return 'warning'
  return 'success'
}

export function formatAge(hours: number, isArabic: boolean): string {
  const days = Math.floor(hours / 24)
  if (days >= 1) return isArabic ? `${days} يوم` : `${days}d`
  return isArabic ? `${Math.max(0, Math.floor(hours))} ساعة` : `${Math.max(0, Math.floor(hours))}h`
}

const STATE_OPTIONS = ['Submitted', 'UnderReview', 'InfoRequested']

export function ReviewQueuePage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const queryClient = useQueryClient()
  const { notify } = useToast()
  const currentUserId = useAuthStore((s) => s.claims?.userId)
  const [stateFilter, setStateFilter] = useState<string>('all')
  const [assigneeFilter, setAssigneeFilter] = useState<string>('all')

  const filters = {
    state: stateFilter === 'all' ? null : stateFilter,
    assignedTo: assigneeFilter === 'all' ? null : assigneeFilter,
  }

  // MSP-84: the queue is a table applications are inserted into continuously - a page-one-only
  // fetch would silently hide everything after the first 50, no error, no empty state, nothing
  // visibly wrong. useInfiniteQuery + the Load more button below is the consumer half of the
  // keyset-paged backend; loading page one and stopping there would recreate exactly that bug.
  const queueQuery = useInfiniteQuery({
    queryKey: ['review-queue', filters.state, filters.assignedTo],
    queryFn: ({ pageParam }) => listReviewQueue(pageParam, filters),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : undefined),
  })
  const items = queueQuery.data?.pages.flatMap((p) => p.items) ?? []

  const claimMutation = useMutation({
    mutationFn: (referenceCode: string) => claimReviewItem(referenceCode),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['review-queue'] })
      notify({ kind: 'success', title: t('review.claimed') })
    },
    onError: () => notify({ kind: 'danger', title: t('review.claimFailed') }),
  })

  const unassignMutation = useMutation({
    mutationFn: (referenceCode: string) => unassignReviewItem(referenceCode),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['review-queue'] })
      notify({ kind: 'success', title: t('review.unassigned') })
    },
    onError: () => notify({ kind: 'danger', title: t('review.unassignFailed') }),
  })

  const assigneeLabel = (item: ReviewQueueItem) => {
    if (!item.assignedReviewerId) return t('review.unassignedLabel')
    return item.assignedReviewerId === currentUserId ? t('review.assignedToMe') : (item.assignedReviewerName ?? t('review.assignedToOther'))
  }

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
        {t('review.queue')}
      </h1>

      <div className="flex flex-wrap gap-4">
        <div className="flex flex-col gap-1">
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>{t('review.filterState')}</span>
          <Select
            value={stateFilter}
            onValueChange={setStateFilter}
            options={[
              { value: 'all', label: t('review.filterAll') },
              ...STATE_OPTIONS.map((s) => ({ value: s, label: s })),
            ]}
          />
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>{t('review.filterAssignee')}</span>
          <Select
            value={assigneeFilter}
            onValueChange={setAssigneeFilter}
            options={[
              { value: 'all', label: t('review.filterAll') },
              { value: 'me', label: t('review.assignedToMe') },
              { value: 'unassigned', label: t('review.unassignedLabel') },
            ]}
          />
        </div>
      </div>

      {items.length === 0 && !queueQuery.isLoading ? (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('review.noItems')}</p>
      ) : (
        <Table>
          <TableHead>
            <TableHeaderCell>{isArabic ? 'الاسم' : 'Name'}</TableHeaderCell>
            <TableHeaderCell>{isArabic ? 'رقم المرجع' : 'Reference'}</TableHeaderCell>
            <TableHeaderCell>{isArabic ? 'الحالة' : 'State'}</TableHeaderCell>
            <TableHeaderCell>{t('review.age')}</TableHeaderCell>
            <TableHeaderCell>{t('review.assignee')}</TableHeaderCell>
            <TableHeaderCell>{t('review.actions')}</TableHeaderCell>
          </TableHead>
          <TableBody>
            {items.map((item) => {
              const ageHours = (Date.now() - new Date(item.enteredQueueAt).getTime()) / (1000 * 60 * 60)
              const isMine = item.assignedReviewerId === currentUserId
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
                <TableCell>{assigneeLabel(item)}</TableCell>
                <TableCell>
                  {isMine ? (
                    <Button
                      variant="ghost"
                      size="sm"
                      isLoading={unassignMutation.isPending}
                      onClick={() => unassignMutation.mutate(item.referenceCode)}
                    >
                      {t('review.unassign')}
                    </Button>
                  ) : (
                    <Button
                      variant="ghost"
                      size="sm"
                      isLoading={claimMutation.isPending}
                      onClick={() => claimMutation.mutate(item.referenceCode)}
                    >
                      {t('review.claim')}
                    </Button>
                  )}
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
