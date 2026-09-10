export interface Fact {
  key: string
  label: string
  value: string
}

/**
 * The counts a reader wants before they decide whether to read the page: how many were invited, how
 * many bid, how many questions are still open.
 *
 * <p>Every one of these numbers was already on the tender workspace, and every one of them had to be
 * found by scrolling to the card that held it and counting the rows. Collected here they answer the
 * question the reader arrived with.</p>
 *
 * <p>A description list rather than a table: these are label-and-value pairs, not rows of a dataset,
 * and a screen reader announces them as pairs. The values are tabular-figure aligned to the end, so a
 * column of digits reads as a column in both directions of text.</p>
 */
export function FactList({ facts }: Readonly<{ facts: readonly Fact[] }>) {
  return (
    <dl className="m-0 grid grid-cols-[1fr_auto] gap-x-3 gap-y-2 text-[length:var(--text-body-sm)]">
      {facts.map((fact) => (
        <div key={fact.key} className="contents">
          <dt style={{ color: 'var(--color-text-secondary)' }}>{fact.label}</dt>
          {/* The numeric face rather than the body one: a column of figures only lines up if the
              digits are the same width, which is what --font-numeric is for. */}
          <dd className="m-0 text-end" style={{ color: 'var(--color-text-primary)', fontFamily: 'var(--font-numeric)', fontVariantNumeric: 'tabular-nums' }}>
            {fact.value}
          </dd>
        </div>
      ))}
    </dl>
  )
}
