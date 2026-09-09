import { forwardRef } from 'react'
import type { InputHTMLAttributes } from 'react'

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  invalid?: boolean
}

/**
 * Text input primitive — token-driven border/focus states, invalid state for form errors.
 *
 * <p><b>Disabled and read-only are rendered, not merely set.</b> This component paints its own
 * background and colour as inline styles, and an inline style beats the user agent's own disabled
 * rendering — so before this, `disabled` changed nothing a person could see. The onboarding wizard sets
 * `disabled` on every field once an application is submitted (`OnboardingPage.tsx`), which meant a form
 * that looked editable and silently refused input. DESIGN-SYSTEM.md §6.2 lists both states as required
 * and `--color-text-disabled` had been defined in both themes since the token layer was written, with no
 * consumer.</p>
 *
 * <p>The two states say different things and look different accordingly: <b>disabled</b> is "not for you,
 * not now" — sunken fill, muted text, a not-allowed cursor; <b>read-only</b> is "this is the value, it is
 * simply not editable here" — no fill, no border, per §6.2's "read-only (no border, muted)".</p>
 */
export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { invalid = false, className = '', style, onFocus, onBlur, disabled, readOnly, ...rest },
  ref,
) {
  const borderColor = invalid ? 'var(--color-danger-solid)' : 'var(--color-border-input)'

  return (
    <input
      ref={ref}
      aria-invalid={invalid || undefined}
      disabled={disabled}
      readOnly={readOnly}
      className={`msp-input w-full rounded-[var(--radius-md)] px-3 py-2 text-[length:var(--text-body)] outline-none transition-colors duration-[var(--motion-fast)] ease-[var(--ease-out)] ${disabled ? 'cursor-not-allowed' : ''} ${className}`}
      style={{
        // The resting border is a custom property so the stylesheet can raise it on hover: an inline
        // style beats a class, which is why this component had no hover state for its whole life.
        ['--input-border' as string]: readOnly && !disabled ? 'transparent' : borderColor,
        backgroundColor: disabled ? 'var(--color-bg-sunken)' : readOnly ? 'transparent' : 'var(--color-bg-surface)',
        color: disabled ? 'var(--color-text-disabled)' : 'var(--color-text-primary)',
        border: '1px solid var(--input-border)',
        ...style,
      }}
      onFocus={(e) => {
        if (!disabled) {
          e.currentTarget.style.boxShadow = 'var(--focus-ring)'
          e.currentTarget.style.setProperty('--input-border', invalid ? 'var(--color-danger-solid)' : 'var(--color-border-focus)')
        }
        onFocus?.(e)
      }}
      onBlur={(e) => {
        e.currentTarget.style.boxShadow = 'none'
        // Back to the resting border for whichever state this field is in - a read-only field that
        // grew a border on focus and kept it would read as editable afterwards.
        e.currentTarget.style.setProperty('--input-border', readOnly && !disabled ? 'transparent' : borderColor)
        onBlur?.(e)
      }}
      {...rest}
    />
  )
})
