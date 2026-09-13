// FEAT-14.1 through 14.6 and FR-AWD-001 through 007: the award, from recommendation to issued PO.
//
// Every action here HIDES only, never gates - the server re-enforces its own guard on state, segregation of duties and
// supplier-active regardless of what this page shows, the same rule as every other page in this codebase. Issuing is the
// case that shows why it matters: it requires award.approve, the same permission as approving it and one the officer who
// recommended does not hold, and the button used to render for anyone who could open the screen - so an officer was offered
// an Issue that answered 403 every time.
//
// A failed fetch is its own branch. An award screen that renders "no recommendation yet" because the fetch failed is the
// worst version of this defect in the product: it invites the officer to start the process again.
//
// THE ERP SYNC LABELS are UX-WRITING.md §7.6's three, keyed by enum member. NotRequested is absent DELIBERATELY, and absence
// is how it renders: §7.6 has no row for it because it is not a sync state - nothing has been asked of the ERP yet - so
// there is nothing to transcribe, and the two wrong answers are both available by accident, namely printing the raw enum
// name, which is what this page did, showing a procurement officer the string "NotRequested", or reusing the pending label,
// which claims a request is in flight when none was made. The helper returns null and the caller renders no chip. It is
// typed as a Record over the enum MINUS that member, so adding a fifth ErpSyncStatus fails the type-check here rather than
// falling through to a missing translation key at runtime.
//
// THE HEADING is the tender, then which of its six views you are on. It used to name the VIEW - "Award" joined to a
// reference code - which is what the strip immediately below already says, while the tender's own name appeared nowhere on
// the screen.
//
// THE WINNER is chosen by the proposal's §3 CODE, in the label as well as the value. "Rank 1 - 86.00" identifies a row on
// this screen and nothing outside it, and a manager recommending a winner - or anyone later reading the award file - needs
// the bid it refers to; the code is not the bidder's name, so this discloses nothing the seal withholds. T-068 made the code
// the value too: it used to be the bid's internal identifier, which this screen then posted back as the winner, and the
// label fell back to a rank alone whenever the code was absent, which it was on every response that did not look codes up.

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../../lib/authStore'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import {Badge, Button, Card, Input, QueryError, Select, SkeletonList, StatusChip, toneFor, useToast} from '../../components/ui'
import { invalidateQuietly } from '../../lib/queryClient'
import type { ErpSyncStatus } from '../../api/awards'
import { getAward, recommendAward, routeAwardForApproval, approveAward, rejectAward, executeAward, retryAwardErpSync, AwardApiError } from '../../api/awards'
import { getEvaluation } from '../../api/evaluations'
import { TenderTabs } from './rfq/TenderTabs'
import { TenderHeader } from './rfq/TenderHeader'
import { apiErrorMessage } from '../../api/problem'

const ERP_SYNC_LABEL_KEYS: Record<Exclude<ErpSyncStatus, 'NotRequested'>, string> = {
  Requested: 'status.erpSync.Requested',
  Synced: 'status.erpSync.Synced',
  Failed: 'status.erpSync.Failed',
}

function erpSyncLabelKey(status: ErpSyncStatus): string | null {
  return status === 'NotRequested' ? null : ERP_SYNC_LABEL_KEYS[status]
}

const ERP_TONES = { Synced: 'success', Failed: 'danger' } as const

export function AwardPage() {
  const canApproveAward = useAuthStore((state) => state.claims?.permissions.includes('award.approve') ?? false)
  const { referenceCode } = useParams({ from: '/back-office/rfqs/$referenceCode/award' })
  const { t } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()

  const [winningProposalCode, setWinningProposalCode] = useState('')
  const [justificationAr, setJustificationAr] = useState('')
  const [justificationEn, setJustificationEn] = useState('')
  const [rejectReason, setRejectReason] = useState('')

  const awardQuery = useQuery({ queryKey: ['award', referenceCode], queryFn: () => getAward(referenceCode) })
  const evaluationQuery = useQuery({ queryKey: ['evaluation', referenceCode], queryFn: () => getEvaluation(referenceCode) })
  const award = awardQuery.data ?? null
  const evaluation = evaluationQuery.data ?? null
  const qualifiedResults = (evaluation?.results ?? []).filter((r) => r.technicallyQualified).sort((a, b) => (a.rank ?? 999) - (b.rank ?? 999))

  const invalidate = () => invalidateQuietly(queryClient, { queryKey: ['award', referenceCode] })
  const errorMessage = (err: unknown, fallback: string) =>
    apiErrorMessage(err, fallback, t('common.concurrencyConflict'))

  const recommendMutation = useMutation({
    mutationFn: () => recommendAward(referenceCode, { winningProposalCode, justificationAr, justificationEn }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('award.recommended') }); setJustificationAr(''); setJustificationEn(''); setWinningProposalCode('') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('award.errors.actionFailed')) }),
  })

  const routeMutation = useMutation({
    mutationFn: () => routeAwardForApproval(referenceCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('award.routed') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('award.errors.actionFailed')) }),
  })

  const approveMutation = useMutation({
    mutationFn: () => approveAward(referenceCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('award.approved') }) },
    onError: (err) => notify({
      kind: 'danger',
      title: err instanceof AwardApiError && err.message.includes('differ from the recommender')
        ? t('award.errors.segregationOfDuties')
        : errorMessage(err, t('award.errors.actionFailed')),
    }),
  })

  const rejectMutation = useMutation({
    mutationFn: () => rejectAward(referenceCode, rejectReason),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('award.rejected') }); setRejectReason('') },
    onError: (err) => notify({
      kind: 'danger',
      title: err instanceof AwardApiError && err.message.includes('differ from the recommender')
        ? t('award.errors.segregationOfDuties')
        : errorMessage(err, t('award.errors.actionFailed')),
    }),
  })

  const executeMutation = useMutation({
    mutationFn: () => executeAward(referenceCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('award.issued') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('award.errors.actionFailed')) }),
  })

  const retryMutation = useMutation({
    mutationFn: () => retryAwardErpSync(referenceCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('award.retryQueued') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('award.errors.actionFailed')) }),
  })

  if (awardQuery.isError || evaluationQuery.isError) {
    return <QueryError error={awardQuery.error} onRetry={() => { void awardQuery.refetch(); void evaluationQuery.refetch() }} />
  }

  if (awardQuery.isLoading || evaluationQuery.isLoading) {
    return <SkeletonList label={t('common.loading')} />
  }

  const showRecommendForm = !award || award.state === 'Rejected'
  const lastApproval = award?.approvals[award.approvals.length - 1]

  return (
    <div className="flex flex-col gap-6">
      <TenderHeader referenceCode={referenceCode} />
      <TenderTabs referenceCode={referenceCode} />

      {award ? (
        <Card title={t('award.status')}>
          <div className="flex flex-col gap-3">
            <StatusChip machine="award" value={award.state} />
            <p>{t('award.justification')}: {justificationForDisplay(award)}</p>
            <p style={{ color: 'var(--color-text-secondary)' }}>{t('award.revision', { count: award.recommendationRevision })}</p>

            {award.approvals.length > 0 ? (
              <div>
                <p className="mb-1 font-[var(--fw-medium)]">{t('award.approvals')}</p>
                <ul className="flex flex-col gap-1">
                  {award.approvals.map((a, i) => (
                    <li key={i} style={{ color: 'var(--color-text-secondary)' }}>
                      {t('award.stepLabel', { step: a.stepNo })}: {a.decision ?? t('award.pending')}
                      {a.comment ? ` — ${a.comment}` : ''}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}

            {award.state === 'Recommended' ? (
              <Button isLoading={routeMutation.isPending} onClick={() => routeMutation.mutate()}>{t('award.routeForApproval')}</Button>
            ) : null}

            {award.state === 'PendingApproval' ? (
              <div className="flex flex-col gap-2">
                <Button isLoading={approveMutation.isPending} onClick={() => approveMutation.mutate()}>{t('award.approve')}</Button>
                <div className="flex flex-wrap items-end gap-2">
                  <Input aria-label={t('award.rejectReason')} placeholder={t('award.rejectReason')} value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} />
                  <Button variant="ghost" isLoading={rejectMutation.isPending} disabled={!rejectReason} onClick={() => rejectMutation.mutate()}>
                    {t('award.reject')}
                  </Button>
                </div>
              </div>
            ) : null}

            {award.state === 'Rejected' && lastApproval?.comment ? (
              <p style={{ color: 'var(--color-danger-solid)' }}>{t('award.rejectionReason')}: {lastApproval.comment}</p>
            ) : null}

            {award.state === 'Approved' && canApproveAward ? (
              <Button isLoading={executeMutation.isPending} onClick={() => executeMutation.mutate()}>{t('award.execute')}</Button>
            ) : null}

            {award.state === 'Awarded' ? (
              <div className="flex flex-col gap-2">
                {erpSyncLabelKey(award.erpSyncStatus) ? (
                  <Badge tone={toneFor(award.erpSyncStatus, ERP_TONES)}>
                    {t('award.erpStatus')}: {t(erpSyncLabelKey(award.erpSyncStatus)!)}
                  </Badge>
                ) : null}
                {award.externalPurchaseOrderRef ? <p>{t('award.externalPoRef')}: {award.externalPurchaseOrderRef}</p> : null}
                {award.erpSyncStatus === 'Failed' ? (
                  <Button size="sm" variant="ghost" isLoading={retryMutation.isPending} onClick={() => retryMutation.mutate()}>
                    {t('award.retrySync')}
                  </Button>
                ) : null}
              </div>
            ) : null}
          </div>
        </Card>
      ) : (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('award.notRecommendedYet')}</p>
      )}

      {showRecommendForm ? (
        <Card title={award ? t('award.reRecommend') : t('award.recommend')}>
          {qualifiedResults.length === 0 ? (
            <p style={{ color: 'var(--color-text-secondary)' }}>{t('award.noQualifiedProposals')}</p>
          ) : (
            <div className="flex flex-col gap-2">
              <Select value={winningProposalCode} onValueChange={setWinningProposalCode} placeholder={t('award.selectWinner')}
                options={qualifiedResults.map((r) => ({
                  value: r.proposalCode,
                  label: `${r.proposalCode} · ${t('award.winnerOption', { rank: r.rank, total: r.weightedTotal.toFixed(2) })}`,
                }))} />
              <Input aria-label={t('award.justificationEn')} placeholder={t('award.justificationEn')} value={justificationEn} onChange={(e) => setJustificationEn(e.target.value)} />
              <Input aria-label={t('award.justificationAr')} placeholder={t('award.justificationAr')} value={justificationAr} onChange={(e) => setJustificationAr(e.target.value)} />
              <Button size="sm" className="self-start" isLoading={recommendMutation.isPending}
                disabled={!winningProposalCode || !justificationAr || !justificationEn}
                onClick={() => recommendMutation.mutate()}>
                {award ? t('award.reRecommend') : t('award.recommend')}
              </Button>
            </div>
          )}
        </Card>
      ) : null}
    </div>
  )
}

function justificationForDisplay(award: { justificationAr: string; justificationEn: string }): string {
  return award.justificationEn
}
