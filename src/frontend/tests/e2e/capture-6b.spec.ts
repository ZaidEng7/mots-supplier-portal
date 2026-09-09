import { expect, test } from '@playwright/test'
import { RFQ_REFERENCE_CODE, mockBackend } from './fixtures'

/**
 * Not a test — a capture driver, kept beside the suite because it needs the same mocked backend the
 * a11y scans use, and because a redesign whose evidence is a description rather than a picture is a
 * description.
 *
 * <p>Run it on purpose: `npx playwright test capture-6b --project=app-a11y`. It asserts nothing and
 * cannot fail the build on layout; the assertions about this screen live in its own unit tests and in
 * the a11y and reflow projects.</p>
 */
const OUT = '../../DESIGN-IS-2026-09-09/shots-after'

// Skipped unless asked for. It writes files and proves nothing a suite should re-prove on every push,
// so CI pays only the cost of skipping it.
test.skip(!process.env.CAPTURE, 'capture driver: run with CAPTURE=1')

for (const locale of ['en', 'ar'] as const) {
  for (const [name, width, height] of [['desktop', 1280, 1400], ['narrow', 320, 1800]] as const) {
    test(`capture buyer tender workspace ${name} ${locale}`, async ({ page }) => {
      await page.setViewportSize({ width, height })
      await mockBackend(page)
      // No `networkidle`: it waits for the network to go quiet, which is a proxy for readiness rather
      // than readiness itself, and it never settles on a page that polls. The heading assertion below is
      // the real condition - the screen has rendered - so waiting for it is both the readiness check and
      // the proof the capture is worth taking.
      await page.goto(`/back-office/rfqs/${RFQ_REFERENCE_CODE}?lng=${locale}`)
      // Not ceremony to satisfy a rule. A screenshot of a blank page is worthless as evidence and looks
      // identical to a screenshot of a working one in a file listing - which is the failure mode this
      // whole batch has been closing. Every capture proves the screen rendered before recording it.
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      await page.screenshot({ path: `${OUT}/rfq-detail-${name}-${locale}.png`, fullPage: true })
    })
  }
}

// Phase 2: the same screens after the system pass, so the heading change has evidence rather than a claim.
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
