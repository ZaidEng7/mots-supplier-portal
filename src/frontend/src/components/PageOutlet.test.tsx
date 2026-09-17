// A screen that is still loading must not take the shell down with it.
//
// Each case builds a small route tree shaped like the real one: a root with its own Suspense, a layout that renders shell chrome,
// an eager screen to land on, and a lazy screen whose code never arrives. It lands on the eager screen first, so the chrome has
// already been shown, then navigates to the lazy one and looks at the chrome while that screen is suspended.
//
// THE CONTROL IS THE SECOND CASE, and it is the defect. With a bare Outlet the nearest boundary is the root's, React hides the
// chrome with display:none, and the assertion that it is gone passes. Without that case the first could pass because the lazy
// screen never suspended at all, or because the chrome was never hidden in this environment, and it would look exactly like a
// fix. With it, the pair can only both pass if the boundary inside the layout is what keeps the chrome up.

import { describe, expect, it } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import { lazy, Suspense, type ReactNode } from 'react'
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
} from '@tanstack/react-router'
import { PageOutlet } from './PageOutlet'

function buildRouter(pageSlot: () => ReactNode) {
  const neverLoads = lazy(() => new Promise<{ default: () => ReactNode }>(() => {}))

  const root = createRootRoute({
    component: () => (
      <Suspense fallback={null}>
        <Outlet />
      </Suspense>
    ),
  })

  const layout = createRoute({
    getParentRoute: () => root,
    id: 'layout',
    component: () => (
      <div>
        <nav>shell chrome</nav>
        {pageSlot()}
      </div>
    ),
  })

  const eager = createRoute({ getParentRoute: () => layout, path: '/eager', component: () => <p>eager screen</p> })
  const slow = createRoute({ getParentRoute: () => layout, path: '/slow', component: neverLoads })

  return createRouter({
    routeTree: root.addChildren([layout.addChildren([eager, slow])]),
    history: createMemoryHistory({ initialEntries: ['/eager'] }),
  })
}

async function visitSlowScreen(pageSlot: () => ReactNode) {
  const router = buildRouter(pageSlot)
  render(<RouterProvider router={router} />)

  expect(await screen.findByText('eager screen')).toBeVisible()
  expect(screen.getByText('shell chrome')).toBeVisible()

  await act(async () => {
    router.history.push('/slow')
    await router.load()
  })

  expect(router.state.location.pathname).toBe('/slow')
}

describe('PageOutlet', () => {
  it('keeps the shell on screen while a lazy screen loads', async () => {
    await visitSlowScreen(() => <PageOutlet />)

    expect(screen.getByText('shell chrome')).toBeVisible()
    expect(screen.getByText('eager screen')).not.toBeVisible()
  })

  it('reproduces the blank screen with a bare Outlet, which is what the first case guards against', async () => {
    await visitSlowScreen(() => <Outlet />)

    expect(screen.getByText('shell chrome')).not.toBeVisible()
  })
})
