import { useTranslation } from 'react-i18next'
import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import {
  Badge, Button, Card, SkeletonTable, StatusChip,
  Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow,
} from '../components/ui'
import { formatDateTime, formatNumber } from '../lib/datetime'
import { listMyProposals } from '../api/proposals'

/**
 * SCR-150, `/proposals`, `supplier_admin` and `supplier_user`, **P0**.
 *
 * <p><b>The gap.</b> A supplier could reach a proposal only through the RFQ containing it, so
 * "what have I bid on" had no answer short of opening every invitation in turn.</p>
 *
 * <p>Each row links back into the proposal workspace on its own RFQ — SCR-154's read, SCR-155's
 * revise, SCR-156's withdraw and SCR-157's award response all already live there, and duplicating
 * any of them here would be a second way to act on one aggregate. This is the missing index, not a
 * second workspace.</p>
 */
export function MyProposalsPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  const query = useQuery({ queryKey: ['my-proposals'], queryFn: listMyProposals })

  if (query.isLoading) return <SkeletonTable label={t('common.loading')} />
  if (query.isError || !query.data) {
    return (
      <Card title={t('myProposals.title')}>
        <p>{t('myProposals.errors.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => void query.refetch()}>{t('myProposals.retry')}</Button>
      </Card>
    )
  }

  const proposals = query.data

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('myProposals.title')}
        </h1>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('myProposals.subtitle')}</p>
      </div>

      <Card title={t('myProposals.listTitle')}>
        {proposals.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('myProposals.empty')}</p>
        ) : (
          <Table caption={t('myProposals.listTitle')}>
            <TableHead>
              <TableHeaderCell>{t('myProposals.fields.rfq')}</TableHeaderCell>
              <TableHeaderCell>{t('myProposals.fields.proposal')}</TableHeaderCell>
              <TableHeaderCell>{t('myProposals.fields.state')}</TableHeaderCell>
              <TableHeaderCell>{t('myProposals.fields.deadline')}</TableHeaderCell>
              <TableHeaderCell>{t('myProposals.fields.total')}</TableHeaderCell>
              <TableHeaderCell>{t('myProposals.fields.actions')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {proposals.map((p) => (
                <TableRow key={p.proposalCode}>
                  <TableCell>
                    <div className="flex flex-col">
                      <span>{isArabic ? p.rfqTitleAr : p.rfqTitleEn}</span>
                      <code className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>{p.rfqCode}</code>
                    </div>
                  </TableCell>
                  <TableCell><code>{p.proposalCode}</code></TableCell>
                  <TableCell><StatusChip machine="proposal" value={p.state} /></TableCell>
                  <TableCell>{p.submissionDeadline ? formatDateTime(p.submissionDeadline, locale) : '—'}</TableCell>
                  {/* A supplier sees their OWN price at every state - the two-envelope seal is about
                      what the BUYER may see, not about hiding a supplier's bid from themselves. */}
                  <TableCell>
                    {p.totalValue === null ? '—' : `${formatNumber(p.totalValue, locale)} ${p.currencyCode ?? ''}`}
                  </TableCell>
                  <TableCell>
                    {/* Back into the workspace that already owns every action on this proposal. */}
                    <Link to="/rfqs/$referenceCode/proposal" params={{ referenceCode: p.rfqCode }}>
                      <Button size="sm" variant="ghost">
                        {p.state === 'Draft' ? t('myProposals.continue') : t('myProposals.open')}
                      </Button>
                    </Link>
                    {p.state === 'AwardOffered' ? (
                      <Badge tone="success">{t('myProposals.awardOffered')}</Badge>
                    ) : null}
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
