import { QueryClientProvider } from '@tanstack/react-query'
import { AppShell } from './components/AppShell'
import { useDirection } from './i18n/useDirection'
import { queryClient } from './lib/queryClient'
import { HomePage } from './routes/HomePage'

function App() {
  useDirection()

  return (
    <QueryClientProvider client={queryClient}>
      <AppShell>
        <HomePage />
      </AppShell>
    </QueryClientProvider>
  )
}

export default App
