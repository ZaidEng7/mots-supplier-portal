import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { Badge, Card, Skeleton, Table, TableBody, TableCell, TableHead, TableRow } from '../../components/ui'
import { getMyEvaluation } from '../../api/evaluations'

/**
 * SCR-501: the evaluator's brief — what is being bought, and how to score it, on one screen.
 *
 * <p><b>T-102 recorded this row as unresolved rather than missing,</b> because the open question was
 * whether the criteria list already WAS the brief. It was not, and the reason was a field nobody could
 * see: `Criterion` has carried `guidance` since EPIC-07, the evaluation's own criterion snapshot never
 * copied it, and `MyEvaluationPage` therefore rendered name, weight and max — the numbers, with the
 * instruction missing. So the scoring screen could not have been the brief even if somebody decided it
 * should be.</p>
 *
 * <p><b>Read-only, and separate from the scoring form on purpose.</b> An evaluator reads this before
 * they start and returns to it when a criterion is ambiguous; mixing it into the form would put a wall
 * of instruction between them and the score fields every time they came back.</p>
 *
 * <p><b>Where there is no guidance it says so.</b> A tender that bound its template before the snapshot
 * carried guidance has none recorded, and the honest line is that it was not recorded — not a blank cell
 * that reads like a loading failure, and not the template's current text, which this tender never bound.</p>
 */
export function MyEvaluationBriefPage({ referenceCode }: { referenceCode: string }) {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')

  const briefQuery = useQuery({
    queryKey: ['my-evaluation', referenceCode],
    queryFn: () => getMyEvaluation(referenceCode),
  })

  if (briefQuery.isPending) return <Skeleton className="h-64 w-full" />
  if (briefQuery.isError || !briefQuery.data) {
    return <p style={{ color: 'var(--color-danger)' }}>{t('evaluationBrief.error')}</p>
  }

  const evaluation = briefQuery.data
  const totalWeight = evaluation.criteria.reduce((sum, c) => sum + c.weight, 0)

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('evaluationBrief.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {isArabic ? evaluation.rfqTitleAr : evaluation.rfqTitleEn} · {evaluation.rfqReferenceCode}
        </p>
        <Link
          to="/back-office/rfqs/$referenceCode/my-evaluation"
          params={{ referenceCode }}
          className="mt-2 inline-block text-[length:var(--text-body-sm)]"
        >
          {t('evaluationBrief.toScoring')}
        </Link>
      </div>

      <Card title={t('evaluationBrief.tender')}>
        <p style={{ color: 'var(--color-text-primary)' }}>
          {(isArabic ? evaluation.rfqDescriptionAr : evaluation.rfqDescriptionEn)
            ?? t('evaluationBrief.noDescription')}
        </p>
        {evaluation.rfqRequirements.length > 0 ? (
          <ul className="mt-4 list-disc ps-6">
            {evaluation.rfqRequirements.map((requirement) => (
              <li key={requirement.id} style={{ color: 'var(--color-text-primary)' }}>
                {isArabic ? requirement.textAr : requirement.textEn}
                {requirement.isMandatory ? (
                  <span className="ms-2"><Badge tone="info">{t('evaluationBrief.mandatory')}</Badge></span>
                ) : null}
              </li>
            ))}
          </ul>
        ) : null}
      </Card>

      <Card title={t('evaluationBrief.howToScore')}>
        {/* The weights as a total, because "60" means nothing without knowing it is 60 of 100 - and a
            template whose weights do not sum to 100 is a thing an evaluator should see rather than
            discover when their scores do not behave as they expect. */}
        <p className="mb-4 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('evaluationBrief.totalWeight', { total: totalWeight })}
        </p>

        <Table caption={t('evaluationBrief.howToScore')}>
          <TableHead labels={[t('evaluationBrief.fields.criterion'), t('evaluationBrief.fields.dimension'), t('evaluationBrief.fields.weight'), t('evaluationBrief.fields.max'), t('evaluationBrief.fields.threshold'), t('evaluationBrief.fields.guidance')]} />
          <TableBody>
            {evaluation.criteria.map((criterion) => {
              const guidance = isArabic ? criterion.guidanceAr : criterion.guidanceEn
              return (
                <TableRow key={criterion.id}>
                  <TableCell>
                    {isArabic ? criterion.nameAr : criterion.nameEn}
                    {criterion.requiresJustification ? (
                      <div className="mt-1">
                        {/* BRULE-061: the rule exists and the scoring form does not read it yet, so the
                            brief is where an evaluator learns a comment will be required. */}
                        <Badge tone="warning">{t('evaluationBrief.justificationRequired')}</Badge>
                      </div>
                    ) : null}
                  </TableCell>
                  <TableCell>{t(`evaluationBrief.dimension.${criterion.dimension}`)}</TableCell>
                  <TableCell>{criterion.weight}</TableCell>
                  <TableCell>{criterion.maxScore}</TableCell>
                  <TableCell>
                    {criterion.threshold === null
                      ? <span style={{ color: 'var(--color-text-secondary)' }}>{t('evaluationBrief.noThreshold')}</span>
                      : criterion.threshold}
                  </TableCell>
                  <TableCell>
                    {guidance
                      ? guidance
                      : <span style={{ color: 'var(--color-text-secondary)' }}>{t('evaluationBrief.noGuidance')}</span>}
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </Card>
    </div>
  )
}

/**
 * The route's own component: reads the param and hands it down.
 *
 * <p>Split from the page so the page takes the tender code as a PROP. Every other screen here calls
 * `useParams` inline, which means its tests must mock the router to render it at all — and a test that
 * mocks the router is testing the mock's idea of the URL. This one does not have to.</p>
 */
export function MyEvaluationBriefRoute() {
  const { referenceCode } = useParams({ from: '/back-office/rfqs/$referenceCode/brief' })
  return <MyEvaluationBriefPage referenceCode={referenceCode} />
}
