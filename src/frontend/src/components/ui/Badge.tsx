import type { ReactNode } from 'react'

export type Tone = 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'brand'

/**
 * The tone for a state, from a table rather than from a chain of comparisons.
 *
 * <p>Six screens wrote the same shape by hand - `x === 'Failed' ? 'danger' : x === 'Sent' ? 'success' :
 * 'neutral'` - which reads as a decision procedure when it is a lookup. A table shows the whole mapping
 * at a glance, and an unmapped state falls to the neutral default rather than to whichever branch
 * happened to be last.</p>
 */
export function toneFor(state: string | null | undefined, map: Readonly<Record<string, Tone>>): Tone {
  return (state && map[state]) || 'neutral'
}

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
      className="inline-flex items-center rounded-full px-2 py-1 text-[length:var(--text-caption)] font-[var(--fw-medium)]"
      style={{ backgroundColor: t.bg, color: t.fg }}
    >
      {children}
    </span>
  )
}
