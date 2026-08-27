import type { ReactNode } from 'react'

interface CardProps {
  title?: string
  action?: ReactNode
  children: ReactNode
}

/** Section container per DESIGN-SYSTEM §6.7 - the surface/border/radius/padding pattern every
 * onboarding section already repeats inline, extracted so new screens don't hand-roll it again. */
export function Card({ title, action, children }: CardProps) {
  return (
    <div className="rounded-[0.75rem] p-6" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
      {title || action ? (
        <div className="mb-3 flex items-center justify-between gap-3">
          {title ? (
            <h2 className="text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
              {title}
            </h2>
          ) : (
            <span />
          )}
          {action}
        </div>
      ) : null}
      {children}
    </div>
  )
}
