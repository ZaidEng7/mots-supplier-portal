import { test, expect } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
import { REFERENCE_CODE, mockBackend } from './fixtures'

const dirname = path.dirname(fileURLToPath(import.meta.url))

/**
 * NFR-A11Y-001/002/003/007: axe against every real application route, not Storybook components.
 *
 * `tests/e2e/storybook-axe.spec.ts` scans 8 isolated components. It has never scanned a page a
 * supplier or reviewer actually uses - 0 of 18 application routes, confirmed by the Phase 4
 * denominator sweep. This file is what closes that gap, and it exists specifically because that
 * gap was found: a gate that scans nothing real is the shape this whole hardening arc keeps
 * finding, and this suite was written to not become the eighth instance of it.
 */

// ---- THE DENOMINATOR, derived from the router source, not hand-typed ----------------------
//
// Route coverage must fail when it silently shrinks - the same failure class as `tsc --noEmit`
// checking zero files, or the axe suite that inspired this one covering 8 components while
// believing it covered the app. So this list is parsed out of src/router.tsx at test-collection
// time rather than copied by hand into this file: a route added to the router without ever
// reaching this suite is a route this parser will find and a page this suite will then scan,
// automatically. The alternative - a hand-maintained array - is exactly the kind of denominator
// that drifts quietly, which is what this ticket exists to stop.
interface RouteEntry {
  name: string
  fullPath: string
  requiresSupplierAuth: boolean
  requiresReviewerAuth: boolean
}

function extractRoutes(): RouteEntry[] {
  const source = readFileSync(path.join(dirname, '../../src/router.tsx'), 'utf-8')
  const blocks = [...source.matchAll(/const (\w+Route) = createRoute\(\{([\s\S]*?)\n\}\)/g)]

  type Raw = { name: string; body: string; path: string | null; id: string | null; parent: string | null }
  const raw: Raw[] = blocks.map(([, name, body]) => {
    const pathMatch = body.match(/path:\s*'([^']*)'/)
    const idMatch = body.match(/id:\s*'([^']*)'/)
    const parentMatch = body.match(/getParentRoute:\s*\(\)\s*=>\s*(\w+)/)
    return {
      name,
      body,
      path: pathMatch ? pathMatch[1] : null,
      id: idMatch ? idMatch[1] : null,
      parent: parentMatch ? parentMatch[1] : null,
    }
  })

  const byName = new Map(raw.map((r) => [r.name, r]))

  function resolveFullPath(r: Raw): string {
    const own = r.path ?? ''
    if (r.parent === 'rootRoute' || !r.parent) return own
    const parent = byName.get(r.parent)
    if (!parent) return own
    const parentPath = resolveFullPath(parent)
    if (!own) return parentPath
    // Both segments present and non-root: join without doubling the slash.
    return `${parentPath === '/' ? '' : parentPath}${own}`
  }

  // Shell/layout routes render only `<Shell><Outlet /></Shell>` - chrome around a child route,
  // never independent content of their own. Excluded from the page count on that basis, not on
  // whether they happen to declare a `path:` - backOfficeLayoutRoute does, and is still a shell.
  const isShellWrapper = (body: string) => /<\w*Shell>[\s\S]*<Outlet \/>/.test(body)

  return raw
    .filter((r) => !isShellWrapper(r.body))
    .map((r) => {
      const fullPath = resolveFullPath(r)
      const requiresSupplierAuth = r.parent === 'supplierLayoutRoute'
      const requiresReviewerAuth = r.parent === 'backOfficeLayoutRoute'
      return { name: r.name, fullPath, requiresSupplierAuth, requiresReviewerAuth }
    })
}

const routes = extractRoutes()

test('the route denominator is what the router actually declares, not what this file assumes', () => {
  // The assertion this whole suite exists to carry. A router change that silently drops a route
  // out of this count must fail here, loudly, rather than the suite quietly scanning fewer pages
  // than it did yesterday and staying green throughout.
  //
  // 18, matching the ticket's stated number - but arrived at here, not assumed: the extraction
  // logic's own first run asserted '/review' where the router actually composes
  // '/back-office/review' (reviewQueueRoute's parent is the /back-office layout), and this
  // assertion caught that before it ever reached a scan. Left as the record of why this suite
  // trusts the parser's output over a hand-typed path list, including its own.
  //
  // 19 (Task #7/Stage C): /back-office/organizations added.
  // 21 (Task #28): /accept-staff-invite and /back-office/staff added.
  expect(routes.length).toBe(21)
  expect(routes.map((r) => r.fullPath)).toEqual(
    expect.arrayContaining(['/login', '/dashboard', '/back-office/review']),
  )
})

// ---- The scan itself, both directions, both locales ---------------------------------------

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

      await page.goto(`${target}${target.includes('?') ? '&' : '?'}lng=${locale}`, { waitUntil: 'networkidle' })

      // Guards against a false "clean" scan: if the page fell through to the error boundary
      // instead of its real content (a mock gap, a route mismatch), the DOM axe would inspect is
      // the 404/500 screen, not the page under test - a pass there proves nothing about NFR-A11Y.
      await expect(page.getByText(/^(404|500)$/)).toHaveCount(0);

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag22aa'])
        .analyze()

      expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([])
    })
  }
}
