import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import {Badge, Button, Card, ListCard, ListState, PageHeading, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow} from '../../components/ui'
import { TenderTabs } from './rfq/TenderTabs'
import { formatDateTime, formatNumber } from '../../lib/datetime'
import { listReceivedProposals, getReceivedProposal } from '../../api/buyerProposals'
import type { BuyerProposalDetail, BuyerProposalListItem } from '../../api/buyerProposals'

/** The supplier's name in the reader's language, falling back to whichever half exists. */
function supplierNameIn(isArabic: boolean, ar: string, en: string): string {
  return (isArabic ? ar : en) || en || ar
}

/**
 * The list of bids. Extracted from the page because the page's own job - which tier the server has
 * released, and whether a bid is open beside the list - is a different question from how a row reads.
 */
function ProposalsTable({ proposals, isArabic, locale, onOpen }: Readonly<{
  proposals: readonly BuyerProposalListItem[]
  isArabic: boolean
  locale: string
  onOpen: (proposalId: string) => void
}>) {
  const { t } = useTranslation()
  return (
    <Table flush caption={t('receivedProposals.listTitle')}>
      <TableHead>
        <TableHeaderCell>{t('receivedProposals.fields.supplier')}</TableHeaderCell>
        <TableHeaderCell>{t('receivedProposals.fields.proposal')}</TableHeaderCell>
        <TableHeaderCell>{t('receivedProposals.fields.state')}</TableHeaderCell>
        <TableHeaderCell>{t('receivedProposals.fields.submittedAt')}</TableHeaderCell>
        <TableHeaderCell>{t('receivedProposals.fields.items')}</TableHeaderCell>
        <TableHeaderCell>{t('receivedProposals.fields.total')}</TableHeaderCell>
        <TableHeaderCell>{t('receivedProposals.fields.actions')}</TableHeaderCell>
      </TableHead>
      <TableBody>
        {proposals.map((p) => (
          <TableRow key={p.proposalId}>
            <TableCell>{supplierNameIn(isArabic, p.supplierNameAr, p.supplierNameEn)}</TableCell>
            <TableCell><code>{p.proposalCode}</code></TableCell>
            <TableCell><StatusChip machine="proposal" value={p.state} /></TableCell>
            <TableCell>{p.submittedAt ? formatDateTime(p.submittedAt, locale) : '—'}</TableCell>
            <TableCell>{p.itemCount}</TableCell>
            {/* Labelled, not blank: the reason the figure is absent is a rule, and a buyer who sees an
                empty cell assumes a broken screen or a free bid. */}
            <TableCell>
              {p.totalValue === null
                ? <Badge tone="neutral">{t('receivedProposals.afterConsolidation')}</Badge>
                : `${formatNumber(p.totalValue, locale)} ${p.currencyCode ?? ''}`}
            </TableCell>
            <TableCell>
              <Button size="sm" variant="ghost" onClick={() => onOpen(p.proposalId)}>
                {t('receivedProposals.open')}
              </Button>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}

/** One bid, read in full. Its own component because it has four optional sections of its own. */
function ProposalDetailBody({ detail, isArabic, locale }: Readonly<{
  detail: BuyerProposalDetail
  isArabic: boolean
  locale: string
}>) {
  const { t } = useTranslation()
  const narrative = isArabic ? detail.narrativeAr : detail.narrativeEn

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h3 className="font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {supplierNameIn(isArabic, detail.supplierNameAr, detail.supplierNameEn)}
        </h3>
        <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          <code>{detail.proposalCode}</code>
          {' · '}
          {t('receivedProposals.fields.documents')}: {detail.documentCount}
        </p>
      </div>

      {detail.narrativeEn || detail.narrativeAr ? (
        <div>
          <h4 className="mb-1 font-[var(--fw-medium)]">{t('receivedProposals.narrative')}</h4>
          <p style={{ color: 'var(--color-text-secondary)' }}>{narrative}</p>
        </div>
      ) : null}

      {detail.answers.length > 0 ? (
        <div>
          <h4 className="mb-1 font-[var(--fw-medium)]">{t('receivedProposals.answers')}</h4>
          <ul className="flex flex-col gap-2">
            {detail.answers.map((a) => (
              <li key={a.requirementId}>
                <p className="font-[var(--fw-medium)]">{isArabic ? a.textAr : a.textEn}</p>
                <p style={{ color: 'var(--color-text-secondary)' }}>{isArabic ? a.answerAr : a.answerEn}</p>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <Table caption={t('receivedProposals.lineItems')}>
        <TableHead>
          <TableHeaderCell>{t('receivedProposals.fields.item')}</TableHeaderCell>
          <TableHeaderCell>{t('receivedProposals.fields.quantity')}</TableHeaderCell>
          <TableHeaderCell>{t('receivedProposals.fields.unitPrice')}</TableHeaderCell>
          <TableHeaderCell>{t('receivedProposals.fields.lineTotal')}</TableHeaderCell>
        </TableHead>
        <TableBody>
          {detail.items.map((i) => (
            <TableRow key={i.rfqItemId}>
              <TableCell>{isArabic ? i.titleAr : i.titleEn}</TableCell>
              <TableCell>{formatNumber(i.quantity, locale)}</TableCell>
              <TableCell>{i.unitPrice === null ? '—' : formatNumber(i.unitPrice, locale)}</TableCell>
              <TableCell>{i.lineTotal === null ? '—' : formatNumber(i.lineTotal, locale)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      {detail.visibility === 'Commercial' ? (
        <p style={{ color: 'var(--color-text-primary)' }}>
          {t('receivedProposals.fields.total')}:{' '}
          <strong>{formatNumber(detail.totalValue ?? 0, locale)} {detail.currencyCode}</strong>
          {detail.paymentTerms ? ` · ${detail.paymentTerms}` : ''}
        </p>
      ) : (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('receivedProposals.commercialWithheldBody')}</p>
      )}
    </div>
  )
}

/**
 * SCR-430 (received proposals) and SCR-431 (proposal detail), `/back-office/rfqs/$code/proposals`,
 * `procurement_officer` and `procurement_manager`, both P0 (T-082).
 *
 * <p>Two inventory rows, one screen: the detail is a panel beside the list rather than its own route,
 * because a buyer comparing bids moves between them constantly and a full navigation per bid is the
 * interaction the comparison matrix already exists to avoid.</p>
 *
 * <p><b>The tier is the server's answer, and this screen only explains it.</b> Nothing here decides
 * what to hide. `visibility` arrives on the wire so a missing figure can be labelled — "commercial
 * values appear after consolidation" — instead of rendering as a blank cell that reads like a bug or,
 * worse, like a bid with no price.</p>
 */
export function ReceivedProposalsPage() {
  const { referenceCode } = useParams({ from: '/back-office/rfqs/$referenceCode/proposals' })
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'
  const [selected, setSelected] = useState<string | null>(null)

  const listQuery = useQuery({
    queryKey: ['received-proposals', referenceCode],
    queryFn: () => listReceivedProposals(referenceCode),
  })

  const detailQuery = useQuery({
    queryKey: ['received-proposal', referenceCode, selected],
    queryFn: () => getReceivedProposal(referenceCode, selected!),
    enabled: selected !== null,
  })

  const list = listQuery.data

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <PageHeading title={t('receivedProposals.title')} subtitle={t('receivedProposals.subtitle', { code: referenceCode })} />

      {/* The way back. These screens are tabs of one tender, and until this was here a buyer who opened
          the bids could only leave through the browser's own button - the strip that names the six views
          was rendered on three of them and not on the other four. */}
      <TenderTabs referenceCode={referenceCode} />
      </div>

      {/* `!list` is treated as still loading, not as empty. Not loaded is not the same as empty, and
          falling through to "no proposals were submitted" while the query is settling states something
          false about a live tender - which is exactly the failure a screen with a broken query wears. */}
      <ListState
        isPending={listQuery.isLoading || (!listQuery.isError && !list)}
        isError={listQuery.isError}
        error={listQuery.error}
        onRetry={() => void listQuery.refetch()}
        isEmpty={false}
        loadingLabel={t('common.loading')}
        errorText={t('receivedProposals.errors.loadFailed')}
        emptyText={t('receivedProposals.empty')}
      >
        {list?.visibility === 'Sealed' ? (
          // The sealed tier is a STATE, not an empty list. Saying "no proposals" here would be false —
          // the count below says otherwise — and saying nothing would read as a broken query.
          <Card title={t('receivedProposals.sealedTitle')}>
            <p style={{ color: 'var(--color-text-secondary)' }}>{t('receivedProposals.sealedBody')}</p>
            <p className="mt-2 font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
              {t('receivedProposals.sealedCount', { count: list.submittedCount })}
            </p>
          </Card>
        ) : (
          <div className="flex flex-col gap-6">
            {list?.visibility === 'Technical' ? (
              <Card title={t('receivedProposals.commercialWithheld')}>
                <p style={{ color: 'var(--color-text-secondary)' }}>{t('receivedProposals.commercialWithheldBody')}</p>
              </Card>
            ) : null}

            <Card flush title={t('receivedProposals.listTitle')}>
              {list && list.proposals.length > 0 ? (
                <ProposalsTable proposals={list.proposals} isArabic={isArabic} locale={locale} onOpen={setSelected} />
              ) : (
                <p style={{ color: 'var(--color-text-secondary)' }}>{t('receivedProposals.empty')}</p>
              )}
            </Card>

            {selected ? (
              <ListCard
                title={t('receivedProposals.detailTitle')}
                action={<Button size="sm" variant="ghost" onClick={() => setSelected(null)}>{t('receivedProposals.close')}</Button>}
                // A 200 carrying no body is a failed load, not an empty one - kept exactly as it was.
                query={{ ...detailQuery, isPending: detailQuery.isLoading, isError: detailQuery.isError || !detailQuery.data }}
                isEmpty={false}
                labels={{ loading: t('common.loading'), error: t('receivedProposals.errors.detailFailed'), empty: t('receivedProposals.empty') }}
                skeletonRows={3}
              >
                {detailQuery.data ? (
                  <ProposalDetailBody detail={detailQuery.data} isArabic={isArabic} locale={locale} />
                ) : null}
              </ListCard>
            ) : null}
          </div>
        )}
      </ListState>
    </div>
  )
}
