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
