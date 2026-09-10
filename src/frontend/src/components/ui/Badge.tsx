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
  success: { bg: 'var(--color-success-bg)', fg: 'var(--color-success-fg)' },
  warning: { bg: 'var(--color-warning-bg)', fg: 'var(--color-warning-fg)' },
  danger: { bg: 'var(--color-danger-bg)', fg: 'var(--color-danger-fg)' },
  info: { bg: 'var(--color-info-bg)', fg: 'var(--color-info-fg)' },
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
      className="inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-0.5 text-[length:var(--text-caption)] font-[var(--fw-medium)]"
      style={{ backgroundColor: t.bg, color: t.fg }}
    >
      {/*
        Decoration, and deliberately not the only thing carrying the state: the label beside it is the
        state, and this chip has never been colour-alone. What the dot buys is a fixed point at the
        start of every chip, so a column of them lines up and can be scanned down rather than read.
      */}
      <span aria-hidden="true" className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: 'currentColor' }} />
      {children}
    </span>
  )
}
