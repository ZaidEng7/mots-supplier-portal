// ACCESSIBILITY.md's reflow clause at the narrowest supported width - first for the reports screen,
// then for the back-office shell on every route.
//
// The reports screen has four tables, and a table is the control most likely to force sideways
// scrolling. That matters because a horizontally scrolling PAGE hides content while a horizontally
// scrolling TABLE does not. Each table sits in its own scroll container; the test asserts that
// behaviour rather than trusting the class name. The selector accepts `overflow-auto` as well as
// `overflow-x-auto` because this screen used to hand-roll its own wrapper around a raw <table> and now
// uses the shared Table, which brings its own container and lets it scroll in both directions - the
// mechanism is the same and stronger, only the class naming it changed, and a selector pinned to the
// old class would have quietly matched nothing and passed.
//
// The reports check is scoped to the page's own content region, and that scoping is no longer hiding
// anything. It used to: the whole document overflowed at 320px - 424px in English, 377px in Arabic -
// because the back-office shell's header was a non-wrapping flex row, so the check was deliberately
// narrowed to avoid failing for a reason it could not fix. T-040 fixed the header, and the second
// suite below now asserts the WHOLE DOCUMENT across every back-office route, so the narrow scope here
// is about what this file is for (tables) rather than about a limitation.
//
// One thing was fixed while measuring this: the toast viewport was `w-96 max-w-full` inset 1rem from
// the end edge, and `max-w-full` ignored its own inset and pushed the page 16px sideways on every
// route. See Toast.tsx.
//
// T-040's own suite covers the shell itself. The header is shared chrome, so a fix to it changes every
// screen in that shell and a spot check on one page proves nothing. Both locales, because the two
// measured differently before the fix and Arabic is the product's primary language.
//
// Both suites guard against vacuity before measuring - a page that failed to render would trivially
// not overflow. Reports waits for its h1 and its first table, so the assertion runs against loaded
// content rather than a skeleton; the shell suite waits for the header, using .first() because several
// pages render a <header> of their own inside the shell's. A bare locator('header') is a strict-mode
// violation on those, which is how this first ran: four routes reported a failure that was the
// locator's, not the layout's.
//
// One pixel of tolerance in each comparison, for sub-pixel rounding - not for a column or a control
// that does not fit.

import { test, expect } from '@playwright/test'
import { mockBackend } from './fixtures'

for (const locale of ['ar', 'en'] as const) {
  test(`the reports tables reflow at 320px without scrolling the region [${locale}]`, async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 720 })
    await mockBackend(page)

    await page.goto(`/back-office/reports?lng=${locale}`, { waitUntil: 'networkidle' })

    await expect(page.locator('h1')).toBeVisible()
    await expect(page.locator('table').first()).toBeVisible()

    const region = page.locator('h1').locator('xpath=ancestor::div[1]')

    const overflow = await region.evaluate((el) => ({
      scrollWidth: el.scrollWidth,
      clientWidth: el.clientWidth,
    }))

    expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.clientWidth + 1)

    const scrollers = await page.locator('div.overflow-auto, div.overflow-x-auto').count()
    expect(scrollers, 'the wide content has to sit in its own scroll container, or the page scrolls instead').toBeGreaterThan(0)
  })
}

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
  '/back-office/notification-templates',
  '/back-office/reference',
  '/back-office/audit',
]

for (const locale of ['ar', 'en'] as const) {
  for (const route of backOfficeRoutes) {
    test(`the back-office shell does not scroll the document sideways at 320px: ${route} [${locale}]`, async ({ page }) => {
      await page.setViewportSize({ width: 320, height: 720 })
      await mockBackend(page)

      await page.goto(`${route}?lng=${locale}`, { waitUntil: 'networkidle' })

      await expect(page.locator('header').first()).toBeVisible()

      const overflow = await page.evaluate(() => ({
        scrollWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
      }))

      expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.clientWidth + 1)
    })
  }
}
