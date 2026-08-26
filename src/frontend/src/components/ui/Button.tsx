import { forwardRef } from 'react'
import type { ButtonHTMLAttributes } from 'react'

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'
type Size = 'sm' | 'md' | 'lg'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  isLoading?: boolean
}

const variantStyle: Record<Variant, { bg: string; bgHover: string; fg: string; border?: string }> = {
  primary: { bg: 'var(--color-brand-solid)', bgHover: 'var(--color-brand-solid-hover)', fg: 'var(--color-text-inverse)' },
  secondary: { bg: 'var(--color-bg-surface)', bgHover: 'var(--color-bg-hover)', fg: 'var(--color-text-primary)', border: 'var(--color-border-strong)' },
  ghost: { bg: 'transparent', bgHover: 'var(--color-bg-hover)', fg: 'var(--color-text-primary)' },
  danger: { bg: 'var(--danger-500)', bgHover: 'var(--danger-600)', fg: 'var(--color-text-inverse)' },
}

const sizeStyle: Record<Size, string> = {
  sm: 'px-3 py-1.5 text-[length:var(--text-body-sm)]',
  md: 'px-4 py-2 text-[length:var(--text-body)]',
  lg: 'px-5 py-2.5 text-[length:var(--text-body-lg)]',
}

/** Reusable button primitive — token-driven, focus-visible ring, disabled/loading states. */
export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = 'primary', size = 'md', isLoading = false, disabled, className = '', children, style, ...rest },
  ref,
) {
  const v = variantStyle[variant]
  return (
    <button
      ref={ref}
      disabled={disabled || isLoading}
      aria-busy={isLoading || undefined}
      className={`inline-flex items-center justify-center gap-2 rounded-[var(--radius-md,0.375rem)] font-[var(--fw-medium)] transition-colors focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50 ${sizeStyle[size]} ${className}`}
      style={{
        backgroundColor: v.bg,
        color: v.fg,
        border: v.border ? `1px solid ${v.border}` : 'none',
        ...style,
      }}
      onMouseEnter={(e) => {
        if (!disabled && !isLoading) e.currentTarget.style.backgroundColor = v.bgHover
      }}
      onMouseLeave={(e) => {
        e.currentTarget.style.backgroundColor = v.bg
      }}
      onFocus={(e) => {
        e.currentTarget.style.boxShadow = 'var(--focus-ring)'
      }}
      onBlur={(e) => {
        e.currentTarget.style.boxShadow = 'none'
      }}
      {...rest}
    >
      {isLoading ? <span aria-hidden="true" className="h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent" /> : null}
      {children}
    </button>
  )
})
