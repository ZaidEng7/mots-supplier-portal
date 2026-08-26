import type { ReactNode } from 'react'
import * as RadixDialog from '@radix-ui/react-dialog'
import { X } from 'lucide-react'

interface DialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  children: ReactNode
  trigger?: ReactNode
}

/** Accessible modal built on Radix — focus trap, Escape-to-close, and labelled-by wiring for free. */
export function Dialog({ open, onOpenChange, title, description, children, trigger }: DialogProps) {
  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      {trigger ? <RadixDialog.Trigger asChild>{trigger}</RadixDialog.Trigger> : null}
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-40" style={{ backgroundColor: 'var(--color-bg-overlay)' }} />
        <RadixDialog.Content
          className="fixed left-1/2 top-1/2 z-50 w-full max-w-md -translate-x-1/2 -translate-y-1/2 rounded-[0.5rem] p-6 shadow-xl"
          style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
        >
          <div className="mb-4 flex items-start justify-between">
            <div>
              <RadixDialog.Title className="text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
                {title}
              </RadixDialog.Title>
              {description ? (
                <RadixDialog.Description className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                  {description}
                </RadixDialog.Description>
              ) : null}
            </div>
            <RadixDialog.Close
              aria-label="Close"
              className="rounded p-1 outline-none"
              style={{ color: 'var(--color-text-muted)' }}
              onFocus={(e) => (e.currentTarget.style.boxShadow = 'var(--focus-ring)')}
              onBlur={(e) => (e.currentTarget.style.boxShadow = 'none')}
            >
              <X size={18} aria-hidden="true" />
            </RadixDialog.Close>
          </div>
          {children}
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  )
}
