import { formatCurrency, formatNumber } from '../../lib/datetime'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from '@tanstack/react-router'
import { Badge, Button, Input, SkeletonTable, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../../components/ui'
import { getComparison, resolveEvaluationTie } from '../../api/comparison'
import { requestProposalClarification } from '../../api/proposals'
import type { ComparisonProposal } from '../../api/comparison'

/** Lowest wins for a price line/grand total; the domain never documents a different direction for
 * any line (BUSINESS-RULES.md's own procurement-lowest-price convention) - this is the ONE
 * direction, not a hardcoded assumption applied blindly to unrelated metrics (score/rank use their
 * own, opposite direction below). */
function lowestValueIds<T>(rows: readonly T[], getSupplierId: (row: T) => string, getValue: (row: T) => number | null): Set<string> {
  const withValues = rows.map((r) => ({ id: getSupplierId(r), value: getValue(r) })).filter((r) => r.value !== null) as { id: string; value: number }[]
  if (withValues.length === 0) return new Set()
  const min = Math.min(...withValues.map((r) => r.value))
  return new Set(withValues.filter((r) => r.value === min).map((r) => r.id))
}

function highestValueIds<T>(rows: readonly T[], getSupplierId: (row: T) => string, getValue: (row: T) => number | null): Set<string> {
  const withValues = rows.map((r) => ({ id: getSupplierId(r), value: getValue(r) })).filter((r) => r.value !== null) as { id: string; value: number }[]
  if (withValues.length === 0) return new Set()
  const max = Math.max(...withValues.map((r) => r.value))
  return new Set(withValues.filter((r) => r.value === max).map((r) => r.id))
}

/** FEAT-12.1..12.6/FR-CMP-001..006. No currency normalization anywhere in this page - OQ-007's
 * recorded interim decision ("amounts shown in entered currency; no FX engine") is what this build
 * follows, not FEAT-12.3's own "normalize to a display currency" language, which OQ-007 already
 * overrides the same way EPIC-10 built against OQ-008 over FEAT-10.2. Each proposal's totals render
 * in its own CurrencyCode, full stop. */
export function ComparisonPage() {
  const { referenceCode } = useParams({ from: '/back-office/rfqs/$referenceCode/comparison' })
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  const queryClient = useQueryClient()
  const { notify } = useToast()
  const [reasons, setReasons] = useState<Record<string, string>>({})

  const comparisonQuery = useQuery({ queryKey: ['comparison', referenceCode], queryFn: () => getComparison(referenceCode) })
  const comparison = comparisonQuery.data ?? null

  // A-1: hooks before the early returns below, which is why this sits here rather than beside the
  // panel it drives.
  // B-1/SCR-433: ask a bidder to clarify. The endpoint has existed since T-051 and nothing called it.
  // Placed here because the comparison is where a buyer is looking at the bids and notices what is
  // missing - and §4.1's transition is UnderReview -> ClarificationRequested, which is where these
  // proposals are.
  const [clarifyReasons, setClarifyReasons] = useState<Record<string, string>>({})
  const clarifyMutation = useMutation({
    mutationFn: ({ proposalCode, reason }: { proposalCode: string; reason: string }) =>
      requestProposalClarification(proposalCode, reason),
    onSuccess: (_, { proposalCode }) => {
      void queryClient.invalidateQueries({ queryKey: ['comparison', referenceCode] })
      setClarifyReasons((prev) => { const next = { ...prev }; delete next[proposalCode]; return next })
      notify({ kind: 'success', title: t('comparison.clarifyRequested') })
    },
    onError: (error: Error) => notify({ kind: 'danger', title: error.message || t('comparison.clarifyFailed') }),
  })

  const resolveMutation = useMutation({
    mutationFn: ({ proposalCode, reason }: { proposalCode: string; reason: string }) =>
      resolveEvaluationTie(referenceCode, proposalCode, reason),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['comparison', referenceCode] })
      notify({ kind: 'success', title: t('comparison.tieResolved') })
    },
    onError: (error: Error) => notify({ kind: 'danger', title: error.message || t('comparison.tieResolveFailed') }),
  })

  if (comparisonQuery.isLoading) {
    return <SkeletonTable label={t('common.loading')} />
  }
  if (!comparison) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('comparison.notFound')}</p>
  }
  if (comparison.proposals.length === 0) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('comparison.empty')}</p>
  }

  const proposals = comparison.proposals
  const tied = proposals.filter((p) => p.tieUnresolved)
  const consolidatedOrLater = comparison.evaluationState === 'Consolidated' || comparison.evaluationState === 'Finalized'
  const criteria = proposals.find((p) => p.criterionScores)?.criterionScores ?? null

  const lineHighlights = new Map(
    comparison.rfqItems.map((item) => [
      item.id,
      lowestValueIds(proposals, (p) => p.supplierId, (p) => p.items?.find((i) => i.rfqItemId === item.id)?.unitPrice ?? null),
    ]),
  )
  const grandTotalHighlight = lowestValueIds(proposals, (p) => p.supplierId, (p) => p.grandTotal)
  const weightedTotalHighlight = highestValueIds(proposals, (p) => p.supplierId, (p) => p.weightedTotal)
  const criterionHighlights = new Map(
    (criteria ?? []).map((criterion) => [
      criterion.criterionId,
      highestValueIds(
        proposals,
        (p) => p.supplierId,
        (p) => p.criterionScores?.find((c) => c.criterionId === criterion.criterionId)?.averageScore ?? null,
      ),
    ]),
  )

  const supplierName = (p: ComparisonProposal) => (isArabic ? p.supplierDisplayNameAr : p.supplierDisplayNameEn)
  const scoreFor = (p: ComparisonProposal, criterionId: string) => p.criterionScores?.find((c) => c.criterionId === criterionId) ?? null

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('comparison.title')} — {isArabic ? comparison.rfqTitleAr : comparison.rfqTitleEn}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('comparison.proposalCount', { count: proposals.length })}
        </p>
        {!consolidatedOrLater ? (
          <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('comparison.awaitingConsolidation')}
          </p>
        ) : null}
      </div>

      <Table caption={t('comparison.title')} maxHeight="70vh">
        <TableHead sticky>
          <TableHeaderCell sticky>{t('comparison.rowLabel')}</TableHeaderCell>
          {proposals.map((p) => (
            <TableHeaderCell key={p.supplierId}>
              <div className="flex flex-col">
                <span>{supplierName(p)}</span>
                <span className="font-[var(--fw-regular)] text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
                  {p.proposalReferenceCode}
                </span>
              </div>
            </TableHeaderCell>
          ))}
        </TableHead>
        <TableBody>
          {/* Group 1: Commercial */}
          <TableRow>
            <TableCell sticky className="font-[var(--fw-semibold)]">
              {t('comparison.groups.commercial')}
            </TableCell>
            {proposals.map((p) => <TableCell key={p.supplierId}>{''}</TableCell>)}
          </TableRow>
          {comparison.rfqItems.map((item) => (
            <TableRow key={item.id}>
              <TableCell sticky>{isArabic ? item.titleAr : item.titleEn}</TableCell>
              {proposals.map((p) => {
                const price = p.items?.find((i) => i.rfqItemId === item.id)
                const highlight = lineHighlights.get(item.id)?.has(p.supplierId) ?? false
                return (
                  <TableCell key={p.supplierId} highlight={highlight}>
                    {price ? (
                      <span className="num" dir="ltr">
                        {formatCurrency(price.unitPrice, p.currencyCode, locale)}
                      </span>
                    ) : (
                      <span style={{ color: 'var(--color-text-secondary)' }}>{t('comparison.notVisible')}</span>
                    )}
                  </TableCell>
                )
              })}
            </TableRow>
          ))}
          <TableRow>
            <TableCell sticky className="font-[var(--fw-semibold)]">{t('comparison.grandTotal')}</TableCell>
            {proposals.map((p) => (
              <TableCell key={p.supplierId} highlight={grandTotalHighlight.has(p.supplierId)}>
                {p.grandTotal !== null ? (
                  <span className="num font-[var(--fw-semibold)]" dir="ltr">
                    {formatCurrency(p.grandTotal, p.currencyCode, locale)}
                  </span>
                ) : (
                  <span style={{ color: 'var(--color-text-secondary)' }}>{t('comparison.notVisible')}</span>
                )}
              </TableCell>
            ))}
          </TableRow>
          <TableRow>
            <TableCell sticky>{t('comparison.paymentTerms')}</TableCell>
            {proposals.map((p) => <TableCell key={p.supplierId}>{p.paymentTerms ?? '—'}</TableCell>)}
          </TableRow>
          <TableRow>
            <TableCell sticky>{t('comparison.incoterm')}</TableCell>
            {proposals.map((p) => <TableCell key={p.supplierId}>{p.incotermCode ?? '—'}</TableCell>)}
          </TableRow>
          <TableRow>
            <TableCell sticky>{t('comparison.validityEnd')}</TableCell>
            {proposals.map((p) => <TableCell key={p.supplierId}>{p.validityEnd ?? '—'}</TableCell>)}
          </TableRow>

          {/* Group 2: Requirements */}
          <TableRow>
            <TableCell sticky className="font-[var(--fw-semibold)]">{t('comparison.groups.requirements')}</TableCell>
            {proposals.map((p) => <TableCell key={p.supplierId}>{''}</TableCell>)}
          </TableRow>
          {(proposals[0]?.requirements ?? []).map((req) => (
            <TableRow key={req.requirementId}>
              <TableCell sticky>{isArabic ? req.textAr : req.textEn}</TableCell>
              {proposals.map((p) => {
                const answer = p.requirements.find((r) => r.requirementId === req.requirementId)
                return (
                  <TableCell key={p.supplierId}>
                    <Badge tone={answer?.answered ? 'success' : 'danger'}>
                      {answer?.answered ? t('comparison.met') : t('comparison.notMet')}
                    </Badge>
                  </TableCell>
                )
              })}
            </TableRow>
          ))}

          {/* Group 3: Evaluation - only once Consolidated+ */}
          {consolidatedOrLater ? (
            <>
              <TableRow>
                <TableCell sticky className="font-[var(--fw-semibold)]">{t('comparison.groups.evaluation')}</TableCell>
                {proposals.map((p) => <TableCell key={p.supplierId}>{''}</TableCell>)}
              </TableRow>
              {(criteria ?? []).map((criterion) => (
                <TableRow key={criterion.criterionId}>
                  <TableCell sticky>{isArabic ? criterion.nameAr : criterion.nameEn}</TableCell>
                  {proposals.map((p) => {
                    const score = scoreFor(p, criterion.criterionId)
                    const highlight = criterionHighlights.get(criterion.criterionId)?.has(p.supplierId) ?? false
                    return (
                      <TableCell key={p.supplierId} highlight={highlight}>
                        {score ? (
                          <span className="flex items-center gap-2">
                            <span className="num" dir="ltr">{formatNumber(score.averageScore, locale, 1)} / {formatNumber(score.maxScore, locale, 0)}</span>
                            {score.metThreshold !== null ? (
                              <Badge tone={score.metThreshold ? 'success' : 'danger'}>
                                {score.metThreshold ? t('comparison.pass') : t('comparison.fail')}
                              </Badge>
                            ) : null}
                          </span>
                        ) : '—'}
                      </TableCell>
                    )
                  })}
                </TableRow>
              ))}
              <TableRow>
                <TableCell sticky className="font-[var(--fw-semibold)]">{t('comparison.qualification')}</TableCell>
                {proposals.map((p) => (
                  <TableCell key={p.supplierId}>
                    {p.technicallyQualified === null ? '—' : (
                      <Badge tone={p.technicallyQualified ? 'success' : 'danger'}>
                        {p.technicallyQualified ? t('comparison.qualified') : t('comparison.notQualified')}
                      </Badge>
                    )}
                  </TableCell>
                ))}
              </TableRow>
              <TableRow>
                <TableCell sticky className="font-[var(--fw-semibold)]">{t('comparison.weightedTotal')}</TableCell>
                {proposals.map((p) => (
                  <TableCell key={p.supplierId} highlight={weightedTotalHighlight.has(p.supplierId)}>
                    {p.weightedTotal !== null ? <span className="num font-[var(--fw-semibold)]" dir="ltr">{formatNumber(p.weightedTotal, locale)}</span> : '—'}
                  </TableCell>
                ))}
              </TableRow>
              <TableRow>
                <TableCell sticky className="font-[var(--fw-semibold)]">{t('comparison.rank')}</TableCell>
                {proposals.map((p) => (
                  <TableCell key={p.supplierId}>
                    {p.rank ?? '—'}
                    {/* A-1: a rank that came from a tie no rule broke is marked, because acting on it
                        as though it were decided is the thing that gets challenged. */}
                    {p.tieUnresolved ? (
                      <span className="ms-2"><Badge tone="warning">{t('comparison.tieUnresolved')}</Badge></span>
                    ) : null}
                  </TableCell>
                ))}
              </TableRow>
            </>
          ) : null}
        </TableBody>
      </Table>

      {/*
        A-1: the tie is surfaced HERE because this is where the officer sees the ranking, and the award
        flow refuses to offer rank 1 until someone has resolved it. A reason is mandatory - a tie broken
        with no stated basis is exactly what the system refused to do, so a person must not do it either.
      */}
      {/*
        B-1/SCR-433. A reason is mandatory - the endpoint reuses WithdrawProposalRequest's validator, and a
        clarification request with no stated question is not one.
      */}
      <div className="mt-6 rounded-[var(--radius-md)] p-4" style={{ border: '1px solid var(--color-border)' }}>
        <p className="font-[var(--fw-semibold)]">{t('comparison.clarifyTitle')}</p>
        <p className="mb-3" style={{ color: 'var(--color-text-secondary)' }}>{t('comparison.clarifyBody')}</p>
        <div className="flex flex-col gap-2">
          {proposals.map((p) => (
            <div key={`clarify-${p.proposalReferenceCode}`} className="flex flex-wrap items-end gap-2">
              <span className="num">{p.proposalReferenceCode}</span>
              <Input
                aria-label={t('comparison.clarifyReason', { code: p.proposalReferenceCode })}
                placeholder={t('comparison.clarifyReasonPlaceholder')}
                value={clarifyReasons[p.proposalReferenceCode] ?? ''}
                onChange={(e) => setClarifyReasons((prev) => ({ ...prev, [p.proposalReferenceCode]: e.target.value }))}
              />
              <Button
                size="sm"
                variant="secondary"
                disabled={!clarifyReasons[p.proposalReferenceCode] || clarifyMutation.isPending}
                onClick={() => clarifyMutation.mutate({
                  proposalCode: p.proposalReferenceCode,
                  reason: clarifyReasons[p.proposalReferenceCode]!,
                })}
              >
                {t('comparison.clarifyAsk')}
              </Button>
            </div>
          ))}
        </div>
      </div>

      {tied.length > 0 ? (
        <div className="mt-6 rounded-[var(--radius-md)] p-4" style={{ border: '1px solid var(--color-border)' }}>
          <p className="font-[var(--fw-semibold)]">{t('comparison.tieTitle')}</p>
          <p className="mb-3" style={{ color: 'var(--color-text-secondary)' }}>{t('comparison.tieBody')}</p>
          <div className="flex flex-col gap-2">
            {tied.map((p) => (
              <div key={p.proposalReferenceCode} className="flex flex-wrap items-end gap-2">
                <span className="num">{p.proposalReferenceCode}</span>
                <Input
                  aria-label={t('comparison.tieReason', { code: p.proposalReferenceCode })}
                  placeholder={t('comparison.tieReasonPlaceholder')}
                  value={reasons[p.proposalReferenceCode] ?? ''}
                  onChange={(e) => setReasons((prev) => ({ ...prev, [p.proposalReferenceCode]: e.target.value }))}
                />
                <Button
                  size="sm"
                  disabled={!reasons[p.proposalReferenceCode] || resolveMutation.isPending}
                  onClick={() => resolveMutation.mutate({
                    proposalCode: p.proposalReferenceCode,
                    reason: reasons[p.proposalReferenceCode]!,
                  })}
                >
                  {t('comparison.tieResolve')}
                </Button>
              </div>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  )
}
