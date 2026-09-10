import { useState } from 'react'
import { useParams } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateQuietly } from '../../lib/queryClient'
import { Badge, Button, Card, Input, QueryError, SkeletonList, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../../components/ui'
import { apiErrorMessage } from '../../api/problem'
import { formatDate } from '../../lib/datetime'
import {
  getRfq,
  inviteSupplier,
  answerClarification,
  publishClarification,
  suggestInvitationCandidates,
} from '../../api/rfqs'
import { TenderTabs } from './rfq/TenderTabs'
import { TenderHeader } from './rfq/TenderHeader'

/**
 * Who was asked to bid, and what they asked back.
 *
 * <p><b>Why this is its own screen.</b> Invitations and clarifications answer one question - how is
 * this tender going with the people it was sent to - and the workspace filed them halfway down a column
 * that started with line items and ended with a cancel button. The comp gives them a tab, with the
 * invited count on it, so the question is answered before the tab is opened.</p>
 *
 * <p>Nothing here changed except where it lives.</p>
 */
export function TenderSuppliersPage() {
  const { referenceCode } = useParams({ strict: false }) as { referenceCode: string }
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const queryClient = useQueryClient()
  const { notify } = useToast()

  const [answerDrafts, setAnswerDrafts] = useState<Record<string, { text: string }>>({})
  const draftFor = (id: string) => answerDrafts[id] ?? { text: '' }

  const rfqQuery = useQuery({ queryKey: ['rfq', referenceCode], queryFn: () => getRfq(referenceCode) })
  const candidatesQuery = useQuery({
    queryKey: ['rfq-candidates', referenceCode],
    queryFn: () => suggestInvitationCandidates(referenceCode),
  })
  const rfq = rfqQuery.data

  const errorMessage = (err: unknown, fallback: string) =>
    apiErrorMessage(err, fallback, t('common.concurrencyConflict'))
  const invalidate = () => {
    invalidateQuietly(queryClient, { queryKey: ['rfq', referenceCode] })
    invalidateQuietly(queryClient, { queryKey: ['workspace', referenceCode] })
  }

  const inviteMutation = useMutation({
    mutationFn: (supplierId: string) => inviteSupplier(referenceCode, supplierId),
    onSuccess: () => {
      invalidate()
      invalidateQuietly(queryClient, { queryKey: ['rfq-candidates', referenceCode] })
      notify({ kind: 'success', title: t('rfq.invitations.invited') })
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.invitations.errors.inviteFailed')) }),
  })

  const answerMutation = useMutation({
    mutationFn: ({ clarificationId, answer }: { clarificationId: string; answer: string }) =>
      answerClarification(referenceCode, clarificationId, answer),
    onSuccess: (_, { clarificationId }) => {
      invalidate()
      notify({ kind: 'success', title: t('rfq.clarifications.answered') })
      setAnswerDrafts((prev) => { const next = { ...prev }; delete next[clarificationId]; return next })
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.clarifications.errors.answerFailed')) }),
  })

  const publishClarificationMutation = useMutation({
    mutationFn: (clarificationId: string) => publishClarification(referenceCode, clarificationId),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.clarifications.published') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.clarifications.errors.answerFailed')) }),
  })

  if (rfqQuery.isError) return <QueryError error={rfqQuery.error} onRetry={() => void rfqQuery.refetch()} />
  if (rfqQuery.isLoading || !rfq) return <SkeletonList label={t('common.loading')} />

  const canInvite = !['SubmissionClosed', 'UnderEvaluation', 'Clarification', 'Shortlisting', 'Recommendation', 'AwardApproval', 'Awarded', 'Completed', 'Cancelled'].includes(rfq.state)
  const invitedSupplierIds = new Set(rfq.invitations.map((i) => i.supplierId))
  const uninvitedCandidates = (candidatesQuery.data ?? []).filter((c) => !invitedSupplierIds.has(c.supplierId))

  return (
    <div className="flex flex-col gap-6">
      {/* The tender's identity, said the same way on all six of its views. This screen already named
          the tender and its code; what it did not carry was who owns it or when bidding closes, which
          the tender's own view has always shown. One record, one head. */}
      <TenderHeader referenceCode={referenceCode} />
      <TenderTabs referenceCode={referenceCode} />

      <div className="flex flex-col gap-4">
          <Card title={t('rfq.invitations.title')}>
            {rfq.invitations.length > 0 ? (
              <Table caption={t('rfq.invitations.title')}>
                <TableHead>
                  <TableHeaderCell>{t('rfq.invitations.fields.supplier')}</TableHeaderCell>
                  <TableHeaderCell>{t('rfq.invitations.fields.status')}</TableHeaderCell>
                  <TableHeaderCell>{t('rfq.invitations.fields.invitedAt')}</TableHeaderCell>
                  <TableHeaderCell>{t('rfq.invitations.fields.viewedAt')}</TableHeaderCell>
                  <TableHeaderCell>{t('rfq.invitations.fields.declineReason')}</TableHeaderCell>
                </TableHead>
                <TableBody>
                  {rfq.invitations.map((inv) => (
                    <TableRow key={inv.id}>
                      <TableCell>{isArabic ? inv.supplierDisplayNameAr : inv.supplierDisplayNameEn}</TableCell>
                      <TableCell>
                        <StatusChip machine="invitation" value={inv.status} />
                      </TableCell>
                      <TableCell>{formatDate(inv.invitedAt, i18n.language)}</TableCell>
                      <TableCell>{inv.viewedAt ? formatDate(inv.viewedAt, i18n.language) : '—'}</TableCell>
                      <TableCell>{inv.declineReason ?? '—'}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            ) : (
              <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.invitations.none')}</p>
            )}
            {canInvite && uninvitedCandidates.length > 0 ? (
              <div className="mt-4">
                <p className="mb-2 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
                  {t('rfq.invitations.candidatesTitle')}
                </p>
                <ul className="flex flex-col gap-2">
                  {uninvitedCandidates.map((c) => (
                    <li key={c.supplierId} className="flex items-center justify-between gap-2">
                      <span>{isArabic ? c.displayNameAr : c.displayNameEn} ({t('rfq.invitations.matchCount', { count: c.matchCount })})</span>
                      <Button size="sm" isLoading={inviteMutation.isPending} onClick={() => inviteMutation.mutate(c.supplierId)}>
                        {t('rfq.invitations.invite')}
                      </Button>
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
          </Card>

          <Card title={t('rfq.clarifications.title')}>
            {rfq.clarifications.length > 0 ? (
              <ul className="flex flex-col gap-4">
                {rfq.clarifications.map((c) => {
                  const draft = draftFor(c.id)
                  return (
                    <li key={c.id} className="border-b pb-4 last:border-b-0" style={{ borderColor: 'var(--color-border)' }}>
                      <div className="flex items-center justify-between gap-2">
                        <p className="font-[var(--fw-medium)]">{isArabic ? c.askedBySupplierNameAr : c.askedBySupplierNameEn}: {c.question}</p>
                        <Badge tone={c.visibility === 'PublishedToAll' ? 'success' : 'info'}>
                          {c.visibility === 'PublishedToAll' ? t('rfq.clarifications.published') : t('rfq.clarifications.private')}
                        </Badge>
                      </div>
                      {c.answer ? (
                        <p className="mt-1" style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.clarifications.answerLabel')}: {c.answer}</p>
                      ) : (
                        <div className="mt-2 flex flex-wrap items-end gap-2">
                          <Input aria-label={t('rfq.clarifications.answerLabel')} placeholder={t('rfq.clarifications.answerLabel')}
                            value={draft.text} onChange={(e) => setAnswerDrafts((prev) => ({ ...prev, [c.id]: { ...draft, text: e.target.value } }))} />
                          <p className="w-full text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                            {t('rfq.clarifications.broadcastNotice')}
                          </p>
                          {/* A-4: no publish checkbox. Answering broadcasts to every invitee with the
                              asker anonymised, so the officer is told that rather than asked it - an
                              option whose only fair setting is "yes" is not a choice. */}
                          <Button size="sm" isLoading={answerMutation.isPending} disabled={!draft.text}
                            onClick={() => answerMutation.mutate({ clarificationId: c.id, answer: draft.text })}>
                            {t('rfq.clarifications.answer')}
                          </Button>
                        </div>
                      )}
                      {c.answer && c.visibility === 'PrivateToAsker' ? (
                        <Button size="sm" variant="secondary" className="mt-2" isLoading={publishClarificationMutation.isPending}
                          onClick={() => publishClarificationMutation.mutate(c.id)}>
                          {t('rfq.clarifications.publish')}
                        </Button>
                      ) : null}
                    </li>
                  )
                })}
              </ul>
            ) : (
              <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.clarifications.none')}</p>
            )}
          </Card>
      </div>
    </div>
  )
}
