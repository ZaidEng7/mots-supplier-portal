import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import { Badge, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../../components/ui'
import { getComparison } from '../../api/comparison'
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

  const comparisonQuery = useQuery({ queryKey: ['comparison', referenceCode], queryFn: () => getComparison(referenceCode) })
  const comparison = comparisonQuery.data ?? null

  if (comparisonQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }
  if (!comparison) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('comparison.notFound')}</p>
  }
  if (comparison.proposals.length === 0) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('comparison.empty')}</p>
  }

  const proposals = comparison.proposals
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
                        {price.unitPrice.toFixed(2)} {p.currencyCode}
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
                    {p.grandTotal.toFixed(2)} {p.currencyCode}
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
                            <span className="num" dir="ltr">{score.averageScore.toFixed(1)} / {score.maxScore}</span>
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
                    {p.weightedTotal !== null ? <span className="num font-[var(--fw-semibold)]" dir="ltr">{p.weightedTotal.toFixed(2)}</span> : '—'}
                  </TableCell>
                ))}
              </TableRow>
              <TableRow>
                <TableCell sticky className="font-[var(--fw-semibold)]">{t('comparison.rank')}</TableCell>
                {proposals.map((p) => (
                  <TableCell key={p.supplierId}>{p.rank ?? '—'}</TableCell>
                ))}
              </TableRow>
            </>
          ) : null}
        </TableBody>
      </Table>
    </div>
  )
}
