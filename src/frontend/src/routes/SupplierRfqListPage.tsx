import { useTranslation } from 'react-i18next'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import {Button, Card, ListState, PageHeading, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow} from '../components/ui'
import { nextPageParam } from '../api/listEnvelope'
import { listInvitedRfqs } from '../api/supplierRfqs'

/** FEAT-08.6/FR-INV-006: only RFQs this supplier holds a real Invitation to are ever returned -
 * the backend list endpoint is itself invitation-scoped, not filtered client-side. */
export function SupplierRfqListPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')

  // API-ARCHITECTURE.md §6.1 names RFQs a cursor-default, infinite-scroll collection. A
  // page-one-only fetch would silently hide every invitation past the 20th - no error, no empty
  // state - which is the failure the backend's keyset paging exists to avoid. Same
  // useInfiniteQuery + "Load more" shape as ReviewQueuePage/TeamPage; SCREEN-SPECIFICATIONS.md
  // §5 (SCR-140) describes the table but is silent on the paging control, so this follows the
  // codebase's existing convention rather than inventing a pager the spec never named.
  const rfqsQuery = useInfiniteQuery({
    queryKey: ['supplier-rfqs'],
    queryFn: ({ pageParam }) => listInvitedRfqs(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: nextPageParam,
  })
  const rfqs = rfqsQuery.data?.pages.flatMap((page) => page.data) ?? []

  return (
    <div className="flex flex-col gap-6">
      <div>
        <PageHeading title={t('supplierRfq.title')} subtitle={t('supplierRfq.subtitle')} />
      </div>

      <Card title={t('supplierRfq.listTitle')}>
        {/* T2-32: loading and empty are distinct states. Before this, `rfqsQuery.data ?? []` meant
            an invited supplier was told "no RFQs" for the whole duration of the fetch - and
            permanently on a fetch failure - because a pending query and a genuinely empty list
            rendered the same copy. UX-PRINCIPLES.md §DoD: "All states designed: empty, loading
            (skeleton), error, success". */}
        <ListState
          isPending={rfqsQuery.isPending}
          isError={rfqsQuery.isError}
          error={rfqsQuery.error}
          onRetry={() => void rfqsQuery.refetch()}
          isEmpty={rfqs.length === 0}
          loadingLabel={t('common.loading')}
          errorText={t('common.loadFailed')}
          emptyText={t('supplierRfq.empty')}
          skeleton="list"
          skeletonRows={3}
        >
          <Table caption={t('supplierRfq.listTitle')}>
            <TableHead>
              <TableHeaderCell>{t('rfq.fields.reference')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.title')}</TableHeaderCell>
              <TableHeaderCell>{t('supplierRfq.myStatus')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {rfqs.map((rfq) => (
                <TableRow key={rfq.rfqCode}>
                  <TableCell>
                    <Link to="/rfqs/$referenceCode" params={{ referenceCode: rfq.rfqCode }} style={{ color: 'var(--color-text-brand)' }}>
                      {rfq.rfqCode}
                    </Link>
                  </TableCell>
                  <TableCell>{isArabic ? rfq.titleAr : rfq.titleEn}</TableCell>
                  <TableCell><StatusChip machine="invitation" value={rfq.invitationStatus} /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ListState>
        {rfqsQuery.hasNextPage ? (
          <Button
            variant="secondary"
            isLoading={rfqsQuery.isFetchingNextPage}
            onClick={() => rfqsQuery.fetchNextPage()}
          >
            {t('supplierRfq.loadMore')}
          </Button>
        ) : null}
      </Card>
    </div>
  )
}
