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
 * <p><b>Scoped to the page's own content region - and that scoping is no longer hiding anything.</b>
 * It used to: the whole document overflowed at 320px (424px in English, 377px in Arabic) because the
 * back-office shell's header was a non-wrapping flex row, so this check was deliberately narrowed to
 * the content region to avoid failing for a reason it could not fix. T-040 fixed the header, and the
 * suite below now asserts the WHOLE DOCUMENT across every back-office route - so the narrow scope
 * here is about what this file is for (tables) rather than about a limitation.</p>
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

/**
 * T-040: the shell itself, at the narrowest supported width, on every back-office route.
 *
 * <p>The header is shared chrome, so a fix to it is a change to every screen in that shell - which
 * means a spot check on one page proves nothing. This asserts the whole DOCUMENT does not scroll
 * sideways, which is the assertion the reports check above could not make while the header was
 * broken.</p>
 *
 * <p>Both locales, because the two measured differently before the fix (424px vs 377px) and Arabic
 * is the product's primary language.</p>
 */
const backOfficeRoutes = [
  '/back-office/dashboard',
  '/back-office/review',
  '/back-office/rfqs',
  '/back-office/procurement',
  '/back-office/procurement/approvals',
  '/back-office/review-dashboard',
  '/back-office/notifications',
  '/back-office/reports',
  '/back-office/ministry',
  '/back-office/admin',
  '/back-office/settings',
]

for (const locale of ['ar', 'en'] as const) {
  for (const route of backOfficeRoutes) {
    test(`the back-office shell does not scroll the document sideways at 320px: ${route} [${locale}]`, async ({ page }) => {
      await page.setViewportSize({ width: 320, height: 720 })
      await mockBackend(page)

      await page.goto(`${route}?lng=${locale}`, { waitUntil: 'networkidle' })

      // Non-vacuity: the SHELL's header must have rendered, or a blank page would trivially not
      // overflow and every route here would "pass".
      //
      // .first(), because several pages render a <header> of their own inside the shell's - a bare
      // locator('header') is a strict-mode violation on those, which is how this first ran: four
      // routes reported a failure that was the locator's, not the layout's.
      await expect(page.locator('header').first()).toBeVisible()

      const overflow = await page.evaluate(() => ({
        scrollWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
      }))

      // One pixel for sub-pixel rounding, not for a control that does not fit.
      expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.clientWidth + 1)
    })
  }
}
