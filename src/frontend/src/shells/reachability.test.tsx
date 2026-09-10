import { describe, expect, it } from 'vitest'
import { router } from '../router'
import {
  BACK_OFFICE_CHROME, BACK_OFFICE_NAV, PARAMETERISED_ROUTE, ROUTE_EXEMPTIONS, SUPPLIER_CHROME,
  SUPPLIER_NAV,
} from './navigation'

/**
 * Every route this product declares can be reached by clicking, or says in writing why it cannot.
 *
 * <p><b>The defect this closes.</b> Three screens shipped built, permissioned and unreachable: nothing
 * in either shell linked to them, so the only way in was to type the address. They were found by a
 * person reading the route tree beside the navigation, twice, months apart. Nothing else could have
 * found them - a route that renders correctly when you visit it passes every test in the suite, and an
 * accessibility sweep that is handed a list of routes will happily sweep a route no user can get to.</p>
 *
 * <p><b>What it measures.</b> The router's own path table on one side, the navigation lists both shells
 * render on the other. Not a copy of either: `router` is the object the application mounts, and the
 * navigation is the array the sidebar maps over, so neither can drift from what ships without this
 * failing.</p>
 *
 * <p><b>What it cannot do.</b> It proves a link exists, not that anybody can see it. A row behind a
 * permission nobody holds would satisfy this and still be unreachable in practice, which is what the
 * permission tests beside it are for. Two instruments, neither sufficient alone.</p>
 */

/** Every path the router can match, from the router itself rather than from a list beside it. */
function declaredRoutes(): string[] {
  // `routesById` is typed as a union of every literal path, which is exactly the wrong shape for
  // walking it: the keys are what we are trying to discover. Read as a record, deliberately.
  const byId = router.routesById as unknown as Record<string, { fullPath?: string }>
  return Object.keys(byId)
    .filter((id) => !id.includes('__root__'))
    .map((id) => byId[id]?.fullPath)
    .filter((path): path is string => typeof path === 'string' && path.length > 0)
    .map((path) => (path.length > 1 ? path.replace(/\/$/, '') : path))
    .filter((path, index, all) => all.indexOf(path) === index)
    .sort()
}

/** Every destination either shell offers, from sidebar groups and top-bar chrome alike. */
function offeredDestinations(): Set<string> {
  const rows = [
    ...BACK_OFFICE_NAV.flatMap((group) => group.items),
    ...SUPPLIER_NAV.flatMap((group) => group.items),
    ...BACK_OFFICE_CHROME,
    ...SUPPLIER_CHROME,
  ]
  return new Set(rows.map((item) => item.to))
}

const ROUTES = declaredRoutes()
const OFFERED = offeredDestinations()

describe('every route is reachable', () => {
  it('reads a real route table and a real navigation list', () => {
    // The denominator, asserted before anything is checked against it. A guard that read zero routes
    // would pass every expectation below in silence, which is the shape of instrument this repository
    // has had to repair six times.
    expect(ROUTES.length).toBeGreaterThan(60)
    expect(OFFERED.size).toBeGreaterThan(30)
    expect(ROUTES).toContain('/back-office/dashboard')
    expect(ROUTES).toContain('/back-office/rfqs/$referenceCode/award')
  })

  it('offers, or excuses in writing, every route that takes no parameter', () => {
    const unreachable = ROUTES
      .filter((path) => !PARAMETERISED_ROUTE.test(path))
      .filter((path) => !OFFERED.has(path))
      .filter((path) => !(path in ROUTE_EXEMPTIONS))

    expect(
      unreachable,
      'a route nothing links to can only be found by typing its address - link it, or write down why not',
    ).toEqual([])
  })

  it('gives every parameterised route a screen that can supply the parameter', () => {
    // These can never be navigation rows: a row would have to invent the reference code. What it CAN
    // be checked against is that the list it is opened from is itself reachable, which is the parent
    // path with the parameter and everything after it removed.
    const orphaned = ROUTES
      .filter((path) => PARAMETERISED_ROUTE.test(path))
      .filter((path) => {
        const parent = path.slice(0, path.indexOf('/$'))
        return !OFFERED.has(parent) && !(parent in ROUTE_EXEMPTIONS)
      })

    expect(orphaned, 'a detail route is reached from its list, so the list has to be reachable').toEqual([])
  })

  it('states a real reason for every exemption, and exempts nothing that exists in the navigation', () => {
    const problems: string[] = []
    for (const [path, reason] of Object.entries(ROUTE_EXEMPTIONS)) {
      if (!ROUTES.includes(path)) problems.push(`${path} is exempted and is not a route`)
      // A one-word reason is a shrug. The point of the list is that a reader can disagree with it.
      if (reason.length < 40) problems.push(`${path}'s reason is too short to argue with`)
      if (OFFERED.has(path)) problems.push(`${path} is exempted and also offered - one of the two is wrong`)
    }
    expect(problems).toEqual([])
  })

  it('every destination the shells offer is a route that exists', () => {
    // The other direction, and the cheaper defect: a row pointing at a path the router does not
    // declare renders a link straight to the not-found screen.
    const broken = [...OFFERED].filter((to) => !ROUTES.includes(to))
    expect(broken, 'a navigation row pointing nowhere is worse than no row').toEqual([])
  })

  it('the check can fail', () => {
    // Revert-to-red, in the shape of the real defect: a route the shells do not offer and nobody
    // wrote a reason for. If this ever passes as reachable, the guard above is measuring nothing.
    const invented = '/back-office/a-screen-nobody-linked'
    expect(OFFERED.has(invented)).toBe(false)
    expect(invented in ROUTE_EXEMPTIONS).toBe(false)
    expect(PARAMETERISED_ROUTE.test(invented)).toBe(false)
    // And the parameter test itself, which is worthless if the pattern matches nothing.
    expect(PARAMETERISED_ROUTE.test('/rfqs/$referenceCode')).toBe(true)
  })
})
