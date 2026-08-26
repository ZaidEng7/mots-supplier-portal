import type { ReactNode } from 'react'

type Tone = 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'brand'

const toneStyle: Record<Tone, { bg: string; fg: string }> = {
  neutral: { bg: 'var(--color-bg-sunken)', fg: 'var(--color-text-secondary)' },
  success: { bg: 'var(--success-50)', fg: 'var(--success-600)' },
  warning: { bg: 'var(--warning-50)', fg: 'var(--warning-600)' },
  danger: { bg: 'var(--danger-50)', fg: 'var(--danger-600)' },
  info: { bg: 'var(--info-50)', fg: 'var(--info-600)' },
  brand: { bg: 'var(--color-brand-subtle)', fg: 'var(--color-text-brand)' },
}

interface BadgeProps {
  tone?: Tone
  children: ReactNode
}

/** Status/label pill — token-driven tone pairs (bg + accessible-contrast fg). */
export function Badge({ tone = 'neutral', children }: BadgeProps) {
  const t = toneStyle[tone]
  return (
    <span
      className="inline-flex items-center rounded-full px-2.5 py-0.5 text-[length:var(--text-caption)] font-[var(--fw-medium)]"
      style={{ backgroundColor: t.bg, color: t.fg }}
    >
      {children}
    </span>
  )
}
