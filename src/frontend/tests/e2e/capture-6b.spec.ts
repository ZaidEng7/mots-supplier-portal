// Not a test - a capture driver. It lives beside the suite because it needs the same mocked backend
// the a11y scans use, and because a redesign whose evidence is a description rather than a picture
// is a description.
//
// Run it on purpose: `npx playwright test capture-6b --project=app-a11y`. It is skipped unless
// CAPTURE is set, so CI pays only the cost of skipping it: it writes files and proves nothing a
// suite should re-prove on every push. It asserts nothing about layout and cannot fail the build on
// it; the assertions about these screens live in their own unit tests and in the a11y and reflow
// projects.
//
// Two things it does assert, both about the capture itself rather than the design. The buyer tender
// workspace waits for the level-1 heading instead of `networkidle` - waiting for the network to go
// quiet is a proxy for readiness rather than readiness itself, and it never settles on a page that
// polls, whereas the heading being visible IS the screen having rendered. And every capture makes
// that check before recording, because a screenshot of a blank page is worthless as evidence and
// looks identical to a screenshot of a working one in a file listing, which is the failure mode this
// batch was closing.
//
// Three groups: the tender workspace at desktop and narrow widths in both locales; the dashboard,
// RFQ list and documents screens after the system pass, so the heading change has evidence rather
// than a claim; then three single screens - ministry award analytics, supplier onboarding, and the
// grouped supplier navigation.

import { expect, test } from '@playwright/test'
import { RFQ_REFERENCE_CODE, mockBackend } from './fixtures'

const OUT = '../../DESIGN-IS-2026-09-09/shots-after'

test.skip(!process.env.CAPTURE, 'capture driver: run with CAPTURE=1')

for (const locale of ['en', 'ar'] as const) {
  for (const [name, width, height] of [['desktop', 1280, 1400], ['narrow', 320, 1800]] as const) {
    test(`capture buyer tender workspace ${name} ${locale}`, async ({ page }) => {
      await page.setViewportSize({ width, height })
      await mockBackend(page)
      await page.goto(`/back-office/rfqs/${RFQ_REFERENCE_CODE}?lng=${locale}`)
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      await page.screenshot({ path: `${OUT}/rfq-detail-${name}-${locale}.png`, fullPage: true })
    })
  }
}

for (const [route, name] of [
  ['/back-office/dashboard', 'dashboard'],
  ['/back-office/rfqs', 'rfq-list'],
  ['/documents', 'documents'],
] as const) {
  for (const locale of ['en', 'ar'] as const) {
    test(`capture ${name} ${locale}`, async ({ page }) => {
      await page.setViewportSize({ width: 1280, height: 900 })
      await mockBackend(page)
      await page.goto(`${route}?lng=${locale}`)
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      await page.screenshot({ path: `${OUT}/${name}-${locale}.png`, fullPage: true })
    })
  }
}

test('capture ministry award analytics en', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 1000 })
  await mockBackend(page)
  await page.goto('/back-office/ministry/awards?lng=en', { waitUntil: 'networkidle' })
  await page.screenshot({ path: `${OUT}/ministry-awards-en.png`, fullPage: true })
})

test('capture supplier onboarding en', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 1200 })
  await mockBackend(page)
  await page.goto('/onboarding?lng=en', { waitUntil: 'networkidle' })
  await page.screenshot({ path: `${OUT}/onboarding-en.png`, fullPage: true })
})

test('capture the grouped supplier navigation', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 400 })
  await mockBackend(page)
  await page.goto('/rfqs?lng=en')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await page.screenshot({ path: `${OUT}/supplier-nav-en.png` })
})
