/**
 * A small set of mutually exclusive filters, drawn as one control rather than as loose buttons.
 *
 * <p><b>What it replaces.</b> The tender list drew its All / Mine / Unassigned filter as three separate
 * buttons, the selected one filled in the brand colour and the other two plain. At a glance that reads
 * as one primary action beside two secondary ones - the same shape the page's own "New tender" button
 * uses - rather than as three settings of one switch. The approved template draws it joined and
 * bordered, with the chosen segment washed rather than filled, and that is what this is.</p>
 *
 * <p><b>Why a fieldset.</b> The native element carries the grouping and the legend names it, so a
 * screen reader announces what the buttons are choosing between before it announces the choices. A
 * `div` with `role="group"` says the same thing with more markup and less support.</p>
 */
import type { ReactNode } from 'react'

export interface Segment<T extends string> {
  value: T
  label: ReactNode
}

export function SegmentedControl<T extends string>({ legend, segments, value, onChange }: Readonly<{
  /** What the segments are choosing between. Announced, and visually hidden. */
  legend: string
  segments: readonly Segment<T>[]
  value: T
  onChange: (value: T) => void
}>) {
  return (
    <fieldset className="m-0 border-0 p-0">
      <legend className="sr-only">{legend}</legend>
      <div
        className="inline-flex overflow-hidden rounded-[var(--radius-sm)]"
        style={{ border: '1px solid var(--color-border-strong)' }}
      >
        {segments.map((segment, index) => {
          const selected = segment.value === value
          return (
            <button
              key={segment.value}
              type="button"
              // `aria-pressed` rather than a radio group: these are buttons that act at once, and a
              // radio would promise a form to submit that does not exist.
              aria-pressed={selected}
              onClick={() => onChange(segment.value)}
              className="px-3 py-1.5 text-[length:var(--text-body-sm)]"
              style={{
                backgroundColor: selected ? 'var(--color-accent-wash)' : 'var(--color-bg-surface)',
                color: selected ? 'var(--color-text-brand)' : 'var(--color-text-secondary)',
                fontWeight: selected ? 'var(--fw-semibold)' : undefined,
                // A rule between segments rather than around each: the group has the outer border, so
                // per-segment borders would double it at both ends.
                borderInlineStart: index === 0 ? undefined : '1px solid var(--color-border-strong)',
              }}
            >
              {segment.label}
            </button>
          )
        })}
      </div>
    </fieldset>
  )
}
