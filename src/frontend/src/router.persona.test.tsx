import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { Suspense, type ReactNode } from 'react'
import type { AnyRoute } from '@tanstack/react-router'
import { mockFetch, renderPage } from './test/renderPage'

/**
 * Each shell layout refuses the persona it is not for, and lets the one it is for through.
 *
 * <p><b>Why this file exists separately from `router.test.tsx`.</b> That file asserts the SHAPE of the
 * route tree - every layout's component body mentions the claims - which is the check that fails when a
 * fourth layout is added without a persona check. It cannot say whether the predicate is the right way
 * round. A layout that refused exactly the persona it exists for would satisfy it completely.</p>
 *
 * <p><b>And the gap it closes.</b> No test rendered a layout component at all. The two older layouts
 * have carried their refusal since a system administrator with a stale `?redirect=/dashboard` landed on
 * a supplier dashboard that could never load; the third was added because a supplier session reaching
 * /evaluation got the back-office chrome and a screen whose every query answered 404. Three refusals,
 * each written from a real defect, none of them executed by anything until now.</p>
 *
 * <p>The router module is loaded for real - `createRoute` and the route tree have to be genuine or this
 * would be testing a fixture - and only the four view-layer exports the shells call are replaced,
 * because a shell outside a RouterProvider cannot render a Link.</p>
 */
vi.mock('@tanstack/react-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tanstack/react-router')>()),
  Link: ({ to, children }: { to: string; children: ReactNode }) => <a href={to}>{children}</a>,
  // The screen the layout would put inside the shell. A testid rather than null, so "the persona was
  // let through" is something this file can assert rather than infer from the absence of a 403.
  Outlet: () => <div data-testid="the-screen-behind-the-shell" />,
  useRouterState: () => '/nowhere-in-particular',
  useNavigate: () => () => undefined,
}))

const { router } = await import('./router')
const { useAuthStore } = await import('./lib/authStore')

/**
 * The layouts and the persona each one is for, typed out by hand with the reason.
 *
 * <p>A walk of the tree supplies the denominator below; this supplies the direction, which a walk
 * cannot. `/back-office` and `evaluator-layout` are staff ground - an evaluator is staff scoped by
 * assignment - and `supplier-layout` is the company's own space.</p>
 */
const LAYOUTS: Array<{ id: string; belongsTo: 'supplier' | 'staff' }> = [
  // The leading slash on the pathless two is TanStack's, not a typo: it prefixes a route id with its
  // parent's path even when the route declares none. The denominator below is what said so.
  { id: '/supplier-layout', belongsTo: 'supplier' },
  { id: '/evaluator-layout', belongsTo: 'staff' },
  { id: '/back-office', belongsTo: 'staff' },
]

function walk(route: AnyRoute, trail: AnyRoute[] = []): AnyRoute[] {
  const children = (route.children ?? []) as AnyRoute[]
  return children.reduce((all, child) => walk(child, all), [...trail, route])
}

const shellLayouts = walk(router.routeTree).filter((r) => String(r.options?.component ?? '').includes('Shell'))

function layoutComponent(id: string): () => ReactNode {
  const route = shellLayouts.find((r) => r.id === id)
  expect(route, `${id} is not a shell layout in the route tree`).toBeDefined()
  return route!.options!.component as unknown as () => ReactNode
}

function signIn(persona: 'supplier' | 'staff') {
  useAuthStore.setState({
    accessToken: 'token',
    status: 'authenticated',
    claims:
      persona === 'supplier'
        ? { userId: 'u-1', email: 'supplier@example.test', supplierId: 'sup-1', permissions: ['rfq.read'] }
        : { userId: 'u-2', email: 'staff@example.test', organizationId: 'org-1', permissions: ['rfq.read'] },
  })
}

describe('shell layouts and the persona each one admits', () => {
  let restore: (() => void) | undefined

  afterEach(() => {
    restore?.()
    restore = undefined
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle', expired: false, lastEmail: null })
  })

  /**
   * Both shells poll /system/status for the ERP banner, declared here so an undeclared request cannot
   * throw and take the persona assertion with it.
   *
   * <p>The Suspense boundary is the root route's, stood up again locally: the shells are `lazy()` in
   * `router.tsx`, so a layout rendered without one suspends and puts nothing in the document - which is
   * what the first run of this file reported, and reads exactly like a refusal.</p>
   */
  function renderLayout(id: string) {
    restore = mockFetch({ '/api/v1/system/status': { erpNotConfigured: false, expiringDocumentCount: 0 } })
    const Layout = layoutComponent(id)
    renderPage(<Suspense fallback={null}><Layout /></Suspense>)
  }

  it('finds every layout this file names, and no others', () => {
    // The denominator, and it has teeth in both directions: a fourth shell layout fails here until it
    // is given a row above with the persona it serves, and a renamed id fails rather than silently
    // dropping out of the cases below.
    const byName = (a: string, b: string) => a.localeCompare(b)
    expect(shellLayouts.map((r) => String(r.id)).sort(byName)).toEqual(LAYOUTS.map((l) => l.id).sort(byName))
  })

  it.each(LAYOUTS)('$id admits the $belongsTo it exists for', async ({ id, belongsTo }) => {
    signIn(belongsTo)

    renderLayout(id)

    // `find`, not `get`: the shell arrives on the lazy chunk's own tick.
    expect(await screen.findByTestId('the-screen-behind-the-shell')).toBeInTheDocument()
    expect(screen.queryByText('403')).not.toBeInTheDocument()
  })

  it.each(LAYOUTS)('$id refuses the other persona with a 403 rather than a redirect', async ({ id, belongsTo }) => {
    signIn(belongsTo === 'supplier' ? 'staff' : 'supplier')

    renderLayout(id)

    // A refusal, not a bounce. A redirect would loop somebody who followed a link, and the code says
    // what happened where a blank screen would not.
    expect(await screen.findByText('403')).toBeInTheDocument()
    expect(screen.queryByTestId('the-screen-behind-the-shell')).not.toBeInTheDocument()
  })
})
