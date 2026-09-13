// The table primitive: the scroll container, the header row, and the body rows.
//
// maxHeight enables an inner VERTICAL scroll, which is RESPONSIVE-AND-RTL.md §4.3's sticky-scroll pattern: a sticky
// <thead> only stays put while scrolling if the scroll container is this element rather than the page. The comparison
// matrix's irreducibly wide and tall grid uses it; every other caller omits it and gets the original
// horizontal-only container unchanged.
//
// A row's background: `highlight` wins over `sticky` deliberately, because a row flagged for attention keeps its
// warning surface even when it is also the pinned one - the flag is why it is pinned.
//
// `bare` drops the table's own border and corners, because the surface around it already has them. It is set by a
// list card, which holds exactly one table and draws the frame itself: two frames a few pixels apart is what a table
// inside a padded card looked like, and it is the difference between this product's list screens and the template
// they are meant to match.
//
// The cell type scale is the one line that made 13px the most common size on every screen the audit measured, because
// tables are most of this product.
//
// THE HEADER takes either form: `labels` declares a plain header row from an array, and `children` is for the rows
// that need more than a word per column - a sticky first cell, a colspan, a sort control. Most tables in this product
// need neither, and spelling six identical <TableHeaderCell>{t(...)} lines out per screen is how they end up
// differing by accident, so exactly one of the two is passed. It is set uppercase at caption size with open
// tracking, which is the approved template's header: it reads as a label for the column rather than as a short first
// row, and it is what stops a dense table looking like a wall of equal-weight text.
//
// msp-row is a hover RULE rather than an inline style, because a pointer state cannot be one. Cells that set their
// own background - flagged and pinned ones - keep it, since an inline style wins over the class, which is the correct
// precedence and not an accident.

import type { CSSProperties, ReactNode } from 'react'
import { useCardHeadingId } from './cardHeading'

function rowBackground(highlight: boolean | undefined, sticky: boolean | undefined): string | undefined {
  if (highlight) return 'var(--color-warning-bg)'
  if (sticky) return 'var(--color-bg-surface)'
  return undefined
}

export function Table({ children, caption, maxHeight, flush = false }: {
  children: ReactNode
  caption?: string
  maxHeight?: string
  flush?: boolean
}) {
  const headingId = useCardHeadingId()
  const namedByCard = headingId !== undefined

  return (
    <div
      className={`w-full overflow-auto ${flush ? '' : 'rounded-[var(--radius-md)]'}`}
      style={{ border: flush ? undefined : '1px solid var(--color-border)', maxHeight }}
    >
      <table
        className="w-full border-collapse text-[length:var(--density-body)]"
        aria-labelledby={namedByCard ? headingId : undefined}
      >
        {caption && !namedByCard ? <caption className="sr-only">{caption}</caption> : null}
        {children}
      </table>
    </div>
  )
}

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
      className={`px-4 py-2.5 text-start text-[length:var(--text-caption)] font-[var(--fw-semibold)] uppercase tracking-[0.06em] ${className}`}
      style={{
        color: 'var(--color-text-secondary)',
        backgroundColor: 'var(--color-bg-sunken)',
        borderBlockEnd: '1px solid var(--color-border)',
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
    <tr className="msp-row border-t" style={{ borderColor: 'var(--color-border)' }}>
      {children}
    </tr>
  )
}

export function TableCell({
  children, sticky, highlight, className = '', style,
}: { children: ReactNode; sticky?: boolean; highlight?: boolean; className?: string; style?: CSSProperties }) {
  return (
    <td
      className={`px-4 py-3 ${className}`}
      style={{
        color: 'var(--color-text-primary)',
        backgroundColor: rowBackground(highlight, sticky),
        fontWeight: highlight ? 'var(--fw-semibold)' : undefined,
        ...(sticky ? { position: 'sticky', insetInlineStart: 0, zIndex: 1 } : {}),
        ...style,
      }}
    >
      {children}
    </td>
  )
}
