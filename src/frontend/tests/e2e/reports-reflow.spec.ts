import { test, expect } from '@playwright/test'
import { mockBackend } from './fixtures'

/**
 * ACCESSIBILITY.md's reflow clause at the narrowest supported width, for the reports screen.
 *
 * <p>Four tables, and a table is the control most likely to force sideways scrolling - which
 * matters because a horizontally scrolling PAGE hides content that a horizontally scrolling TABLE
 * does not. Each table sits in its own `overflow-x-auto` container; this asserts the behaviour
 * rather than trusting the class name.</p>
 *
 * <p><b>Scoped to the page's own content region, and the reason is a finding, not a convenience.</b>
 * The whole document DOES overflow at 320px - 424px against a 320px viewport in English, 377px in
 * Arabic - and the cause is the back-office shell's header, a `flex items-center gap-3` row of
 * controls with no wrapping. That is pre-existing, affects every back-office route, and has nothing
 * to do with this screen; fixing the shared chrome is a change with consequences across many
 * screens and is reported rather than made here. Asserting on the whole document would mean this
 * check failed for a reason it cannot fix, and a check that is expected to fail gets ignored.</p>
 *
 * <p>One thing WAS fixed while measuring this: the toast viewport was `w-96 max-w-full` inset 1rem
 * from the end edge, so `max-w-full` ignored its own inset and pushed the page 16px sideways on
 * every route. See Toast.tsx.</p>
 */
for (const locale of ['ar', 'en'] as const) {
  test(`the reports tables reflow at 320px without scrolling the region [${locale}]`, async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 720 })
    await mockBackend(page)

    await page.goto(`/back-office/reports?lng=${locale}`, { waitUntil: 'networkidle' })

    // Non-vacuity: a page that failed to render would trivially not overflow. Waiting on a table
    // also means the assertion runs against loaded content, not a skeleton.
    await expect(page.locator('h1')).toBeVisible()
    await expect(page.locator('table').first()).toBeVisible()

    const region = page.locator('h1').locator('xpath=ancestor::div[1]')

    const overflow = await region.evaluate((el) => ({
      scrollWidth: el.scrollWidth,
      clientWidth: el.clientWidth,
    }))

    // One pixel of tolerance for sub-pixel rounding, not for a column that does not fit.
    expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.clientWidth + 1)

    // And the tables themselves are the things that scroll, which is the mechanism that makes the
    // above true rather than an accident of narrow content.
    const scrollers = await page.locator('div.overflow-x-auto').count()
    expect(scrollers).toBeGreaterThan(0)
  })
}
