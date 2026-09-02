import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import { Badge, Button, Card, Input, Select, SkeletonList, StatusChip, useToast } from '../../components/ui'
import { invalidateQuietly } from '../../lib/queryClient'
import { getAward, recommendAward, routeAwardForApproval, approveAward, rejectAward, executeAward, retryAwardErpSync, AwardApiError } from '../../api/awards'
import { getEvaluation } from '../../api/evaluations'

/** FEAT-14.1..14.6/FR-AWD-001..007. Every action here hides only, never gates - the server
 * re-enforces its own guard (state, segregation of duties, supplier-active) regardless of what
 * this page shows, same rule as every other page in this codebase. */
export function AwardPage() {
  const { referenceCode } = useParams({ from: '/back-office/rfqs/$referenceCode/award' })
  const { t } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()

  const [winningProposalId, setWinningProposalId] = useState('')
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
    err instanceof AwardApiError && err.isConcurrencyConflict ? t('common.concurrencyConflict') : err instanceof AwardApiError ? err.message : fallback

  const recommendMutation = useMutation({
    mutationFn: () => recommendAward(referenceCode, { winningProposalId, justificationAr, justificationEn }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('award.recommended') }); setJustificationAr(''); setJustificationEn(''); setWinningProposalId('') },
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

  if (awardQuery.isLoading || evaluationQuery.isLoading) {
    return <SkeletonList label={t('common.loading')} />
  }

  const showRecommendForm = !award || award.state === 'Rejected'
  const lastApproval = award?.approvals[award.approvals.length - 1]

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
        {t('award.title')} — {referenceCode}
      </h1>

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

            {award.state === 'Approved' ? (
              <Button isLoading={executeMutation.isPending} onClick={() => executeMutation.mutate()}>{t('award.execute')}</Button>
            ) : null}

            {award.state === 'Awarded' ? (
              <div className="flex flex-col gap-2">
                <Badge tone={award.erpSyncStatus === 'Synced' ? 'success' : award.erpSyncStatus === 'Failed' ? 'danger' : 'info'}>
                  {t('award.erpStatus')}: {award.erpSyncStatus}
                </Badge>
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
              <Select value={winningProposalId} onValueChange={setWinningProposalId} placeholder={t('award.selectWinner')}
                options={qualifiedResults.map((r) => ({ value: r.proposalId, label: t('award.winnerOption', { rank: r.rank, total: r.weightedTotal.toFixed(2) }) }))} />
              <Input aria-label={t('award.justificationEn')} placeholder={t('award.justificationEn')} value={justificationEn} onChange={(e) => setJustificationEn(e.target.value)} />
              <Input aria-label={t('award.justificationAr')} placeholder={t('award.justificationAr')} value={justificationAr} onChange={(e) => setJustificationAr(e.target.value)} />
              <Button size="sm" className="self-start" isLoading={recommendMutation.isPending}
                disabled={!winningProposalId || !justificationAr || !justificationEn}
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
