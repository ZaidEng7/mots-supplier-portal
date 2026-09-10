import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Badge, PageHeading, StatusChip } from '../../../components/ui'
import { getRfq } from '../../../api/rfqs'
import { formatRelative } from '../../../lib/datetime'

/**
 * The tender's own identity, said the same way on every one of its six views.
 *
 * <p><b>The defect this closes.</b> The six tabs headed their pages three different ways. Two named the
 * tender and put its code underneath. Three named the TAB - "Award", "My evaluation", "Comparison" -
 * and joined a reference code to it with a dash, so the page's largest text repeated the tab strip
 * directly below it and the tender's own name appeared nowhere. A reader arriving at the award screen
 * from a link could not tell which tender they were awarding without reading the code.</p>
 *
 * <p>One record seen six ways: the head says which record, the strip says which way. That is the
 * approved template's workspace and it is the only arrangement in which both are worth reading.</p>
 *
 * <p><b>It reads its own tender,</b> for the same reason the strip beside it reads its own counts: four
 * of the six screens hold neither the tender nor its workspace, and threading one object through six
 * signatures would put it in four components with no other use for it. The query key is the one the
 * tender screens already use and the one `TenderTabs` already issues, so this is the cached answer
 * rather than a second request.</p>
 */
export function TenderHeader({ referenceCode, actions }: Readonly<{
  referenceCode: string
  /** What this particular view lets you do. The identity is shared; the actions are not. */
  actions?: ReactNode
}>) {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const rfqQuery = useQuery({ queryKey: ['rfq', referenceCode], queryFn: () => getRfq(referenceCode) })
  const rfq = rfqQuery.data

  /**
   * The code alone until the tender arrives, and never a spinner.
   *
   * <p>This sits above the tab strip on every view, so a placeholder here would flash on every
   * navigation between tabs. The code is the one fact the screen holds before any request answers, and
   * it is enough to say which tender this is.</p>
   */
  if (!rfq) return <PageHeading title={referenceCode} actions={actions} />

  const isSubmissionOpen = rfq.state === 'SubmissionOpen'

  return (
    <PageHeading
      title={isArabic ? rfq.titleAr : rfq.titleEn}
      subtitle={[
        rfq.referenceCode,
        `${t('rfq.ownership.ownerLabel')}: ${rfq.ownerName ?? t('rfq.unassigned')}`,
        rfq.assignedApproverName ? `${t('rfq.ownership.approverLabel')}: ${rfq.assignedApproverName}` : null,
      ].filter(Boolean).join(' · ')}
      meta={
        <>
          <StatusChip machine="rfq" value={rfq.state} />
          {/* The second chip, and the one a reader acts on. A state of "Open for submissions" does not
              say whether that means today or next month. Only while it is open: on a Draft or an
              Awarded tender the same date is history rather than a countdown. */}
          {isSubmissionOpen && rfq.submissionClosesAt ? (
            <Badge>{t('rfq.closes', { when: formatRelative(rfq.submissionClosesAt, i18n.language) })}</Badge>
          ) : null}
        </>
      }
      actions={actions}
    />
  )
}
