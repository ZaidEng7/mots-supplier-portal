import { forwardRef } from 'react'
import type { InputHTMLAttributes } from 'react'

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  invalid?: boolean
}

/** Text input primitive — token-driven border/focus states, invalid state for form errors. */
export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { invalid = false, className = '', style, ...rest },
  ref,
) {
  return (
    <input
      ref={ref}
      aria-invalid={invalid || undefined}
      className={`w-full rounded-[0.375rem] px-3 py-2 text-[length:var(--text-body)] outline-none transition-colors ${className}`}
      style={{
        backgroundColor: 'var(--color-bg-surface)',
        color: 'var(--color-text-primary)',
        border: `1px solid ${invalid ? 'var(--color-danger-solid)' : 'var(--color-border-input)'}`,
        ...style,
      }}
      onFocus={(e) => {
        e.currentTarget.style.boxShadow = 'var(--focus-ring)'
        e.currentTarget.style.borderColor = invalid ? 'var(--color-danger-solid)' : 'var(--color-border-focus)'
      }}
      onBlur={(e) => {
        e.currentTarget.style.boxShadow = 'none'
        e.currentTarget.style.borderColor = invalid ? 'var(--color-danger-solid)' : 'var(--color-border-input)'
      }}
      {...rest}
    />
  )
})
