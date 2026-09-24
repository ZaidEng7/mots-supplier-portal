// NFR-A11Y-001/002/003/007: axe against every real application route, not Storybook components.
//
// storybook-axe.spec.ts scans 8 isolated components. It had never scanned a page a supplier or
// reviewer actually uses - 0 of 18 application routes, confirmed by the Phase 4 denominator sweep.
// This file is what closes that gap, and it exists specifically because the gap was found: a gate
// that scans nothing real is the shape this whole hardening arc keeps finding, and this suite was
// written so as not to become the eighth instance of it.
//
// THE DENOMINATOR, derived from the router source rather than hand-typed. Route coverage must fail
// when it silently shrinks - the same failure class as `tsc --noEmit` checking zero files, or the axe
// suite that inspired this one covering 8 components while believing it covered the app. So the list
// is parsed out of src/router.tsx at collection time: a route added to the router without ever
// reaching this suite is a route the parser finds and this suite then scans, automatically. A
// hand-maintained array is exactly the denominator that drifts quietly, which is what the ticket
// exists to stop.
//
// The first test asserts that count, loudly, so a router change that drops a route cannot leave the
// suite quietly scanning fewer pages than yesterday and staying green. The number was arrived at
// rather than assumed - the extraction logic's own first run asserted '/review' where the router
// composes '/back-office/review' (reviewQueueRoute's parent is the /back-office layout), and this
// assertion caught that before it reached a scan. That is the record of why this suite trusts the
// parser's output over a hand-typed path list, including its own.
//
// Every move of the count, and each one fired this guard first:
//   18  the ticket's stated number
//   19  T-7/Stage C: /back-office/organizations
//   21  T-28: /accept-staff-invite, /back-office/staff
//   23  Epics 1/6 closure: /offerings, /back-office/roles
//   24  Epics 3/6 closure: /back-office/offerings
//   27  FEAT-11.1/EPIC-07: /back-office/evaluation-templates, /back-office/rfqs,
//       /back-office/rfqs/$referenceCode
//   29  EPIC-08: /rfqs, /rfqs/$referenceCode - supplier-facing invitations
//   30  EPIC-09: /rfqs/$referenceCode/proposal
//   31  EPIC-11: /back-office/rfqs/$referenceCode/my-evaluation
//   32  EPIC-12: /back-office/rfqs/$referenceCode/comparison
//   33  EPIC-14: /back-office/rfqs/$referenceCode/award
//   35  EPIC-15/SCR-900: /notifications, /back-office/notifications - one screen reached through each
//       persona's own shell, because the two shells are two different URL spaces
//   36  EPIC-17/SCR-500: /evaluation, the evaluator dashboard. Back-office chrome on a root path, per
//       SCREEN-INVENTORY's route column and IA §4.3's shell
//   39  EPIC-17 part 2: /back-office/procurement, /back-office/procurement/approvals,
//       /back-office/review-dashboard - SCR-400, SCR-401, SCR-300
//   40  EPIC-19: /back-office/reports - FEAT-19.1/19.2
//   41  EPIC-18/SCR-600: /back-office/ministry, the governance dashboard. A persona that could
//       previously reach nothing gained a route, and the scan had to be told rather than quietly
//       continuing to cover 40 of 41 pages
//   42  T-062/SCR-700: /back-office/admin - system_admin had no landing page; the third consecutive
//       move for a persona gaining somewhere to go
//   43  T-060/SCR-724: /back-office/settings - FR-ADM-006's system settings
//   44  T-061/SCR-715: /back-office/notification-templates - FR-ADM-007's editable copy
//   45  T-080/SCR-710-712: /back-office/reference - FR-ADM-004's reference-data editor. Three
//       inventory rows, one route: the operations are identical across the five tables, so the count
//       moves by one rather than three
//   46  T-079/SCR-720: /back-office/audit - FR-AUD-004's audit explorer. Three audit endpoints
//       existed and no screen called any of them; the supplier-facing two were closed on the
//       supplier's own settings screen, and this is the third
//   58  batch 11: twelve routes at once, the largest single move - the batch closed the screen backlog
//       rather than adding a feature. /rfqs/$referenceCode/proposals (SCR-430/431, the buyer's view of
//       the bids on one RFQ), /profile (SCR-120), /documents (SCR-130), /proposals (SCR-150),
//       /back-office/account (SCR-902), /about (SCR-908, public), /help and /back-office/help
//       (SCR-907, one screen in each shell), /back-office/operations (SCR-721/722/723/725/726, the
//       operator's five panels), /back-office/ui-strings (SCR-716), /back-office/search (SCR-906),
//       /back-office/email-templates (T-076). Without the guard the scan would have kept covering 46
//       of 58 pages and stayed green while twelve screens went unexamined
//   64  phase 3: six routes for five screens - /suppliers (SCR-402), /review/suppliers (SCR-307),
//       /rfqs/$referenceCode/brief (SCR-501, what an evaluator scores against), /ministry/categories
//       (SCR-604), /settings/notifications and /account/notifications (SCR-901, one screen in each
//       shell, as SCR-907 and SCR-900 are)
//   68  phase 4, D-66: the four Ministry oversight screens - /ministry/rfqs (SCR-602),
//       /ministry/rfqs/$referenceCode (SCR-606, each named bidder and what it bid),
//       /ministry/suppliers (SCR-601), /ministry/awards (SCR-603). The widest disclosure in the
//       product, so also the set most worth scanning in Arabic as well as English
//   70  the comp gives the tender workspace a tab strip, and two of its six tabs are new routes -
//       Suppliers and Settings - carrying sections the workspace used to stack down one column
//
// The scan itself runs every route in both locales, with two false-clean guards before axe sees the
// page. First, no 404 or 500: a page that fell through to the error boundary (a mock gap, a route
// mismatch) hands axe the error screen, and a pass there proves nothing about NFR-A11Y. Second - the
// guard this suite needed most - the page is really in the locale it claims. `?lng=ar` was silently
// overwritten on every authenticated route by the account language the fixture returned, so both of
// each route's two scans ran against the English LTR interface, roughly 60 of 68 routes, while this
// file reported 137 scans "in both languages". A design audit found that; nothing here could have.

import { test, expect } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { REFERENCE_CODE, RFQ_REFERENCE_CODE, mockBackend } from './fixtures'
import { extractRoutes } from './routes'


const routes = extractRoutes()

test('the route denominator is what the router actually declares, not what this file assumes', () => {
  expect(routes.length).toBe(71)
  expect(routes.map((r) => r.fullPath)).toEqual(
    expect.arrayContaining(['/login', '/dashboard', '/back-office/review']),
  )
})


for (const route of routes) {
  for (const locale of ['ar', 'en'] as const) {
    test(`a11y: ${route.fullPath} [${locale}]`, async ({ page }) => {
      await mockBackend(page)

      let target = route.fullPath
      if (target === '/reset-password' || target === '/verify-email' || target === '/accept-invite') {
        target += '?token=fake-token-for-a11y-scan'
      }
      if (route.name === 'reviewApplicationRoute') {
        target = target.replace('$referenceCode', REFERENCE_CODE)
      }
      if (route.name === 'ministryRfqDetailRoute') {
        target = target.replace('$referenceCode', RFQ_REFERENCE_CODE)
      }
      if (route.name === 'rfqDetailRoute' || route.name === 'supplierRfqDetailRoute' || route.name === 'supplierProposalRoute' || route.name === 'myEvaluationRoute' || route.name === 'myEvaluationBriefRoute' || route.name === 'comparisonRoute' || route.name === 'awardRoute') {
        target = target.replace('$referenceCode', RFQ_REFERENCE_CODE)
      }

      await page.goto(`${target}${target.includes('?') ? '&' : '?'}lng=${locale}`, { waitUntil: 'networkidle' })

      await expect(page.getByText(/^(404|500)$/)).toHaveCount(0);

      await expect(page.locator('html')).toHaveAttribute('dir', locale === 'ar' ? 'rtl' : 'ltr')
      await expect(page.locator('html')).toHaveAttribute('lang', locale)

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag22aa'])
        .analyze()

      expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([])
    })
  }
}
