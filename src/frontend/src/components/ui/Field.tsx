import { useId } from 'react'
import type { ReactNode } from 'react'
import * as Label from '@radix-ui/react-label'

interface FieldProps {
  label: string
  error?: string
  hint?: string
  required?: boolean
  children: (inputProps: { id: string; 'aria-describedby'?: string; 'aria-invalid'?: boolean }) => ReactNode
}

/** Label + input-slot + error/hint wiring so every form control gets consistent a11y association. */
export function Field({ label, error, hint, required, children }: FieldProps) {
  const id = useId()
  const errorId = error ? `${id}-error` : undefined
  const hintId = hint ? `${id}-hint` : undefined
  const describedBy = [errorId, hintId].filter(Boolean).join(' ') || undefined

  return (
    <div className="flex flex-col gap-1.5">
      <Label.Root
        htmlFor={id}
        className="text-[length:var(--text-body-sm)] font-[var(--fw-medium)]"
        style={{ color: 'var(--color-text-secondary)' }}
      >
        {label}
        {required ? (
          <span aria-hidden="true" style={{ color: 'var(--color-danger-fg)' }}>
            {' '}
            *
          </span>
        ) : null}
      </Label.Root>
      {children({ id, 'aria-describedby': describedBy, 'aria-invalid': !!error })}
      {hint && !error ? (
        // --color-text-muted fails WCAG AA at caption size (3.83:1 vs the 4.5:1 required for
        // small text) - use --color-text-secondary here, which clears AA at 5.99:1.
        <p id={hintId} className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={errorId} role="alert" className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-danger-fg)' }}>
          {error}
        </p>
      ) : null}
    </div>
  )
}
