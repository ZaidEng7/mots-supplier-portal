// Captures the LIVE application against the real database, not the mocked fixtures.
//
// Every other capture driver in this suite intercepts `/api/v1/**` and answers from fixtures.ts,
// which is right for an accessibility scan: it makes each route render deterministically without a
// backend. It is wrong for an audit of how the product reads, because those fixtures return one row
// where the seeded database now holds thirty-one suppliers and twenty-five tenders, and a list with
// one row cannot show whether a list works. So this one signs in for real and photographs what a
// person would actually see. It needs the API and the database up, and it is skipped unless asked
// for: `CAPTURE=1 npx playwright test capture-live --project=capture`.
//
// OFFICER_SCREENS is the buyer's side, as a Ministry procurement officer works it all day.
// SUPPLIER_SCREENS is the supplier's side, used a few times a year under deadline. The last test
// opens one tender in full - workspace, suppliers, bids, comparison, award, settings - which is where
// the officer spends that day; the tender code is read off the first link in the list rather than
// hard-coded, so it follows whatever the database holds.
//
// signIn waits for the redirect off /login, because that is the proof the session took. A screenshot
// of the login form filed under the officer's dashboard would be exactly the wrong evidence.

import { expect, test } from '@playwright/test'

const OUT = '../../DESIGN-IS-2026-09-10/shots'

test.skip(!process.env.CAPTURE, 'capture driver: run with CAPTURE=1')

const PASSWORD = 'motsdemo2026'

const OFFICER_SCREENS = [
  ['/back-office/dashboard', 'officer-dashboard'],
  ['/back-office/rfqs', 'officer-tender-list'],
  ['/back-office/procurement', 'officer-procurement-dashboard'],
  ['/back-office/review', 'officer-review-queue'],
  ['/back-office/review-dashboard', 'officer-review-dashboard'],
  ['/back-office/suppliers', 'officer-supplier-directory'],
  ['/back-office/reports', 'officer-reports'],
  ['/back-office/ministry', 'officer-ministry-overview'],
  ['/back-office/ministry/awards', 'officer-ministry-awards'],
  ['/back-office/offerings', 'officer-offering-search'],
  ['/back-office/search', 'officer-search'],
  ['/back-office/notifications', 'officer-notifications'],
] as const

const SUPPLIER_SCREENS = [
  ['/dashboard', 'supplier-dashboard'],
  ['/rfqs', 'supplier-tenders'],
  ['/proposals', 'supplier-proposals'],
  ['/onboarding', 'supplier-onboarding'],
  ['/profile', 'supplier-profile'],
  ['/documents', 'supplier-documents'],
  ['/offerings', 'supplier-offerings'],
  ['/team', 'supplier-team'],
  ['/settings', 'supplier-settings'],
] as const

async function signIn(page: import('@playwright/test').Page, email: string) {
  await page.goto('/login?lng=en')
  await page.getByLabel(/email/i).fill(email)
  await page.getByLabel(/password/i).fill(PASSWORD)
  await page.getByRole('button', { name: /sign in|log in/i }).click()
  await expect(page).not.toHaveURL(/\/login/, { timeout: 20_000 })
}

for (const [persona, email, screens] of [
  ['officer', 'officer@mots.local', OFFICER_SCREENS],
  ['supplier', 'supplier@mots.local', SUPPLIER_SCREENS],
] as const) {
  test(`capture ${persona} screens against the real database`, async ({ page }) => {
    test.setTimeout(180_000)
    await page.setViewportSize({ width: 1440, height: 1000 })
    await signIn(page, email)

    for (const [route, name] of screens) {
      await page.goto(`${route}?lng=en`, { waitUntil: 'networkidle' })
      await expect(page.getByText(/^(404|500)$/)).toHaveCount(0)
      await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: true })
    }
  })
}

test('capture a tender workspace against the real database', async ({ page }) => {
  test.setTimeout(180_000)
  await page.setViewportSize({ width: 1440, height: 1000 })
  await signIn(page, 'officer@mots.local')

  await page.goto('/back-office/rfqs?lng=en', { waitUntil: 'networkidle' })
  const firstTender = page.getByRole('link', { name: /RFQ-DEMO-/ }).first()
  await expect(firstTender).toBeVisible()
  const code = ((await firstTender.textContent()) ?? '').trim()

  for (const [suffix, name] of [
    ['', 'workspace-tender'],
    ['/suppliers', 'workspace-suppliers'],
    ['/proposals', 'workspace-bids'],
    ['/comparison', 'workspace-comparison'],
    ['/award', 'workspace-award'],
    ['/settings', 'workspace-settings'],
  ] as const) {
    await page.goto(`/back-office/rfqs/${code}${suffix}?lng=en`, { waitUntil: 'networkidle' })
    await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: true })
  }
})
