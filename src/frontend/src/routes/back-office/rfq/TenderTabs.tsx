// The views of one tender, as a strip rather than as a page, each offered only to a reader who can open it.
//
// What this replaces. The workspace stacked every section of the tender down a single column - the tender itself, its
// suppliers, its decisions and every management form - and reached the bids, the comparison and the award through buttons
// buried in a card two thirds of the way down. A reader had no way to tell what a tender contained without scrolling it,
// and no way back except the browser's own button.
//
// LINKS, NOT A TAB WIDGET. Each of these is a real route, so a bid list can be linked to, bookmarked and reached with the
// back button, and only the view being looked at is fetched. role="tablist" would promise arrow-key movement between panels
// that are all on one page, which is not what this is.
//
// EACH TAB IS OFFERED ONLY TO SOMEONE WHO CAN OPEN IT. The strip used to give every persona the same six tabs. An evaluator
// holds evaluation.score, evaluation.submit and rfq.clarify and nothing else, so on their own scoring screen they were
// offered six destinations that each answered 403 - none of them the screen they were on, so no tab was marked current and
// none led back to it. Tabs are now data with a `when`, written with the navigation's own `holds`, read through the same
// permission lookup the back-office shell uses, and filtered the way the shell filters its rows. Each `when` names the
// permission the endpoint behind that view enforces: the tender, its suppliers and its settings read GetRfq on rfq.read;
// the bids read ListReceivedProposals and the comparison GetComparison, both on comparison.view; the award reads GetAward on
// award.recommend and GetEvaluation on evaluation.open, so it needs both. The evaluator's own scoring is a tab too, on
// evaluation.score, placed beside the comparison it feeds - which is how a pure evaluator sees one tab, their own, rather
// than none. HIDE, NEVER GATE, as in the navigation: the API refuses every one of these reads regardless, so a hidden tab
// is a tidier surface and never a boundary. A reader offered no tab at all gets no strip, rather than a bordered landmark
// with nothing in it.
//
// The counts are the point of putting them here: "Suppliers" and "Suppliers 7" ask a reader for different amounts of work,
// and the second answers a question they would otherwise open the tab to ask. It reads its own counts, because seven screens
// render this strip and four of them - the bids, the comparison, the award and an evaluator's own scoring - hold neither the
// tender nor its workspace, so threading two numbers through all seven signatures would put them in four components that
// have no other use for them. The query keys are the ones the tender screens already use, so on those it is the cached
// answer rather than a second request. Both reads need rfq.read and are not issued without it: for an evaluator they could
// only answer 403, twice, on every view, and neither count belongs to a tab that reader is offered.
//
// A count is NULL rather than 0 when the answer has not arrived. A count of zero is a fact - nobody has bid - and printing
// it because a request failed says that fact wrongly. A tab with no number is honest about not knowing, and the strip is
// navigation first: it must still take you to the bids when the count of them could not be read.
//
// The tender's own tab is matched EXACTLY, because every other tab's path starts with it - a prefix match would mark
// "Tender" current from inside Settings, which is the same defect the supplier navigation had against its dashboard link.
// Every tab also asks the router to match exactly. TanStack's Link marks itself aria-current="page" by prefix and writes
// that after the props written here, so the rule above drew the Tender tab unselected inside Settings while a screen reader
// still heard it announced as the current page.
//
// The separator before a count is MARKUP rather than styling. Without it the accessible name computes as "Suppliers7",
// because gap is layout and a screen reader concatenates the text nodes.

import { Link, useRouterState } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { getRfq } from '../../../api/rfqs'
import { getWorkspace } from '../../../api/workspace'
import { usePermissions } from '../../../lib/authStore'
import { holds, type NavContext } from '../../../shells/navigation'

interface TenderTab {
  key: string
  to: string
  href: string
  label: string
  count: number | null
  exact: boolean
  when?: (context: Pick<NavContext, 'can'>) => boolean
}

export function TenderTabs({ referenceCode }: Readonly<{ referenceCode: string }>) {
  const { t } = useTranslation()
  const can = usePermissions()
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const canReadTender = can('rfq.read')
  const rfqQuery = useQuery({ queryKey: ['rfq', referenceCode], queryFn: () => getRfq(referenceCode), enabled: canReadTender })
  const workspaceQuery = useQuery({
    queryKey: ['workspace', referenceCode],
    queryFn: () => getWorkspace(referenceCode),
    enabled: canReadTender,
  })
  const invitedCount = rfqQuery.isSuccess ? rfqQuery.data.invitations.length : null
  const bidCount = workspaceQuery.isSuccess ? (workspaceQuery.data?.submittedProposalCount ?? null) : null
  const base = `/back-office/rfqs/${referenceCode}`

  const tabs: readonly TenderTab[] = [
    {
      key: 'tender', to: '/back-office/rfqs/$referenceCode', href: base, label: t('rfq.tabs.tender'),
      count: null, exact: true, when: holds('rfq.read'),
    },
    {
      key: 'suppliers', to: '/back-office/rfqs/$referenceCode/suppliers', href: `${base}/suppliers`, label: t('rfq.tabs.suppliers'),
      count: invitedCount, exact: false, when: holds('rfq.read'),
    },
    {
      key: 'bids', to: '/back-office/rfqs/$referenceCode/proposals', href: `${base}/proposals`, label: t('rfq.tabs.bids'),
      count: bidCount, exact: false, when: holds('comparison.view'),
    },
    {
      key: 'evaluation', to: '/back-office/rfqs/$referenceCode/comparison', href: `${base}/comparison`, label: t('rfq.tabs.evaluation'),
      count: null, exact: false, when: holds('comparison.view'),
    },
    {
      key: 'my-evaluation', to: '/back-office/rfqs/$referenceCode/my-evaluation', href: `${base}/my-evaluation`,
      label: t('rfq.tabs.myEvaluation'), count: null, exact: false, when: holds('evaluation.score'),
    },
    {
      key: 'award', to: '/back-office/rfqs/$referenceCode/award', href: `${base}/award`, label: t('rfq.tabs.award'),
      count: null, exact: false, when: holds('award.recommend', 'evaluation.open'),
    },
    {
      key: 'settings', to: '/back-office/rfqs/$referenceCode/settings', href: `${base}/settings`, label: t('rfq.tabs.settings'),
      count: null, exact: false, when: holds('rfq.read'),
    },
  ]

  const context = { can }
  const offered = tabs.filter((tab) => tab.when?.(context) ?? true)

  if (offered.length === 0) return null

  return (
    <nav aria-label={t('rfq.tabs.label')} className="flex flex-wrap gap-5 border-b" style={{ borderColor: 'var(--color-border)' }}>
      {offered.map((tab) => {
        const isCurrent = tab.exact ? pathname === tab.href : pathname.startsWith(tab.href)
        return (
          <Link
            key={tab.key}
            to={tab.to}
            params={{ referenceCode }}
            activeOptions={{ exact: true }}
            aria-current={isCurrent ? 'page' : undefined}
            className="flex items-center gap-2 border-b-2 pb-3 text-[length:var(--text-body-sm)]"
            style={{
              color: isCurrent ? 'var(--color-text-brand)' : 'var(--color-text-secondary)',
              borderColor: isCurrent ? 'var(--color-brand-solid)' : 'transparent',
              fontWeight: isCurrent ? 'var(--fw-semibold)' : undefined,
              textDecoration: 'none',
              marginBottom: '-1px',
            }}
          >
            {tab.label}
            {tab.count !== null ? ' ' : null}
            {tab.count !== null ? (
              <span
                className="rounded-[var(--radius-pill)] px-2 text-[length:var(--text-caption)] tabular-nums"
                style={{ backgroundColor: 'var(--color-bg-sunken)', color: 'var(--color-text-secondary)' }}
              >
                {tab.count}
              </span>
            ) : null}
          </Link>
        )
      })}
    </nav>
  )
}
