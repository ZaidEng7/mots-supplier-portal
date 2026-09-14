// SCR-500, the evaluator dashboard: /evaluation, P0, FR-DSH-004, backlog T3-02.
//
// This is the screen that makes EPIC-11 reachable. Evaluation scoring was complete and had no navigable path from
// anywhere in the app: an evaluator could be assigned work and had no way to find it. Everything else on this screen is
// secondary to the list existing at all.
//
// Sub-tabs are IA §4.3's own: "My Evaluations -> tabs Assigned · In Progress · Submitted". The server derives which tab
// an assignment belongs in, so the tab a row appears under and the progress shown on it cannot disagree.
//
// The page's NAME comes first, then the card. This screen carried its title in a card header band, which renders at body
// size, so it had no page heading at all.
//
// The empty state is UX-WRITING.md §4's own row for this persona - "Evaluator - nothing assigned | 'Nothing to
// evaluate' | 'Proposals assigned to you for scoring will appear here.' | -" - transcribed, including the absent action.
//
// State labels come from the catalogue, never the raw enum name, and the date carries the timezone §6.2 requires,
// because a date an evaluator can miss is a deadline.

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { listMyAssignments, type MyAssignmentTab } from '../api/myEvaluations'
import { Card } from '../components/ui/Card'
import { PageHeading } from '../components/ui/ListScreen'
import { SkeletonList } from '../components/ui/Skeleton'
import { StatusChip } from '../components/ui/StatusChip'
import { Button } from '../components/ui/Button'
import { formatDeadline, formatNumber } from '../lib/datetime'

export function EvaluationDashboardPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  const [tab, setTab] = useState<MyAssignmentTab>('Assigned')

  const query = useQuery({
    queryKey: ['my-evaluations', tab],
    queryFn: () => listMyAssignments(tab),
  })

  const tabs: MyAssignmentTab[] = ['Assigned', 'InProgress', 'Submitted']

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('evaluationDashboard.title')} />
      <Card>
      <div role="tablist" aria-label={t('evaluationDashboard.title')} className="mb-4 flex flex-wrap gap-2">
        {tabs.map((candidate) => (
          <Button
            key={candidate}
            role="tab"
            aria-selected={tab === candidate}
            variant={tab === candidate ? 'primary' : 'ghost'}
            size="sm"
            onClick={() => setTab(candidate)}
          >
            {t(`evaluationDashboard.tabs.${candidate}`)}
          </Button>
        ))}
      </div>

      {query.isPending ? <SkeletonList label={t('evaluationDashboard.title')} rows={4} /> : null}

      {query.isError ? (
        <div>
          <p>{t('evaluationDashboard.loadFailed')}</p>
          <Button size="sm" variant="ghost" onClick={() => query.refetch()}>{t('evaluationDashboard.retry')}</Button>
        </div>
      ) : null}

      {query.data?.length === 0 ? (
        <div className="py-8 text-center">
          <p className="font-[var(--fw-semibold)]">{t('evaluationDashboard.emptyTitle')}</p>
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('evaluationDashboard.emptyBody')}</p>
        </div>
      ) : null}

      <ul className="flex flex-col gap-3">
        {(query.data ?? []).map((assignment) => (
          <li key={assignment.rfqReferenceCode}
            className="flex flex-col gap-2 rounded-[var(--radius-md)] p-3"
            style={{ border: '1px solid var(--color-border)' }}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="font-[var(--fw-semibold)]">
                {isArabic ? assignment.rfqTitleAr : assignment.rfqTitleEn}
              </span>
              <StatusChip machine="evaluation" value={assignment.evaluationState} />
            </div>

            <div className="flex flex-wrap gap-4 text-[length:var(--text-body-sm)]"
              style={{ color: 'var(--color-text-secondary)' }}>
              <span>{t('evaluationDashboard.progress', {
                done: formatNumber(assignment.scoresRecorded, locale, 0),
                total: formatNumber(assignment.scoresExpected, locale, 0),
              })}</span>

              <span>
                {assignment.evaluationTargetDate
                  ? t('evaluationDashboard.due', { date: formatDeadline(assignment.evaluationTargetDate, locale) })
                  : t('evaluationDashboard.noDueDate')}
              </span>
            </div>

            <Link
              to="/back-office/rfqs/$referenceCode/my-evaluation"
              params={{ referenceCode: assignment.rfqReferenceCode }}
              className="text-[length:var(--text-body-sm)]"
            >
              {assignment.submittedAt ? t('evaluationDashboard.review') : t('evaluationDashboard.score')}
            </Link>
          </li>
        ))}
      </ul>
      </Card>
    </div>
  )
}
