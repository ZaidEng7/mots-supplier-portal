import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import { Badge, Button, Card, Input, useToast } from '../../components/ui'
import { invalidateQuietly } from '../../lib/queryClient'
import { getMyEvaluation, scoreCriterion, submitMyEvaluation, EvaluationApiError } from '../../api/evaluations'

/** FEAT-11.3/11.5/FR-EVL-003..006: the evaluator's own scoring workspace - blind to every other
 * evaluator's scores (see getMyEvaluation's own doc comment), and the financial criteria for a
 * proposal are greyed out (never gated by hiding alone - the server refuses the write regardless,
 * see ScoreCriterionHandler) until that proposal passes technical qualification for THIS
 * evaluator. */
export function MyEvaluationPage() {
  const { referenceCode } = useParams({ from: '/back-office/rfqs/$referenceCode/my-evaluation' })
  const { t } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const [drafts, setDrafts] = useState<Record<string, string>>({})

  const evaluationQuery = useQuery({ queryKey: ['my-evaluation', referenceCode], queryFn: () => getMyEvaluation(referenceCode) })
  const evaluation = evaluationQuery.data ?? null
  const invalidate = () => invalidateQuietly(queryClient, { queryKey: ['my-evaluation', referenceCode] })
  const errorMessage = (err: unknown, fallback: string) =>
    err instanceof EvaluationApiError && err.isConcurrencyConflict ? t('common.concurrencyConflict') : err instanceof EvaluationApiError ? err.message : fallback

  const scoreMutation = useMutation({
    mutationFn: ({ proposalId, criterionId, rawScore }: { proposalId: string; criterionId: string; rawScore: number }) =>
      scoreCriterion(referenceCode, { proposalId, criterionId, rawScore, commentAr: null, commentEn: null }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('evaluation.my.saved') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('evaluation.my.errors.scoreFailed')) }),
  })

  const submitMutation = useMutation({
    mutationFn: () => submitMyEvaluation(referenceCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('evaluation.my.submitted') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('evaluation.my.errors.submitFailed')) }),
  })

  if (evaluationQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }

  if (!evaluation) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('evaluation.my.notAssigned')}</p>
  }

  const draftKey = (proposalId: string, criterionId: string) => `${proposalId}:${criterionId}`
  const scoreFor = (proposalId: string, criterionId: string) =>
    evaluation.myScores.find((s) => s.proposalId === proposalId && s.criterionId === criterionId)
  const isSubmitted = !!evaluation.submittedAt

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('evaluation.my.title')} — {referenceCode}
        </h1>
        <Badge tone={isSubmitted ? 'success' : 'info'}>{evaluation.state}</Badge>
      </div>

      {evaluation.proposalIds.map((proposalId) => {
        const qualified = evaluation.technicallyQualifiedByProposal[proposalId] ?? false
        return (
          <Card key={proposalId} title={`${t('evaluation.my.proposal')}: ${proposalId}`}>
            <div className="mb-3">
              <Badge tone={qualified ? 'success' : 'warning'}>
                {qualified ? t('evaluation.my.qualified') : t('evaluation.my.notQualified')}
              </Badge>
            </div>
            <div className="flex flex-col gap-3">
              {evaluation.criteria.map((criterion) => {
                const locked = criterion.isFinancial && !qualified
                const existing = scoreFor(proposalId, criterion.id)
                const key = draftKey(proposalId, criterion.id)
                const value = drafts[key] ?? (existing ? String(existing.rawScore) : '')
                return (
                  <div key={criterion.id} className="flex flex-wrap items-center gap-2">
                    <span className="min-w-40">{criterion.nameEn}</span>
                    <Badge tone={criterion.isFinancial ? 'warning' : 'info'}>
                      {criterion.isFinancial ? t('evaluation.financialEnvelope') : t('evaluation.technicalEnvelope')}
                    </Badge>
                    <Input
                      type="number"
                      aria-label={`${t('evaluation.my.score')}: ${criterion.nameEn}`}
                      placeholder={t('evaluation.my.scorePlaceholder')}
                      value={value}
                      disabled={locked || isSubmitted}
                      title={locked ? t('evaluation.my.financialLocked') : undefined}
                      onChange={(e) => setDrafts((prev) => ({ ...prev, [key]: e.target.value }))}
                      className="w-24"
                    />
                    <Button
                      size="sm"
                      disabled={locked || isSubmitted || value === ''}
                      isLoading={scoreMutation.isPending}
                      onClick={() => scoreMutation.mutate({ proposalId, criterionId: criterion.id, rawScore: Number(value) })}
                    >
                      {t('evaluation.my.save')}
                    </Button>
                    {locked ? <span style={{ color: 'var(--color-text-secondary)' }}>{t('evaluation.my.financialLocked')}</span> : null}
                  </div>
                )
              })}
            </div>
          </Card>
        )
      })}

      <div>
        {isSubmitted ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('evaluation.my.alreadySubmitted')}</p>
        ) : (
          <Button isLoading={submitMutation.isPending} onClick={() => submitMutation.mutate()}>
            {t('evaluation.my.submit')}
          </Button>
        )}
      </div>
    </div>
  )
}
