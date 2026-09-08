import type { ReactNode } from 'react'
import { Button } from './Button'
import { Card } from './Card'
import { SkeletonTable } from './Skeleton'

/**
 * The three pieces every list screen in this product repeats: a heading, a labelled filter control, and a
 * body that is either loading, broken, empty or a table. They were written out longhand on each screen
 * until SCR-402 and SCR-307 made the fourth and fifth copy, at which point the duplication stopped being
 * incidental — the same markup drifting apart screen by screen is how a heading ends up one size on one
 * page and another size on the next.
 *
 * <p>Deliberately thin. Each of these holds layout and nothing else: no query, no state, no fetching. A
 * screen still decides what it asks for, what it calls its filters, and what its empty case means, because
 * those are the parts that differ and the parts a reader needs to see on the screen itself.</p>
 */

/** Screen title and its one-line explanation of what the reader is looking at. */
export function PageHeading({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <div>
      <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
        {title}
      </h1>
      {subtitle ? (
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {subtitle}
        </p>
      ) : null}
    </div>
  )
}

/**
 * One filter control with its caption.
 *
 * <p>`htmlFor` decides which element the caption is: a real `<label>` when it names a control with an id -
 * a text input, say - and a plain `<span>` otherwise. A `<label for>` pointing at nothing is worse than no
 * label, because a screen reader announces the association and then lands the user nowhere.</p>
 */
export function FilterField({ label, htmlFor, children }: { label: string; htmlFor?: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      {htmlFor ? (
        <label className="text-[length:var(--text-caption)]" htmlFor={htmlFor} style={{ color: 'var(--color-text-secondary)' }}>
          {label}
        </label>
      ) : (
        <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
          {label}
        </span>
      )}
      {children}
    </div>
  )
}

/** The row of filters above a list. */
export function FilterBar({ children }: { children: ReactNode }) {
  return <div className="flex flex-wrap gap-4">{children}</div>
}

/**
 * The body of a list card: loading, failed, empty, or the caller's table.
 *
 * <p>The four are kept distinct on purpose. "We could not load this" and "there is nothing here" are
 * different facts about the world, and a screen that renders an empty table for a failed request tells the
 * reader the second when the first is true.</p>
 */
export function ListState({
  isPending, isError, isEmpty, loadingLabel, errorText, emptyText, skeletonRows = 5, children,
}: {
  isPending: boolean
  isError: boolean
  isEmpty: boolean
  loadingLabel: string
  errorText: string
  emptyText: string
  skeletonRows?: number
  children: ReactNode
}) {
  if (isPending) return <SkeletonTable label={loadingLabel} rows={skeletonRows} />
  if (isError) return <p style={{ color: 'var(--color-danger)' }}>{errorText}</p>
  if (isEmpty) return <p style={{ color: 'var(--color-text-secondary)' }}>{emptyText}</p>
  return <>{children}</>
}

/** The next page of a keyset-paged list. Renders nothing when there is no next page. */
export function LoadMore({
  hasNextPage, isFetching, onClick, label,
}: {
  hasNextPage: boolean
  isFetching: boolean
  onClick: () => void
  label: string
}) {
  if (!hasNextPage) return null
  return (
    <div className="mt-4">
      <Button variant="secondary" onClick={onClick} disabled={isFetching}>
        {label}
      </Button>
    </div>
  )
}

/**
 * A card holding one paged list: its title, the four body states, and the next-page button.
 *
 * <p>Two screens wrote this scaffolding out identically and a third was about to. The parts that differ
 * between list screens are the table and the words; the parts that do not are the ones collected here, so a
 * new list screen inherits the paging affordance rather than reimplementing it - and forgetting the
 * next-page button is the specific defect MSP-84 recorded, where a list silently ended at twenty rows.</p>
 */
export function ListCard({
  title, isPending, isError, isEmpty, loadingLabel, errorText, emptyText, skeletonRows,
  hasNextPage = false, isFetchingNextPage = false, onLoadMore, loadMoreLabel, children,
}: {
  title: string
  isPending: boolean
  isError: boolean
  isEmpty: boolean
  loadingLabel: string
  errorText: string
  emptyText: string
  skeletonRows?: number
  hasNextPage?: boolean
  isFetchingNextPage?: boolean
  onLoadMore?: () => void
  loadMoreLabel?: string
  children: ReactNode
}) {
  return (
    <Card title={title}>
      <ListState
        isPending={isPending}
        isError={isError}
        isEmpty={isEmpty}
        loadingLabel={loadingLabel}
        errorText={errorText}
        emptyText={emptyText}
        skeletonRows={skeletonRows}
      >
        {children}
        {onLoadMore && loadMoreLabel ? (
          <LoadMore
            hasNextPage={hasNextPage}
            isFetching={isFetchingNextPage}
            onClick={onLoadMore}
            label={loadMoreLabel}
          />
        ) : null}
      </ListState>
    </Card>
  )
}
