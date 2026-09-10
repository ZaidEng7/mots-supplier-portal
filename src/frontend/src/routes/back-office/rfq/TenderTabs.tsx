import { Link, useRouterState } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'

/**
 * The buyer's six views of one tender, as a strip rather than as a page.
 *
 * <p><b>What this replaces.</b> The workspace stacked every section of the tender down a single
 * column - the tender itself, its suppliers, its decisions and every management form - and reached the
 * bids, the comparison and the award through buttons buried in a card two thirds of the way down. A
 * reader had no way to tell what a tender contained without scrolling it, and no way back except the
 * browser's own button.</p>
 *
 * <p><b>Links, not a tab widget.</b> Each of these is a real route, so a bid list can be linked to,
 * bookmarked and reached with the back button, and only the view being looked at is fetched. `role=
 * "tablist"` would promise arrow-key movement between panels that are all on one page, which is not
 * what this is.</p>
 *
 * <p>The counts are the point of putting them here: "Suppliers" and "Suppliers 7" ask a reader for
 * different amounts of work, and the second answers a question they would otherwise open the tab to
 * ask.</p>
 */
export function TenderTabs({ referenceCode, invitedCount, bidCount }: Readonly<{
  referenceCode: string
  invitedCount: number
  bidCount: number
}>) {
  const { t } = useTranslation()
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const base = `/back-office/rfqs/${referenceCode}`

  const tabs = [
    { key: 'tender', to: '/back-office/rfqs/$referenceCode', href: base, label: t('rfq.tabs.tender'), count: null, exact: true },
    { key: 'suppliers', to: '/back-office/rfqs/$referenceCode/suppliers', href: `${base}/suppliers`, label: t('rfq.tabs.suppliers'), count: invitedCount, exact: false },
    { key: 'bids', to: '/back-office/rfqs/$referenceCode/proposals', href: `${base}/proposals`, label: t('rfq.tabs.bids'), count: bidCount, exact: false },
    { key: 'evaluation', to: '/back-office/rfqs/$referenceCode/comparison', href: `${base}/comparison`, label: t('rfq.tabs.evaluation'), count: null, exact: false },
    { key: 'award', to: '/back-office/rfqs/$referenceCode/award', href: `${base}/award`, label: t('rfq.tabs.award'), count: null, exact: false },
    { key: 'settings', to: '/back-office/rfqs/$referenceCode/settings', href: `${base}/settings`, label: t('rfq.tabs.settings'), count: null, exact: false },
  ]

  return (
    <nav aria-label={t('rfq.tabs.label')} className="flex flex-wrap gap-5 border-b" style={{ borderColor: 'var(--color-border)' }}>
      {tabs.map((tab) => {
        // Exact for the tender itself, because every other tab's path starts with it - a prefix match
        // would mark "Tender" current from inside Settings, which is the same defect the supplier
        // navigation had against its dashboard link.
        const isCurrent = tab.exact ? pathname === tab.href : pathname.startsWith(tab.href)
        return (
          <Link
            key={tab.key}
            to={tab.to}
            params={{ referenceCode }}
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
            {/* The separator is markup, not styling. Without it the accessible name computes as
                "Suppliers7", because gap is layout and a screen reader concatenates the text nodes. */}
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
