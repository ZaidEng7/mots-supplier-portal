import type { CSSProperties, ReactNode } from 'react'

/** maxHeight enables an inner vertical scroll (RESPONSIVE-AND-RTL.md §4.3's sticky-scroll pattern -
 * a sticky <thead> only stays put while scrolling if the scroll container is this element, not the
 * page), used by the comparison matrix's irreducibly-wide/tall grid; every other caller omits it and
 * gets the original horizontal-only container unchanged. */
export function Table({ children, caption, maxHeight }: { children: ReactNode; caption?: string; maxHeight?: string }) {
  return (
    <div
      className="w-full overflow-auto rounded-[0.5rem]"
      style={{ border: '1px solid var(--color-border)', maxHeight }}
    >
      <table className="w-full border-collapse text-[length:var(--text-body-sm)]">
        {caption ? <caption className="sr-only">{caption}</caption> : null}
        {children}
      </table>
    </div>
  )
}

/**
 * `labels` declares a plain header row from an array; `children` is for the rows that need more than a
 * word per column - a sticky first cell, a colspan, a sort control.
 *
 * <p>Most tables in this product need neither, and spelling six identical `<TableHeaderCell>{t(...)}`
 * lines out per screen is how they end up differing by accident. Pass exactly one of the two.</p>
 */
export function TableHead({ children, labels, sticky }: { children?: ReactNode; labels?: string[]; sticky?: boolean }) {
  return (
    <thead style={{ backgroundColor: 'var(--color-bg-sunken)', ...(sticky ? { position: 'sticky', insetBlockStart: 0, zIndex: 2 } : {}) }}>
      <tr>{labels ? labels.map((label) => <TableHeaderCell key={label}>{label}</TableHeaderCell>) : children}</tr>
    </thead>
  )
}

export function TableHeaderCell({
  children, scope = 'col', sticky, className = '', style,
}: { children: ReactNode; scope?: 'col' | 'row'; sticky?: boolean; className?: string; style?: CSSProperties }) {
  return (
    <th
      scope={scope}
      className={`px-4 py-2.5 text-start font-[var(--fw-semibold)] ${className}`}
      style={{
        color: 'var(--color-text-secondary)',
        backgroundColor: 'var(--color-bg-sunken)',
        ...(sticky ? { position: 'sticky', insetInlineStart: 0, zIndex: 1 } : {}),
        ...style,
      }}
    >
      {children}
    </th>
  )
}

export function TableBody({ children }: { children: ReactNode }) {
  return <tbody>{children}</tbody>
}

export function TableRow({ children }: { children: ReactNode }) {
  return (
    <tr className="border-t" style={{ borderColor: 'var(--color-border)' }}>
      {children}
    </tr>
  )
}

export function TableCell({
  children, sticky, highlight, className = '', style,
}: { children: ReactNode; sticky?: boolean; highlight?: boolean; className?: string; style?: CSSProperties }) {
  return (
    <td
      className={`px-4 py-2.5 ${className}`}
      style={{
        color: 'var(--color-text-primary)',
        backgroundColor: highlight ? 'var(--color-warning-subtle, var(--color-brand-subtle))' : sticky ? 'var(--color-bg-surface)' : undefined,
        fontWeight: highlight ? 'var(--fw-semibold)' : undefined,
        ...(sticky ? { position: 'sticky', insetInlineStart: 0, zIndex: 1 } : {}),
        ...style,
      }}
    >
      {children}
    </td>
  )
}
