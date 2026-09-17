// Two guards on the route tree itself, as unit tests, for the failures that are invisible in a browser: a duplicate path
// silently shadows whichever route was registered second, and an authenticated screen hung off the root instead of a layout is
// reachable with no session and no shell.
//
// This is the same class of instrument as the e2e sweep's route denominator, one level down. That one catches a NEW screen
// going unexamined; this one catches an EXISTING screen changing shape - and it runs in a second rather than requiring a
// browser. The root is excluded from the walk because it reports path '/' itself, which would collide with the index route and
// make the uniqueness check fail on a tree that is correct.
//
// THE UNIQUENESS CHECK is narrower than it looks, and deliberately kept. TanStack Router already throws "Duplicate routes found
// with id" when two routes collide under the SAME parent - verified by making that edit, and it fails at import, which is
// stronger than any assertion here. What it does not catch is a collision across two DIFFERENT layouts: the ids differ so the
// invariant is satisfied, while the fullPaths are identical and one screen is unreachable. Also verified, by pointing a
// supplier-layout route at the evaluator layout's path - the router constructed happily and this test failed.
//
// EVERY AUTHENTICATED SCREEN sits under a guarded layout. The guard lives on the two layout routes, so a screen parented to the
// root instead is reachable with no session at all - a one-line mistake and an invisible one, because the page renders, queries
// 401, and looks like a data problem rather than a missing guard.
//
// EVERY SHELL LAYOUT HAS A PERSONA CHECK, not only a session check. The two named layouts have carried a persona refusal since
// a system administrator with a stale ?redirect=/dashboard landed on a supplier dashboard that could never load. The evaluator
// layout did not, and no comment explained the asymmetry - so a supplier session reaching /evaluation got the back-office
// chrome, the dark staff rail included, and a screen whose every query answered 404: the same defect, through the one door
// nobody had closed. This asserts the SHAPE rather than the behaviour - a layout route whose component is a shell has a body
// that reads the claims - so it cannot prove the predicate is the right one, which reachability and the integration suite do,
// but it fails when a fourth layout is added without one. The layouts are the routes that mount a shell, whether pathless or a
// real path, and the denominator is asserted because three mount one and a filter that found none would pass in silence.
//
// EVERY SHELL LAYOUT RENDERS ITS SCREEN THROUGH PageOutlet, never a bare Outlet. Screens are lazy, and a bare Outlet leaves the
// nearest Suspense boundary above the shell, so the first visit to a screen hid the sidebar and header along with it and the
// whole app went blank until the code arrived - about 285ms warm, a second or more after a server restart. PageOutlet.test.tsx
// proves the boundary keeps the shell up and carries the control that reproduces the blank; this is the half that fails when a
// fourth layout is added the old way. It reads the component's source, like the persona check, and asserts the same denominator.
//
// THE TWO SHELLS STAY IN SEPARATE URL SPACES, because supplier screens and back-office screens are different shells with
// different navigation and a path that answered under both would render one persona's chrome around the other's page.
//
// The screens this batch added are named INDIVIDUALLY rather than counted, so a route deleted in a refactor fails here with its
// own name instead of as an off-by-one on a total. And an unknown path answers with the 404 screen rather than a blank page.
//
// THE REACHABILITY GUARD, and the reason it exists. Six times in this project a screen has been built, permissioned, tested -
// and reachable only by typing its address. SCR-400, the procurement officer's own home screen, SCR-300, the reviewer's
// dashboard, FEAT-19's reports and SCR-908's about page were all in that state at the end of batch 11. Nothing caught it,
// because every instrument this repository has asks whether a route RESOLVES, and they all pass for a screen no menu mentions.
//
// So it asserts the other direction: for every route, something OUTSIDE the router links to it. The four token-entry routes are
// exempt and named individually - they are reached from an email, which is the correct and only way in - and naming them rather
// than pattern-matching means a NEW unreachable route cannot join the exemption by accident. Two more are excluded as not
// screens: '/' is the address you type to open the product at all, and '/back-office' is a LAYOUT route that renders the shell
// and an Outlet while its children are the screens. Neither can be linked to meaningfully, and both would otherwise sit in the
// list forever teaching people to ignore it.
//
// Its control pins both ends: a collector that returned nothing would fail the rule loudly, and one that returned everything
// would pass it while checking nothing.
//
// THE PATH COLLECTOR is deliberately broader than `to=`. Links are written several ways here - a bare attribute, a template
// with a parameter, and a data array of steps rendered as to={step.path} - and matching only the attribute form reported three
// false positives on the first run. What actually matters is whether any component NAMES the path, because a route nothing
// mentions is one nobody can navigate to. A template parameter reaches the route declared with $referenceCode. The router
// itself is excluded, because it DEFINES the routes and counting it would make every route trivially reachable - which is the
// failure this guard exists to catch.

import { describe, expect, it } from 'vitest'
import type { AnyRoute } from '@tanstack/react-router'
import { router } from './router'

function walk(route: AnyRoute, trail: AnyRoute[] = []): AnyRoute[] {
  const children = (route.children ?? []) as AnyRoute[]
  return children.reduce((all, child) => walk(child, all), [...trail, route])
}

const all = walk(router.routeTree)
const withPaths = all.filter((r) => r.id !== '__root__' && typeof r.path === 'string' && r.path.length > 0)

describe('route tree', () => {
  it('registers every route under exactly one full path', () => {
    const fullPaths = withPaths.map((r) => r.fullPath)
    expect(new Set(fullPaths).size).toBe(fullPaths.length)
  })

  it('puts every authenticated screen under a guarded layout', () => {
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

  it('gives every shell layout a persona check, not only a session check', () => {
    const layouts = all.filter((r) => String(r.options?.component ?? '').includes('Shell'))

    expect(layouts.length).toBeGreaterThanOrEqual(3)

    const withoutPersonaCheck = layouts
      .filter((r: AnyRoute) => {
        const body = String(r.options?.component ?? '')
        return body.includes('Shell') && !body.includes('claims')
      })
      .map((r: AnyRoute) => r.id ?? r.fullPath)

    expect(withoutPersonaCheck, 'a shell layout that only checks for a session lets the wrong persona in')
      .toEqual([])
  })

  it('renders every shell layout\'s screen inside its own loading boundary', () => {
    const layouts = all.filter((r) => String(r.options?.component ?? '').includes('Shell'))

    expect(layouts.length).toBeGreaterThanOrEqual(3)

    const withBareOutlet = layouts
      .filter((r: AnyRoute) => {
        const body = String(r.options?.component ?? '')
        return !body.includes('PageOutlet') || /(?<!Page)Outlet\b/.test(body)
      })
      .map((r: AnyRoute) => r.id ?? r.fullPath)

    expect(withBareOutlet, 'a bare Outlet in a shell blanks the whole app while a lazy screen loads').toEqual([])
  })

  it('keeps the two shells in separate URL spaces', () => {
    const supplier = withPaths.filter((r) => !r.fullPath.startsWith('/back-office')).map((r) => r.fullPath)
    const backOffice = withPaths.filter((r) => r.fullPath.startsWith('/back-office')).map((r) => r.fullPath)

    expect(supplier.some((p) => backOffice.includes(p))).toBe(false)
    expect(backOffice.length).toBeGreaterThan(20)
  })

  it('registers the screens this batch added', () => {
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

describe('every screen is reachable by clicking', () => {
  const ENTERED_FROM_EMAIL = new Set([
    '/reset-password', '/verify-email', '/accept-invite', '/accept-staff-invite',
  ])

  const NOT_A_SCREEN = new Set(['/', '/back-office'])

  it('has no route that only a typed URL can reach', () => {
    const referenced = collectReferencedPaths()

    const unreachable = withPaths
      .map((r) => r.fullPath)
      .filter((p) => !ENTERED_FROM_EMAIL.has(p) && !NOT_A_SCREEN.has(p))
      .filter((p) => !referenced.has(p))

    expect(unreachable, `nothing outside the router mentions: ${unreachable.join(', ')}`).toEqual([])
  })

  it('finds the paths it is looking for', () => {
    const referenced = collectReferencedPaths()

    expect(referenced.has('/back-office/procurement')).toBe(true)
    expect(referenced.has('/dashboard')).toBe(true)
    expect(referenced.has('/this-route-does-not-exist')).toBe(false)
  })
})

function collectReferencedPaths(): Set<string> {
  const modules = import.meta.glob('./**/*.{ts,tsx}', { query: '?raw', import: 'default', eager: true }) as Record<string, string>
  const paths = new Set<string>()

  for (const [file, source] of Object.entries(modules)) {
    if (file.endsWith('/router.tsx')) continue
    if (/\.(test|spec|stories)\./.test(file)) continue

    for (const m of source.matchAll(/['"`](\/[A-Za-z0-9\-_/$${}.]*)['"`]/g)) {
      const raw = m[1]
      paths.add(raw.replace(/\$\{[^}]*\}/g, '$referenceCode').replace(/\/$/, '') || '/')
    }
  }
  return paths
}
