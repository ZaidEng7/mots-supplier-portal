import { createContext, useCallback, useContext, useState } from 'react'
import type { ReactNode } from 'react'
import * as RadixToast from '@radix-ui/react-toast'

type ToastKind = 'info' | 'success' | 'danger'

interface ToastItem {
  id: string
  title: string
  description?: string
  kind: ToastKind
}

interface ToastContextValue {
  notify: (toast: Omit<ToastItem, 'id'>) => void
}

const ToastContext = createContext<ToastContextValue | null>(null)

const kindColor: Record<ToastKind, string> = {
  info: 'var(--color-info-solid)',
  success: 'var(--success-500)',
  danger: 'var(--color-danger-solid)',
}

/** App-wide toast host on Radix Toast (polite live region, swipe-to-dismiss). Wrap the app once. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([])

  const notify = useCallback((toast: Omit<ToastItem, 'id'>) => {
    // crypto.randomUUID rather than Date.now()+Math.random(): this id carries no security
    // property (it keys a toast in a list), so Sonar's weak-PRNG finding is a false positive -
    // but randomUUID is free, collision-free, and removes the ambiguity for the next reader.
    setToasts((prev) => [...prev, { ...toast, id: crypto.randomUUID() }])
  }, [])

  const dismiss = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }, [])

  return (
    <ToastContext.Provider value={{ notify }}>
      <RadixToast.Provider swipeDirection="right">
        {children}
        {toasts.map((toast) => (
          <RadixToast.Root
            key={toast.id}
            duration={5000}
            onOpenChange={(open) => {
              if (!open) dismiss(toast.id)
            }}
            className="rounded-[0.5rem] p-4 shadow-lg"
            style={{
              backgroundColor: 'var(--color-bg-surface)',
              border: `1px solid ${kindColor[toast.kind]}`,
            }}
          >
            <RadixToast.Title className="text-[length:var(--text-body)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
              {toast.title}
            </RadixToast.Title>
            {toast.description ? (
              <RadixToast.Description className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {toast.description}
              </RadixToast.Description>
            ) : null}
          </RadixToast.Root>
        ))}
        {/* bottom-20 on mobile clears the fixed MobileTabBar (SupplierShell, DESIGN-SYSTEM.md
            §5.5); back to bottom-4 at md+ where that bar doesn't render. */}
        <RadixToast.Viewport className="fixed bottom-20 end-4 z-50 flex w-96 max-w-full flex-col gap-2 outline-none md:bottom-4" />
      </RadixToast.Provider>
    </ToastContext.Provider>
  )
}

export function useToast() {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast must be used within a ToastProvider')
  return ctx
}
