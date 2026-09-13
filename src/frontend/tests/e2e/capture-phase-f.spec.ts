// The record of what shipped: every route the router declares, in both languages, in both themes.
//
// Not a test. It asserts that each screen rendered before photographing it and nothing else about how
// it looks - the assertions about how these screens must behave live in the unit suites and in the
// accessibility and reflow projects. Run it on purpose:
// `CAPTURE=1 npx playwright test capture-phase-f --project=capture`.
//
// The dark half, and the finding behind it. This driver sets the theme by hand, because nothing in
// the application ever does. tokens.css defines a complete dark palette under `.theme-dark` and
// `[data-theme="dark"]`; no component, stylesheet or media query sets either, and a Storybook story
// is the only thing in the repository that ever has. So the dark theme has been defined, rewritten
// during this redesign, and guarded pair-by-pair by themeContrast, while being unreachable to every
// user of the product. Photographing it is what turns that from a sentence in a commit message into
// something a person can look at, and it is the only way to know the palette is right for the day
// somebody turns it on. themeReachability.test.ts is the instrument that stops it being forgotten
// again.
//
// It uses the attribute rather than the class, and sets it after the app has rendered rather than
// before. `prefers-color-scheme` would change nothing, because this product binds no media query to
// its dark palette; and an init script that adds the class loses it, because the app writes `dir` and
// `lang` onto the same element as it starts. The attribute selector in tokens.css is the door that
// stays open.
//
// target() makes the same parameter substitutions the accessibility sweep does, for the same reason:
// a route with a parameter renders its not-found screen without one, and a photograph of that proves
// nothing.
//
// Three guards stand before each screenshot, all against false-clean evidence. No 404 or 500, because
// a screenshot of an error boundary is indistinguishable from a working screen in a file listing -
// the exact failure this whole effort keeps finding. The expected `dir`, so an Arabic capture is
// really Arabic. And in dark mode, both the attribute and the computed surface token, because a
// screenshot of the light theme filed under dark/ would be the most convincing wrong evidence this
// driver could produce.

import { expect, test } from '@playwright/test'
import { REFERENCE_CODE, RFQ_REFERENCE_CODE, mockBackend } from './fixtures'
import { extractRoutes } from './routes'

const OUT = '../../COMP-BUILD/shots-phase-f'

test.skip(!process.env.CAPTURE, 'capture driver: run with CAPTURE=1')

const routes = extractRoutes()

function target(route: { name: string; fullPath: string }): string {
  let path = route.fullPath
  if (path === '/reset-password' || path === '/verify-email' || path === '/accept-invite') {
    path += '?token=fake-token-for-a-capture'
  }
  if (route.name === 'reviewApplicationRoute') return path.replace('$referenceCode', REFERENCE_CODE)
  return path.replace('$referenceCode', RFQ_REFERENCE_CODE)
}

const slug = (path: string) => path.replace(/\$/g, '').replace(/[/?=&]/g, '-').replace(/^-|-$/g, '') || 'root'

for (const route of routes) {
  for (const locale of ['en', 'ar'] as const) {
    for (const theme of ['light', 'dark'] as const) {
      test(`capture ${route.fullPath} [${locale}] [${theme}]`, async ({ page }) => {
        await page.setViewportSize({ width: 1440, height: 1000 })
        await mockBackend(page)

        const to = target(route)
        await page.goto(`${to}${to.includes('?') ? '&' : '?'}lng=${locale}`, { waitUntil: 'networkidle' })

        if (theme === 'dark') {
          await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'dark'))
        }

        await expect(page.getByText(/^(404|500)$/)).toHaveCount(0)
        await expect(page.locator('html')).toHaveAttribute('dir', locale === 'ar' ? 'rtl' : 'ltr')
        if (theme === 'dark') {
          await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
          const surface = await page.evaluate(() =>
            getComputedStyle(document.documentElement).getPropertyValue('--color-bg-surface').trim())
          expect(surface).toBe('#1C2421')
        }

        await page.screenshot({ path: `${OUT}/${theme}/${locale}/${slug(route.fullPath)}.png`, fullPage: true })
      })
    }
  }
}
