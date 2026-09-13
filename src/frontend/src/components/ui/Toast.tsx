// The app-wide toast host, on Radix Toast: a polite live region with swipe-to-dismiss. Wrap the app once.
//
// The viewport is pinned with a logical end-4, so in Arabic the toast sits on the LEFT. A hardcoded "right" meant the
// toast had to be swiped away from its own edge, and the enter animation would have travelled in from the opposite
// side of the screen to the one it sat on. The side is read from the language rather than from useDirection, which
// also writes the document attributes - one owner for that side effect is enough.
//
// Ids come from crypto.randomUUID rather than Date.now() plus Math.random(). This id carries no security property -
// it keys a toast in a list - so Sonar's weak-PRNG finding is a false positive, but randomUUID is free,
// collision-free, and removes the ambiguity for the next reader.
//
// msp-toast enters and exits along the edge it can be swiped to, in src/index.css, which is what makes
// swipe-to-dismiss discoverable without being taught.
//
// bottom-20 on mobile clears the fixed MobileTabBar, per SupplierShell and DESIGN-SYSTEM.md, and the stack sits above
// a modal per §4.5, so a confirmation is never hidden behind the dialog that produced it.

import { createContext, useCallback, useContext, useState } from 'react'
import type { ReactNode } from 'react'
import * as RadixToast from '@radix-ui/react-toast'
import { useTranslation } from 'react-i18next'
import { RTL_LANGUAGES } from '../../i18n/rtl'

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
  success: 'var(--color-success-solid)',
  danger: 'var(--color-danger-solid)',
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([])
  const { i18n } = useTranslation()
  const swipe = RTL_LANGUAGES.has(i18n.language) ? 'left' : 'right'

  const notify = useCallback((toast: Omit<ToastItem, 'id'>) => {
    setToasts((prev) => [...prev, { ...toast, id: crypto.randomUUID() }])
  }, [])

  const dismiss = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }, [])

  return (
    <ToastContext.Provider value={{ notify }}>
      <RadixToast.Provider swipeDirection={swipe}>
        {children}
        {toasts.map((toast) => (
          <RadixToast.Root
            key={toast.id}
            duration={5000}
            onOpenChange={(open) => {
              if (!open) dismiss(toast.id)
            }}
            className="msp-toast rounded-[var(--radius-md)] p-4"
            style={{
              backgroundColor: 'var(--color-bg-surface)',
              border: `1px solid ${kindColor[toast.kind]}`,
              boxShadow: 'var(--shadow-lg)',
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
        <RadixToast.Viewport
          style={{ zIndex: 'var(--z-toast)' }}
          className="fixed bottom-20 end-4 flex w-96 max-w-[calc(100%-2rem)] flex-col gap-2 outline-none md:bottom-4" />
      </RadixToast.Provider>
    </ToastContext.Provider>
  )
}

export function useToast() {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast must be used within a ToastProvider')
  return ctx
}
