import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import { Badge, Button, Card, Input, SkeletonList, StatusChip, useToast } from '../../components/ui'
import { invalidateQuietly } from '../../lib/queryClient'
import { formatNumber } from '../../lib/datetime'
import {
  getMyEvaluation, scoreCriterion, submitMyEvaluation, evaluatorProposalDocumentUrl, EvaluationApiError,
  getConflictDeclaration, declareConflict,
} from '../../api/evaluations'

/** FEAT-11.3/11.5/FR-EVL-003..006: the evaluator's own scoring workspace - blind to every other
 * evaluator's scores (see getMyEvaluation's own doc comment), and the financial criteria for a
 * proposal are greyed out (never gated by hiding alone - the server refuses the write regardless,
 * see ScoreCriterionHandler) until that proposal passes technical qualification for THIS
 * evaluator. */
export function MyEvaluationPage() {
  const { referenceCode } = useParams({ from: '/back-office/rfqs/$referenceCode/my-evaluation' })
  const { t, i18n } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'

  /**
   * T-067: opens a technical supporting file. Two steps, because §4.2 mandates a signed URL rather
   * than a streamed body - the route returns the URL and the browser follows it. A failure is
   * surfaced as a toast rather than a silent no-op, because an evaluator who clicks a filename and
   * gets nothing has no way to tell a permission refusal from a dead link.
   */
  const openProposalDocument = async (rfqCode: string, proposalCode: string, documentId: string, fileName: string) => {
    try {
      const url = await evaluatorProposalDocumentUrl(rfqCode, proposalCode, documentId)
      window.open(url, '_blank', 'noopener,noreferrer')
    } catch {
      notify({ kind: 'danger', title: t('evaluation.my.errors.documentFailed', { fileName }) })
    }
  }

  /*
    A-8/BRULE-067: the declaration comes FIRST, and the workspace query waits for it.

    `enabled` is the whole mechanism. Reading my-evaluation opens scoring as a documented side effect,
    so loading both at once would pass the declaration window before the evaluator had seen a single
    name - which is precisely the window BRULE-067's recusal needs.
  */
  const declarationQuery = useQuery({
    queryKey: ['conflict-declaration', referenceCode],
    queryFn: () => getConflictDeclaration(referenceCode),
  })
  const declarationRequired = declarationQuery.data?.declarationRequired === true

  const [conflictReason, setConflictReason] = useState('')
  const declareMutation = useMutation({
    mutationFn: (hasConflict: boolean) => declareConflict(referenceCode, hasConflict, hasConflict ? conflictReason : undefined),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['conflict-declaration', referenceCode] })
      void queryClient.invalidateQueries({ queryKey: ['my-evaluation', referenceCode] })
    },
    onError: () => notify({ kind: 'danger', title: t('evaluation.my.declaration.failed') }),
  })

  const evaluationQuery = useQuery({
    queryKey: ['my-evaluation', referenceCode],
    queryFn: () => getMyEvaluation(referenceCode),
    enabled: declarationQuery.isSuccess && !declarationRequired,
  })
  const evaluation = evaluationQuery.data ?? null
  const invalidate = () => invalidateQuietly(queryClient, { queryKey: ['my-evaluation', referenceCode] })
  const errorMessage = (err: unknown, fallback: string) =>
    err instanceof EvaluationApiError && err.isConcurrencyConflict ? t('common.concurrencyConflict') : err instanceof EvaluationApiError ? err.message : fallback

  const scoreMutation = useMutation({
    mutationFn: ({ proposalCode, criterionId, rawScore }: { proposalCode: string; criterionId: string; rawScore: number }) =>
      scoreCriterion(referenceCode, { proposalCode, criterionId, rawScore, commentAr: null, commentEn: null }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('evaluation.my.saved') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('evaluation.my.errors.scoreFailed')) }),
  })

  const submitMutation = useMutation({
    mutationFn: () => submitMyEvaluation(referenceCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('evaluation.my.submitted') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('evaluation.my.errors.submitFailed')) }),
  })

  if (declarationQuery.isLoading) {
    return <SkeletonList label={t('common.loading')} />
  }

  /*
    A-8: the recusal declaration, shown once, before any scoring.

    The bidder names are HERE and nowhere else during the evaluation. An evaluator who recognises a
    conflict says so and is recused with their reason; one who does not proceeds, and from that point on
    the bids are pseudonymous until consolidation. Nobody has to recuse themselves from a bidder they
    cannot see, because the declaration already happened.
  */
  if (declarationRequired) {
    const bidders = declarationQuery.data?.bidders ?? []
    return (
      <Card title={t('evaluation.my.declaration.title')}>
        <p className="mb-3" style={{ color: 'var(--color-text-secondary)' }}>{t('evaluation.my.declaration.body')}</p>
        <ul className="mb-4 flex flex-col gap-1">
          {bidders.map((bidder) => (
            <li key={bidder.proposalCode}>
              {i18n.language.startsWith('ar') ? bidder.supplierDisplayNameAr : bidder.supplierDisplayNameEn}
            </li>
          ))}
        </ul>
        <div className="flex flex-wrap items-end gap-2">
          <Button size="sm" disabled={declareMutation.isPending} onClick={() => declareMutation.mutate(false)}>
            {t('evaluation.my.declaration.noConflict')}
          </Button>
          <Input
            aria-label={t('evaluation.my.declaration.reasonLabel')}
            placeholder={t('evaluation.my.declaration.reasonPlaceholder')}
            value={conflictReason}
            onChange={(event) => setConflictReason(event.target.value)}
          />
          <Button
            size="sm"
            variant="secondary"
            disabled={!conflictReason || declareMutation.isPending}
            onClick={() => declareMutation.mutate(true)}
          >
            {t('evaluation.my.declaration.hasConflict')}
          </Button>
        </div>
      </Card>
    )
  }

  if (evaluationQuery.isLoading) {
    return <SkeletonList label={t('common.loading')} />
  }

  if (!evaluation) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('evaluation.my.notAssigned')}</p>
  }

  const draftKey = (proposalCode: string, criterionId: string) => `${proposalCode}:${criterionId}`
  const scoreFor = (proposalCode: string, criterionId: string) =>
    evaluation.myScores.find((s) => s.proposalCode === proposalCode && s.criterionId === criterionId)
  const isSubmitted = !!evaluation.submittedAt
  const isArabic = i18n.language.startsWith('ar')

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('evaluation.my.title')} — {referenceCode}
        </h1>
        <StatusChip machine="evaluation" value={evaluation.state} />
      </div>

      {/*
        T-067: the SPECIFICATION, on the screen where the scoring happens. Before this an evaluator
        held neither rfq.read nor comparison.view, so the requirement they were scoring against was
        not reachable from anywhere in the product - see EvaluatorVisibilityTests.
      */}
      <Card title={`${t('evaluation.my.specification')}: ${isArabic ? evaluation.rfqTitleAr : evaluation.rfqTitleEn}`}>
        {(isArabic ? evaluation.rfqDescriptionAr : evaluation.rfqDescriptionEn) ? (
          <p className="mb-3">{isArabic ? evaluation.rfqDescriptionAr : evaluation.rfqDescriptionEn}</p>
        ) : null}

        {evaluation.rfqItems.length > 0 ? (
          <>
            <h3 className="mb-1 text-[length:var(--text-body-md)]">{t('evaluation.my.items')}</h3>
            <ul className="mb-3 list-inside list-disc">
              {evaluation.rfqItems.map((item) => (
                <li key={item.id}>
                  {isArabic ? item.titleAr : item.titleEn} — {formatNumber(item.quantity, locale, 0)} {item.unitOfMeasureCode}
                </li>
              ))}
            </ul>
          </>
        ) : null}

        {evaluation.rfqRequirements.length > 0 ? (
          <>
            <h3 className="mb-1 text-[length:var(--text-body-md)]">{t('evaluation.my.requirements')}</h3>
            <ul className="list-inside list-disc">
              {evaluation.rfqRequirements.map((req) => (
                <li key={req.id}>
                  {isArabic ? req.textAr : req.textEn}
                  {req.isMandatory ? ` (${t('evaluation.my.mandatory')})` : ''}
                </li>
              ))}
            </ul>
          </>
        ) : null}
      </Card>

      {evaluation.proposals.map((proposal) => {
        const proposalCode = proposal.proposalCode
        const qualified = proposal.technicallyQualified
        const narrative = isArabic ? proposal.narrativeAr : proposal.narrativeEn
        return (
          <Card key={proposalCode} title={`${t('evaluation.my.proposal')}: ${proposalCode}`}>
            <div className="mb-3 flex flex-wrap items-center gap-2">
              <Badge tone={qualified ? 'success' : 'warning'}>
                {qualified ? t('evaluation.my.qualified') : t('evaluation.my.notQualified')}
              </Badge>
              {/*
                A-8: the bidder is ANONYMOUS while scoring is open, and named at the two moments where
                the name is the point - before scoring opens, which is the recusal declaration
                (BRULE-067), and after consolidation, when the scores are locked. Supersedes D-19,
                which widened this view to include the name precisely so recusal was possible; A-8
                moves recusal earlier instead.

                The pseudonym is always shown, so a committee can discuss "Bidder B" either way.
              */}
              <span style={{ color: 'var(--color-text-secondary)' }}>
                {isArabic ? proposal.bidderLabelAr : proposal.bidderLabelEn}
              </span>
              {proposal.supplierDisplayNameEn !== null ? (
                <span style={{ color: 'var(--color-text-secondary)' }}>
                  · {isArabic ? proposal.supplierDisplayNameAr : proposal.supplierDisplayNameEn}
                </span>
              ) : (
                <Badge tone="info">{t('evaluation.my.anonymousBidder')}</Badge>
              )}
            </div>

            {/* T-067: the bid's own technical content, which is what is being scored. */}
            {narrative ? (
              <section className="mb-3">
                <h3 className="mb-1 text-[length:var(--text-body-md)]">{t('evaluation.my.narrative')}</h3>
                <p>{narrative}</p>
              </section>
            ) : null}

            {proposal.requirementAnswers.length > 0 ? (
              <section className="mb-3">
                <h3 className="mb-1 text-[length:var(--text-body-md)]">{t('evaluation.my.answers')}</h3>
                <ul className="list-inside list-disc">
                  {proposal.requirementAnswers.map((answer) => (
                    <li key={answer.id}>{isArabic ? answer.answerAr : answer.answerEn}</li>
                  ))}
                </ul>
              </section>
            ) : null}

            {proposal.documents.length > 0 ? (
              <section className="mb-3">
                <h3 className="mb-1 text-[length:var(--text-body-md)]">{t('evaluation.my.documents')}</h3>
                <ul className="list-inside list-disc">
                  {proposal.documents.map((doc) => (
                    <li key={doc.id}>
                      <Button
                        size="sm"
                        variant="secondary"
                        onClick={() => void openProposalDocument(referenceCode, proposalCode, doc.id, doc.originalFileName)}
                      >
                        {doc.originalFileName}
                      </Button>
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}

            <div className="flex flex-col gap-3">
              {evaluation.criteria.map((criterion) => {
                const locked = criterion.isFinancial && !qualified
                const existing = scoreFor(proposalCode, criterion.id)
                const key = draftKey(proposalCode, criterion.id)
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
                      onClick={() => scoreMutation.mutate({ proposalCode, criterionId: criterion.id, rawScore: Number(value) })}
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
