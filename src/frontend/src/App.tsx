// The application shell: the router, the query client, the toast host and the chrome that has to exist on every page - the
// maintenance banner, the language's direction, and the two overlays that can appear over anything.

import { QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from '@tanstack/react-router'
import { useDirection } from './i18n/useDirection'
import { queryClient } from './lib/queryClient'
import { ToastProvider } from './components/ui'
import { router } from './router'

function App() {
  useDirection()

  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <RouterProvider router={router} />
      </ToastProvider>
    </QueryClientProvider>
  )
}

export default App
