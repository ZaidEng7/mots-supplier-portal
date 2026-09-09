import { test } from '@playwright/test'
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
      await page.goto(`/back-office/rfqs/${RFQ_REFERENCE_CODE}?lng=${locale}`, { waitUntil: 'networkidle' })
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
      await page.goto(`${route}?lng=${locale}`, { waitUntil: 'networkidle' })
      await page.screenshot({ path: `${OUT}/${name}-${locale}.png`, fullPage: true })
    })
  }
}
