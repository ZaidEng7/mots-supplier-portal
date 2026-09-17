// The place a screen renders inside a shell, with its own loading boundary so the shell stays put while the screen loads.
//
// THE DEFECT THIS CLOSES. Most screens are lazy-loaded, so the first visit to one after a page load suspends while its code
// arrives. The layouts rendered a bare Outlet, which left the nearest Suspense boundary ABOVE the shell. React hides everything
// under the boundary that catches a suspension, so the sidebar, the header and the page all went to display:none together and
// the screen went blank until the code arrived.
//
// Measured, not reasoned about: about 285ms per first visit against a warm dev server, zero on a second visit to the same screen,
// and a full second or more straight after a server restart, when Vite transforms every module on its first request. That is why
// it looked like every button flashed after a restart and none did later.
//
// The boundary sits here, inside the shell and around the screen only, so a first visit now blanks the content area while the
// navigation and header stay on screen. The fallback is null rather than a skeleton on purpose: the wait is a few hundred
// milliseconds, and a skeleton that appears and vanishes that quickly reads as a second flash rather than as loading.
//
// Every shell layout in router.tsx renders this instead of Outlet, and router.test.tsx fails a layout that goes back to a bare one.

import { Suspense } from 'react'
import { Outlet } from '@tanstack/react-router'

export function PageOutlet() {
  return (
    <Suspense fallback={null}>
      <Outlet />
    </Suspense>
  )
}
