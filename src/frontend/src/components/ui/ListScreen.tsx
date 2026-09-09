import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from './Button'
import { Card } from './Card'
import { Input } from './Input'
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

/**
 * The free-text filter every list screen has, as one element.
 *
 * <p>It always carries a real `<label for>`: an unlabelled search box is the most common accessibility
 * defect in a table toolbar, and a placeholder is not a label - it disappears the moment somebody types.</p>
 */
export function SearchField({
  id, label, placeholder, value, onChange,
}: {
  id: string
  label: string
  placeholder?: string
  value: string
  onChange: (value: string) => void
}) {
  return (
    <FilterField label={label} htmlFor={id}>
      <Input id={id} value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} />
    </FilterField>
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
  if (isError) return <p style={{ color: 'var(--color-danger-fg)' }}>{errorText}</p>
  if (isEmpty) return <p style={{ color: 'var(--color-text-secondary)' }}>{emptyText}</p>
  return <>{children}</>
}

/**
 * "We could not load this", said once, everywhere.
 *
 * <p><b>The defect this closes.</b> Eighteen screens distinguished loading from empty and stopped there,
 * so a failed fetch rendered the EMPTY state: "you have no invitations" when the truth was "we could not
 * ask". React Query does not throw to the router's error boundary unless a query opts in, so nothing
 * escalated - the page looked calm and said something false.</p>
 *
 * <p>One component and one string pair rather than eighteen: the words a person needs here are the same
 * on every screen, and eighteen variants would be eighteen more strings to translate and keep aligned.
 * `onRetry` is optional because not every caller holds a refetch worth offering.</p>
 */
export function QueryError({ onRetry }: { onRetry?: () => void }) {
  const { t } = useTranslation()
  return (
    <div role="alert" className="flex flex-col items-start gap-2">
      <p style={{ color: 'var(--color-danger-fg)' }}>{t('common.loadFailed')}</p>
      {onRetry ? (
        <Button size="sm" variant="secondary" onClick={onRetry}>
          {t('common.retry')}
        </Button>
      ) : null}
    </div>
  )
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
 * The parts of a `useInfiniteQuery` a list card needs. Structural rather than the library's own type: it
 * keeps this file independent of React Query, and it lets a screen with a plain `useQuery` pass the two
 * fields it does have.
 */
export interface PagedQueryLike {
  isPending: boolean
  isError: boolean
  hasNextPage?: boolean
  isFetchingNextPage?: boolean
  fetchNextPage?: () => unknown
}

/** The words a list card says in each of its four states. */
export interface ListCardLabels {
  loading: string
  error: string
  empty: string
  loadMore?: string
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
  title, query, isEmpty, labels, skeletonRows, children,
}: {
  title: string
  query: PagedQueryLike
  isEmpty: boolean
  labels: ListCardLabels
  skeletonRows?: number
  children: ReactNode
}) {
  return (
    <Card title={title}>
      <ListState
        isPending={query.isPending}
        isError={query.isError}
        isEmpty={isEmpty}
        loadingLabel={labels.loading}
        errorText={labels.error}
        emptyText={labels.empty}
        skeletonRows={skeletonRows}
      >
        {children}
        {query.fetchNextPage && labels.loadMore ? (
          <LoadMore
            hasNextPage={query.hasNextPage ?? false}
            isFetching={query.isFetchingNextPage ?? false}
            onClick={() => query.fetchNextPage?.()}
            label={labels.loadMore}
          />
        ) : null}
      </ListState>
    </Card>
  )
}
