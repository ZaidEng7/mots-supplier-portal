import { useEffect, useRef } from 'react'
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
  // Task #22/NFR-A11Y-002: Radix's own focus-restore-on-close only knows what to restore to when
  // the dialog was opened through its own `Trigger`. Every real call site in this app opens the
  // dialog by flipping external `open` state from a plain Button instead (TeamPage's "Invite
  // member", AddressDialog, BankAccountDialog, PersonDialog, ReasonDialog - none of them pass
  // `trigger`) - Radix has no trigger element to remember for any of them, so focus fell through
  // to <body> on close, a real keyboard/screen-reader disorientation bug, not a hypothetical one:
  // caught by tests/e2e/app-keyboard.spec.ts driving Escape with real keyboard input, not a click.
  // Captured independently of whether `trigger` is used, so this fixes every call site at once
  // rather than only the ones a future caller remembers to wire through Trigger.
  const previouslyFocused = useRef<HTMLElement | null>(null)
  useEffect(() => {
    if (open) previouslyFocused.current = document.activeElement as HTMLElement | null
  }, [open])

  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      {trigger ? <RadixDialog.Trigger asChild>{trigger}</RadixDialog.Trigger> : null}
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-40" style={{ backgroundColor: 'var(--color-bg-overlay)' }} />
        {/*
          * The dialog is capped and scrolls its own body, rather than growing until its buttons leave
          * the screen.
          *
          * Found on the offering editor: it carries a repeater for "additional attributes", and five
          * rows pushed Save and Cancel below the fold while clipping the Arabic name field off the
          * top. The form was intact and simply could not be finished or dismissed - a modal traps
          * focus, so scrolling the page behind it is not a way out either.
          *
          * Fixed here rather than on that one page because any dialog with a repeater in it has the
          * same shape, and the next one will be written by somebody who never saw this.
          */}
        <RadixDialog.Content
          className="fixed left-1/2 top-1/2 z-50 flex max-h-[calc(100dvh-2rem)] w-full max-w-md -translate-x-1/2 -translate-y-1/2 flex-col rounded-[0.5rem] p-6 shadow-xl"
          style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
          onCloseAutoFocus={(e) => {
            if (previouslyFocused.current) {
              e.preventDefault()
              previouslyFocused.current.focus()
            }
          }}
        >
          <div className="mb-4 flex flex-none items-start justify-between">
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
          {/* The scroll region: the title and the close button stay put, the form moves. */}
          <div className="min-h-0 flex-1 overflow-y-auto">{children}</div>
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  )
}
