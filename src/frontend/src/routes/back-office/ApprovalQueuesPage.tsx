import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { getApprovalQueues, type ApprovalQueueItem } from '../../api/dashboards'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { StatusChip } from '../../components/ui/StatusChip'
import { SkeletonList } from '../../components/ui/Skeleton'
import { formatDateTime } from '../../lib/datetime'

/**
 * SCR-401 — manager approvals. `/procurement/approvals`, P0.
 * SCREEN-INVENTORY: "Queues: RFQ publish approvals + award approvals".
 *
 * <p><b>These are role-and-organization queues, not personal ones</b>, and the copy says so. Nothing
 * resolves a single named approver from the `award.approve` claim - the gap EPIC-15 reported - so
 * "assigned to you" would be a claim the system cannot make.</p>
 *
 * <p>Every row's link comes from the server alongside the row itself. PR #90's defect was a queue
 * offering work its persona could not open, and having one source for both is what lets a test
 * follow exactly what the user would click.</p>
 */
export function ApprovalQueuesPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  const query = useQuery({ queryKey: ['approval-queues'], queryFn: getApprovalQueues })

  const queue = (title: string, items: ApprovalQueueItem[], machine: 'rfq' | 'award', emptyText: string) => (
    <Card title={title}>
      {query.isPending ? <SkeletonList label={title} rows={3} /> : null}
      {items.length === 0 && !query.isPending ? (
        <p style={{ color: 'var(--color-text-secondary)' }}>{emptyText}</p>
      ) : null}
      <ul className="flex flex-col gap-2">
        {items.map((item) => (
          <li key={`${item.rfqReferenceCode}-${machine}`}
            className="flex flex-wrap items-center justify-between gap-2 rounded-[var(--radius-md)] p-3"
            style={{ border: '1px solid var(--color-border)' }}>
            <div className="flex flex-col">
              <Link to="/back-office/rfqs/$referenceCode" params={{ referenceCode: item.rfqReferenceCode }}>
                {isArabic ? item.titleAr : item.titleEn}
              </Link>
              {item.waitingSince ? (
                <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                  {formatDateTime(item.waitingSince, locale)}
                </span>
              ) : null}
            </div>
            <StatusChip machine={machine} value={item.state} />
          </li>
        ))}
      </ul>
    </Card>
  )

  if (query.isError) {
    return (
      <Card title={t('approvals.title')}>
        <p>{t('approvals.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => query.refetch()}>{t('approvals.retry')}</Button>
      </Card>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h1 className="text-[length:var(--text-heading-lg)]">{t('approvals.title')}</h1>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('approvals.subtitle')}</p>
      </div>

      {queue(t('approvals.rfqQueue'), query.data?.rfqPublishApprovals ?? [], 'rfq', t('approvals.noRfqs'))}
      {queue(t('approvals.awardQueue'), query.data?.awardApprovals ?? [], 'award', t('approvals.noAwards'))}
    </div>
  )
}
