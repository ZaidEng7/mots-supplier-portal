import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import {
  Badge, Button, Card, SkeletonTable, StatusChip,
  Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow,
} from '../../components/ui'
import { formatDateTime, formatNumber } from '../../lib/datetime'
import { listReceivedProposals, getReceivedProposal } from '../../api/buyerProposals'

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
  const supplierName = (ar: string, en: string) => (isArabic ? ar : en) || en || ar

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('receivedProposals.title')}
        </h1>
        <p style={{ color: 'var(--color-text-secondary)' }}>
          {t('receivedProposals.subtitle', { code: referenceCode })}
        </p>
      </div>

      {listQuery.isLoading ? (
        <SkeletonTable label={t('common.loading')} />
      ) : listQuery.isError ? (
        <Card title={t('receivedProposals.title')}>
          <p>{t('receivedProposals.errors.loadFailed')}</p>
          <Button size="sm" variant="ghost" onClick={() => void listQuery.refetch()}>{t('receivedProposals.retry')}</Button>
        </Card>
      ) : !list ? (
        // Not loaded is not the same as empty. Falling through to "no proposals were submitted"
        // while the query is still settling states something false about a live tender - and it is
        // exactly the failure mode a screen with a broken query wears.
        <SkeletonTable label={t('common.loading')} />
      ) : list.visibility === 'Sealed' ? (
        // The sealed tier is a STATE, not an empty list. Saying "no proposals" here would be false —
        // the count below says otherwise — and saying nothing would read as a broken query.
        <Card title={t('receivedProposals.sealedTitle')}>
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('receivedProposals.sealedBody')}</p>
          <p className="mt-2 font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('receivedProposals.sealedCount', { count: list.submittedCount })}
          </p>
        </Card>
      ) : (
        <>
          {list.visibility === 'Technical' ? (
            <Card title={t('receivedProposals.commercialWithheld')}>
              <p style={{ color: 'var(--color-text-secondary)' }}>{t('receivedProposals.commercialWithheldBody')}</p>
            </Card>
          ) : null}

          <Card title={t('receivedProposals.listTitle')}>
            {list.proposals.length === 0 ? (
              <p style={{ color: 'var(--color-text-secondary)' }}>{t('receivedProposals.empty')}</p>
            ) : (
              <Table caption={t('receivedProposals.listTitle')}>
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
                  {list.proposals.map((p) => (
                    <TableRow key={p.proposalId}>
                      <TableCell>{supplierName(p.supplierNameAr, p.supplierNameEn)}</TableCell>
                      <TableCell><code>{p.proposalCode}</code></TableCell>
                      <TableCell><StatusChip machine="proposal" value={p.state} /></TableCell>
                      <TableCell>{p.submittedAt ? formatDateTime(p.submittedAt, locale) : '—'}</TableCell>
                      <TableCell>{p.itemCount}</TableCell>
                      {/* Labelled, not blank: the reason the figure is absent is a rule, and a
                          buyer who sees an empty cell assumes a broken screen or a free bid. */}
                      <TableCell>
                        {p.totalValue === null
                          ? <Badge tone="neutral">{t('receivedProposals.afterConsolidation')}</Badge>
                          : `${formatNumber(p.totalValue, locale)} ${p.currencyCode ?? ''}`}
                      </TableCell>
                      <TableCell>
                        <Button size="sm" variant="ghost" onClick={() => setSelected(p.proposalId)}>
                          {t('receivedProposals.open')}
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </Card>

          {selected ? (
            <Card
              title={t('receivedProposals.detailTitle')}
              action={<Button size="sm" variant="ghost" onClick={() => setSelected(null)}>{t('receivedProposals.close')}</Button>}
            >
              {detailQuery.isLoading ? (
                <SkeletonTable label={t('common.loading')} rows={3} />
              ) : detailQuery.isError || !detailQuery.data ? (
                <p>{t('receivedProposals.errors.detailFailed')}</p>
              ) : (
                <div className="flex flex-col gap-4">
                  <div>
                    <h3 className="font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
                      {supplierName(detailQuery.data.supplierNameAr, detailQuery.data.supplierNameEn)}
                    </h3>
                    <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                      <code>{detailQuery.data.proposalCode}</code>
                      {' · '}
                      {t('receivedProposals.fields.documents')}: {detailQuery.data.documentCount}
                    </p>
                  </div>

                  {detailQuery.data.narrativeEn || detailQuery.data.narrativeAr ? (
                    <div>
                      <h4 className="mb-1 font-[var(--fw-medium)]">{t('receivedProposals.narrative')}</h4>
                      <p style={{ color: 'var(--color-text-secondary)' }}>
                        {isArabic ? detailQuery.data.narrativeAr : detailQuery.data.narrativeEn}
                      </p>
                    </div>
                  ) : null}

                  {detailQuery.data.answers.length > 0 ? (
                    <div>
                      <h4 className="mb-1 font-[var(--fw-medium)]">{t('receivedProposals.answers')}</h4>
                      <ul className="flex flex-col gap-2">
                        {detailQuery.data.answers.map((a) => (
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
                      {detailQuery.data.items.map((i) => (
                        <TableRow key={i.rfqItemId}>
                          <TableCell>{isArabic ? i.titleAr : i.titleEn}</TableCell>
                          <TableCell>{formatNumber(i.quantity, locale)}</TableCell>
                          <TableCell>{i.unitPrice === null ? '—' : formatNumber(i.unitPrice, locale)}</TableCell>
                          <TableCell>{i.lineTotal === null ? '—' : formatNumber(i.lineTotal, locale)}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>

                  {detailQuery.data.visibility === 'Commercial' ? (
                    <p style={{ color: 'var(--color-text-primary)' }}>
                      {t('receivedProposals.fields.total')}:{' '}
                      <strong>{formatNumber(detailQuery.data.totalValue ?? 0, locale)} {detailQuery.data.currencyCode}</strong>
                      {detailQuery.data.paymentTerms ? ` · ${detailQuery.data.paymentTerms}` : ''}
                    </p>
                  ) : (
                    <p style={{ color: 'var(--color-text-secondary)' }}>{t('receivedProposals.commercialWithheldBody')}</p>
                  )}
                </div>
              )}
            </Card>
          ) : null}
        </>
      )}
    </div>
  )
}
