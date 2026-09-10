import type { ReactNode } from 'react'

/**
 * The tone a figure carries, where it carries one.
 *
 * <p>Reserved, and never decoration. `warning` says somebody is waiting; `success` says something
 * completed. A count with no such meaning takes no tone, which is most of them - a row where every
 * tile is coloured is a row where colour has stopped meaning anything.</p>
 */
export type MetricTone = 'neutral' | 'warning' | 'success' | 'danger'

export interface MetricProps {
  label: string
  /** Already formatted for the reader's locale. This component does not know what a number means. */
  value: string
  tone?: MetricTone
}

const toneColour: Record<MetricTone, string> = {
  neutral: 'var(--color-text-primary)',
  warning: 'var(--color-warning-fg)',
  success: 'var(--color-success-fg)',
  danger: 'var(--color-danger-fg)',
}

/**
 * One figure a dashboard leads with.
 *
 * <p><b>What this replaces.</b> Five dashboards wrote the same eight lines of markup by hand - a
 * bordered box, a small grey label, a number at heading size - and the copies had already begun to
 * differ in how many columns they took at each width. None of them had a surface, a shadow, or any way
 * to tell one figure from another, so a count of three approvals waiting on you looked exactly like a
 * count of forty-one suppliers already approved.</p>
 *
 * <p>The figure is set in the numeric face at the template's own size, which is nearly twice the
 * heading these tiles used: a dashboard's whole job is that its numbers can be read across a desk.</p>
 *
 * <p>The template also puts a line under each figure saying what it is made of - "2 close this week".
 * There is no such line here, because the endpoints behind these tiles return a number and nothing
 * else. Inventing one would mean the dashboard saying something it was not told.</p>
 */
export function Metric({ label, value, tone = 'neutral' }: Readonly<MetricProps>) {
  return (
    <li
      className="rounded-[var(--radius-lg)] p-4"
      style={{
        backgroundColor: 'var(--color-bg-surface)',
        border: '1px solid var(--color-border)',
        boxShadow: 'var(--shadow-sm)',
      }}
    >
      <p className="m-0 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
        {label}
      </p>
      <p
        className="m-0 mt-1.5 text-[length:var(--text-display)] font-[var(--fw-semibold)] leading-none tracking-[-0.02em]"
        style={{ color: toneColour[tone], fontFamily: 'var(--font-numeric)', fontVariantNumeric: 'tabular-nums' }}
      >
        {value}
      </p>
    </li>
  )
}

/**
 * The row a dashboard opens with.
 *
 * <p>`auto-fit` with a floor rather than a column count per breakpoint: the five screens that wrote
 * this by hand had already drifted to three different answers - five columns on one, four on three,
 * two on a phone in all of them - for rows holding four or five tiles. A minimum width lets the row
 * decide, and it is the same decision on every screen.</p>
 */
export function MetricRow({ children }: Readonly<{ children: ReactNode }>) {
  return (
    <ul className="m-0 grid list-none grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-4 p-0">
      {children}
    </ul>
  )
}
