import type { CSSProperties } from 'react'

interface SkeletonProps {
  /** Any CSS length. Defaults to filling its container so a bar matches the column it stands in. */
  width?: string
  height?: string
  radius?: string
  className?: string
  style?: CSSProperties
}

/**
 * Base loading placeholder (T2-32).
 *
 * DESIGN-SYSTEM.md §6.17 "States (empty / loading / error / success)": *"**Loading:** **skeleton**
 * shaped like final content (cards/rows/detail), shimmering with reduced motion respected; never a
 * bare full-screen spinner."* The shimmer and its reduced-motion opt-out live in `index.css` under
 * `.msp-skeleton` - CSS, matching how every other animation in this codebase is done (see the
 * global `prefers-reduced-motion` block that MSP-72 added there).
 *
 * <p>Decorative by definition: a shimmering bar carries no information a screen reader should read,
 * so every bar is `aria-hidden`. The announcement belongs to the container - see
 * {@link SkeletonList} / {@link SkeletonGrid}, which own the live region.</p>
 */
export function Skeleton({ width = '100%', height = '1rem', radius = '0.375rem', className = '', style }: SkeletonProps) {
  return (
    <span
      aria-hidden="true"
      className={`msp-skeleton block ${className}`}
      style={{ width, height, borderRadius: radius, backgroundColor: 'var(--color-bg-sunken)', ...style }}
    />
  )
}

interface SkeletonContainerProps {
  /**
   * Accessible name for the live region, e.g. "Loading RFQs". Required rather than defaulted so a
   * caller cannot ship an unlabelled "Loading..." that tells a screen-reader user nothing about
   * WHICH region is pending - several can be on one dashboard at once (SCR-120, SCR-400).
   */
  label: string
}

interface SkeletonListProps extends SkeletonContainerProps {
  /**
   * Row count. A prop, not a constant: SCR-120's lists are "top 5" while SCR-400's task lists and
   * SCR-160's tables are longer, and a skeleton that does not match the row count it replaces
   * causes exactly the layout shift DESIGN-SYSTEM.md §6.17 exists to prevent. The docs do not name
   * a number for any individual list - see the batch report's documentation-gaps section.
   */
  rows?: number
}

/**
 * `SkeletonList` - the variant SCR-120, SCR-160, SCR-400, SCR-500 and SCR-700 name directly
 * (SCREEN-SPECIFICATIONS.md "Components used" / "*Loading:*" lines). Stacked full-width rows,
 * shaped like the `DataTable` rows and list items it stands in for.
 *
 * <p><b>Accessibility.</b> ACCESSIBILITY.md has no clause naming skeletons specifically (reported
 * as a documentation gap); this implements the two clauses that do govern an async placeholder:
 * §6 "Toasts / status messages" - *"container `role="status"`/`aria-live="polite"` for
 * success/info ... **do not move focus**"* - and §7's async pattern - *"disables the primary button
 * with `aria-busy` and an accessible "Submitting..." status"*, whose `aria-busy`-plus-named-status
 * shape is what is reused here. The visible bars are `aria-hidden`; the `.sr-only` label is the
 * only thing announced, once, politely, with no focus move.</p>
 */
export function SkeletonList({ label, rows = 5 }: SkeletonListProps) {
  return (
    <div role="status" aria-live="polite" aria-busy="true" className="flex flex-col gap-3">
      <span className="sr-only">{label}</span>
      {Array.from({ length: rows }, (_, i) => (
        <Skeleton key={i} height="2.25rem" />
      ))}
    </div>
  )
}

interface SkeletonTableProps extends SkeletonContainerProps {
  /** Body rows, excluding the header row. */
  rows?: number
  /** Data columns, excluding the frozen row-header column. */
  columns?: number
}

/**
 * `SkeletonTable` - the variant SCR-432 (comparison matrix) names: *"**Matrix** (`inline-start`
 * frozen row headers = criteria/line items; columns = proposals)"*, built with
 * *"`ComparisonMatrix` (frozen headers, horizontal scroll)"*, whose loading state is
 * *"skeleton matrix"*.
 *
 * <p>Shaped to that description rather than to a generic grid: a header row, and a first column
 * that is <b>sticky at `insetInlineStart`</b> so the placeholder scrolls the same way the real
 * matrix does. Using the logical property rather than `left` is what makes it correct in Arabic -
 * the same choice `Table.tsx` already makes for its own sticky cells, so the skeleton and the table
 * it replaces cannot drift apart in RTL.</p>
 *
 * <p>The header bar and the frozen column are visually distinguished (taller header, narrower first
 * column) because a matrix skeleton that looked like an even grid would misrepresent the layout the
 * user is about to get, which is the layout-shift failure DESIGN-SYSTEM §6.17 exists to prevent.</p>
 */
export function SkeletonTable({ label, rows = 5, columns = 3 }: SkeletonTableProps) {
  const template = `minmax(8rem, 1.25fr) repeat(${columns}, minmax(6rem, 1fr))`
  return (
    <div
      role="status"
      aria-live="polite"
      aria-busy="true"
      className="w-full overflow-x-auto"
      style={{ border: '1px solid var(--color-border)', borderRadius: '0.5rem' }}
    >
      <span className="sr-only">{label}</span>
      <div className="grid gap-2 p-3" style={{ gridTemplateColumns: template, minWidth: 'max-content' }}>
        {Array.from({ length: columns + 1 }, (_, c) => (
          <Skeleton
            key={`h${c}`}
            height="1.75rem"
            style={c === 0 ? { position: 'sticky', insetInlineStart: 0, zIndex: 1 } : undefined}
          />
        ))}
        {Array.from({ length: rows }, (_, r) =>
          Array.from({ length: columns + 1 }, (_, c) => (
            <Skeleton
              key={`r${r}c${c}`}
              height="1.25rem"
              style={c === 0 ? { position: 'sticky', insetInlineStart: 0, zIndex: 1 } : undefined}
            />
          )),
        )}
      </div>
    </div>
  )
}

interface SkeletonGridProps extends SkeletonContainerProps {
  /** Tile count. See {@link SkeletonListProps.rows} on why this is a prop. */
  items?: number
  /** Tiles per row at desktop width; the grid collapses to one column below `sm`. */
  columns?: number
}

/**
 * `SkeletonGrid` - the variant SCR-600 names (*"`SkeletonGrid` for tiles + charts"*). Card-shaped
 * tiles rather than rows, because what it replaces is a `StatTile` row and a chart area, not a list.
 * Same live-region contract as {@link SkeletonList}.
 */
export function SkeletonGrid({ label, items = 4, columns = 4 }: SkeletonGridProps) {
  return (
    <div
      role="status"
      aria-live="polite"
      aria-busy="true"
      className="grid gap-4"
      // auto-fit rather than a fixed count so the grid reflows at 320px without a breakpoint of
      // its own (ACCESSIBILITY.md §9: "Reflow at 320px/400% with no loss").
      style={{ gridTemplateColumns: `repeat(auto-fit, minmax(min(100%, ${Math.floor(100 / columns)}%), 1fr))` }}
    >
      <span className="sr-only">{label}</span>
      {Array.from({ length: items }, (_, i) => (
        <Skeleton key={i} height="5.5rem" radius="0.75rem" />
      ))}
    </div>
  )
}
