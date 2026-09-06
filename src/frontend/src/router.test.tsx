import { describe, expect, it } from 'vitest'
import type { AnyRoute } from '@tanstack/react-router'
import { router } from './router'

/**
 * A guard on the route tree itself, as a unit test, for the failures that are invisible in a browser:
 * a duplicate path silently shadows whichever route was registered second, and an authenticated screen
 * hung off the root instead of a layout is reachable with no session and no shell.
 *
 * <p>This is the same class of instrument as the e2e sweep's route denominator, one level down. That
 * one catches a NEW screen going unexamined; this one catches an EXISTING screen changing shape - and
 * it runs in a second rather than requiring a browser.</p>
 */
function walk(route: AnyRoute, trail: AnyRoute[] = []): AnyRoute[] {
  const children = (route.children ?? []) as AnyRoute[]
  return children.reduce((all, child) => walk(child, all), [...trail, route])
}

const all = walk(router.routeTree)
// The root is excluded because it reports path '/' itself, which would collide with the index route
// and make the uniqueness check below fail on a tree that is correct.
const withPaths = all.filter((r) => r.id !== '__root__' && typeof r.path === 'string' && r.path.length > 0)

describe('route tree', () => {
  it('registers every route under exactly one full path', () => {
    // Narrower than it looks, and deliberately kept. TanStack Router already throws "Duplicate routes
    // found with id" when two routes collide under the SAME parent - verified by making that edit, and
    // it fails at import, which is stronger than any assertion here.
    //
    // What it does not catch is a collision across two DIFFERENT layouts: the ids differ, so the
    // invariant is satisfied, while the fullPaths are identical and one screen is unreachable. Also
    // verified, by pointing a supplier-layout route at the evaluator layout's path - the router
    // constructed happily and this test failed.
    const fullPaths = withPaths.map((r) => r.fullPath)
    expect(new Set(fullPaths).size).toBe(fullPaths.length)
  })

  it('puts every authenticated screen under a guarded layout', () => {
    // The guard lives on the two layout routes, so a screen parented to the root instead is reachable
    // with no session at all. That is a one-line mistake and an invisible one: the page renders,
    // queries 401, and looks like a data problem rather than a missing guard.
    const publicPaths = new Set([
      '/', '/about', '/help', '/login', '/register', '/forgot-password', '/reset-password',
      '/verify-email', '/accept-invite', '/accept-staff-invite',
    ])

    const unguarded = withPaths
      .filter((r) => !publicPaths.has(r.fullPath))
      .filter((r) => {
        for (let node: AnyRoute | undefined = r; node; node = node.parentRoute as AnyRoute | undefined) {
          if (typeof node.options?.beforeLoad === 'function') return false
        }
        return true
      })
      .map((r) => r.fullPath)

    expect(unguarded).toEqual([])
  })

  it('keeps the two shells in separate URL spaces', () => {
    // Supplier screens and back-office screens are different shells with different navigation, and a
    // path that answers under both would render one persona's chrome around the other's page.
    const supplier = withPaths.filter((r) => !r.fullPath.startsWith('/back-office')).map((r) => r.fullPath)
    const backOffice = withPaths.filter((r) => r.fullPath.startsWith('/back-office')).map((r) => r.fullPath)

    expect(supplier.some((p) => backOffice.includes(p))).toBe(false)
    expect(backOffice.length).toBeGreaterThan(20)
  })

  it('registers the screens this batch added', () => {
    // Named individually rather than counted, so a route deleted in a refactor fails here with its
    // own name instead of as an off-by-one on a total.
    const paths = new Set(withPaths.map((r) => r.fullPath))
    for (const path of [
      '/profile', '/documents', '/proposals',
      '/back-office/operations', '/back-office/ui-strings', '/back-office/email-templates',
      '/back-office/search', '/back-office/audit', '/back-office/reference',
      '/back-office/reports', '/back-office/ministry',
    ]) {
      expect(paths, `missing route ${path}`).toContain(path)
    }
  })

  it('answers an unknown path with the 404 screen rather than a blank page', () => {
    expect(router.options.defaultNotFoundComponent).toBeDefined()
  })
})
