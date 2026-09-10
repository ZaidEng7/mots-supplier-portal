import { expect, test } from '@playwright/test'
import { REFERENCE_CODE, RFQ_REFERENCE_CODE, mockBackend } from './fixtures'
import { extractRoutes } from './routes'

/**
 * The record of what shipped: every route the router declares, in both languages, in both themes.
 *
 * <p>Not a test. It asserts that each screen rendered before photographing it and nothing else about
 * how it looks - the assertions about how these screens must behave live in the unit suites and in the
 * accessibility and reflow projects. Run it on purpose:
 * `CAPTURE=1 npx playwright test capture-phase-f --project=capture`.</p>
 *
 * <p><b>The dark half, and the finding behind it.</b> This driver applies `.theme-dark` by hand,
 * because nothing in the application ever applies it. `tokens.css` defines a complete dark palette
 * under `.theme-dark` and `[data-theme="dark"]`; no component, stylesheet or media query sets either,
 * and a Storybook story is the only thing in the repository that ever has. So the dark theme has been
 * defined, rewritten during this redesign, and guarded pair-by-pair by `themeContrast`, while being
 * unreachable to every user of the product.</p>
 *
 * <p>Photographing it is what turns that from a sentence in a commit message into something a person
 * can look at, and it is the only way to know the palette is right for the day somebody turns it on.
 * `themeReachability.test.ts` is the instrument that stops it being forgotten again.</p>
 */
const OUT = '../../COMP-BUILD/shots-phase-f'

test.skip(!process.env.CAPTURE, 'capture driver: run with CAPTURE=1')

const routes = extractRoutes()

/** The same substitutions the accessibility sweep makes, for the same reason: a route with a parameter
 *  renders its not-found screen without one, and a photograph of that proves nothing. */
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
          // The attribute rather than the class, and after the app has rendered rather than before it.
          // `prefers-color-scheme` would change nothing, because this product binds no media query to
          // its dark palette; and an init script that adds the class loses it, because the app writes
          // `dir` and `lang` onto this same element as it starts. The attribute selector in tokens.css
          // is the door that stays open.
          await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'dark'))
        }

        // The same false-clean guards the accessibility sweep uses. A screenshot of an error boundary
        // is indistinguishable from a screenshot of a working screen in a file listing, which is the
        // exact failure this whole effort keeps finding.
        await expect(page.getByText(/^(404|500)$/)).toHaveCount(0)
        await expect(page.locator('html')).toHaveAttribute('dir', locale === 'ar' ? 'rtl' : 'ltr')
        if (theme === 'dark') {
          await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
          // And the palette really took: a screenshot of the light theme filed under `dark/` would be
          // the most convincing wrong evidence this driver could produce.
          const surface = await page.evaluate(() =>
            getComputedStyle(document.documentElement).getPropertyValue('--color-bg-surface').trim())
          expect(surface).toBe('#1C2421')
        }

        await page.screenshot({ path: `${OUT}/${theme}/${locale}/${slug(route.fullPath)}.png`, fullPage: true })
      })
    }
  }
}
