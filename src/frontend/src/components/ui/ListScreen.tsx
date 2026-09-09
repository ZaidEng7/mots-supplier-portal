import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from './Button'
import { Card } from './Card'
import { Input } from './Input'
import { SkeletonList, SkeletonTable } from './Skeleton'
import { errorDetail } from '../../api/problem'

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
export function PageHeading({ title, subtitle, actions, meta }: Readonly<{
  title: string
  subtitle?: string
  /** The primary action for this screen, beside the title rather than adrift below it. */
  actions?: ReactNode
  /** A status chip, a reference code, an owner - the facts that identify this particular record. */
  meta?: ReactNode
}>) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0">
        {/*
          `--text-h1`, not `--text-h2`. The audit measured `<h1>` rendering at THREE different sizes
          across the product for one job - the h1 token on 5 screens, h2 on 45 and h3 on 7 - and this
          component was itself one of the wrong ones, so the six screens already using it were being
          made consistent with each other and inconsistent with the scale.

          RECONCILIATION.md's shared rule: page title is `--text-h1`, one size, once per page. Never a
          smaller heading for the page's own name.
        */}
        {/*
          `break-words` is not decoration. A page title is not always prose: the back-office dashboard
          greets you with your own email address, and a supplier's legal name can be one long token in
          either script. At 24px those fit a 320px viewport and at 30px they do not - the reflow guard
          caught this the moment the size changed, with the document scrolling sideways by 47px.
          Breaking inside a word is the right answer for a heading that may contain data.
        */}
        <h1 className="break-words text-[length:var(--text-h1)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {title}
        </h1>
        {subtitle ? (
          <p className="mt-1 break-words text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {subtitle}
          </p>
        ) : null}
        {meta ? <div className="mt-2 flex flex-wrap items-center gap-2">{meta}</div> : null}
      </div>
      {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
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
export function FilterField({ label, htmlFor, children }: Readonly<{ label: string; htmlFor?: string; children: ReactNode }>) {
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
}: Readonly<{
  id: string
  label: string
  placeholder?: string
  value: string
  onChange: (value: string) => void
}>) {
  return (
    <FilterField label={label} htmlFor={id}>
      <Input id={id} value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} />
    </FilterField>
  )
}

/** The row of filters above a list. */
export function FilterBar({ children }: Readonly<{ children: ReactNode }>) {
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
  isPending, isError, isEmpty, loadingLabel, errorText, emptyText, error, onRetry, skeleton = 'table', skeletonRows = 5, children,
}: Readonly<{
  isPending: boolean
  isError: boolean
  isEmpty: boolean
  loadingLabel: string
  errorText: string
  emptyText: string
  /** The thrown value, so a failure can say what the server said. Optional: a caller that does not
   * hold it still gets `errorText`. */
  error?: unknown
  /** Offered as the way out of the failure. Optional, because not every caller holds a refetch. */
  onRetry?: () => void
  /** What the content is shaped like, so the placeholder is shaped like it too. A table skeleton over a
   * card list is a promise the screen does not keep, which is the whole point of a skeleton. */
  skeleton?: 'table' | 'list'
  skeletonRows?: number
  children: ReactNode
}>) {
  if (isPending) {
    return skeleton === 'list'
      ? <SkeletonList label={loadingLabel} rows={skeletonRows} />
      : <SkeletonTable label={loadingLabel} rows={skeletonRows} />
  }
  // Delegates rather than rendering its own paragraph. QueryError carries role="alert" and the retry
  // control; a second, quieter error presentation here meant that adopting ListState LOST both.
  if (isError) return <QueryError error={error} errorText={errorText} onRetry={onRetry} />
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
export function QueryError({ error, errorText, onRetry }: Readonly<{
  error?: unknown
  /** A caller's own fallback wording. Without one this uses the shared `common.loadFailed`. */
  errorText?: string
  onRetry?: () => void
}>) {
  const { t } = useTranslation()
  // The audit's §C5: a reader learned THAT a screen failed and never WHY. The why was already on the
  // error - every api module builds its message from the server's RFC 9457 `detail` - and the read
  // paths were discarding it in favour of a string written to be a last resort. `common.loadFailed`
  // is still the fallback; it is no longer the whole message.
  const detail = errorDetail(error)
  return (
    <div role="alert" className="flex flex-col items-start gap-2">
      <p style={{ color: 'var(--color-danger-fg)' }}>{detail ?? errorText ?? t('common.loadFailed')}</p>
      {onRetry ? (
        <Button size="sm" variant="secondary" onClick={onRetry}>
          {t('common.retry')}
        </Button>
      ) : null}
    </div>
  )
}

/**
 * The next page of a keyset-paged list. Renders nothing when there is no next page.
 *
 * <p>`isLoading` rather than `disabled`: six screens wrote this button by hand and every one of them
 * showed a spinner while the page was in flight, because a control that only greys out looks broken on a
 * slow connection. The shared version keeps the behaviour the hand-written ones had.</p>
 */
export function LoadMore({
  hasNextPage, isFetching, onClick, label,
}: Readonly<{
  hasNextPage: boolean
  isFetching: boolean
  onClick: () => void
  label: string
}>) {
  if (!hasNextPage) return null
  return (
    <div className="mt-4">
      <Button variant="secondary" isLoading={isFetching} onClick={onClick}>
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
  /** React Query's own `error`. Structural like the rest of this interface, and optional so a screen
   * holding a plain `useQuery` result still satisfies it. */
  error?: unknown
  hasNextPage?: boolean
  isFetchingNextPage?: boolean
  fetchNextPage?: () => unknown
  /** React Query's own `refetch`, so a failed card offers the way out of the failure rather than only
   * naming it. Optional for the same structural reason as the rest of this interface. */
  refetch?: () => unknown
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
  title, action, query, isEmpty, labels, skeleton, skeletonRows, footer, children,
}: Readonly<{
  title: string
  /** A control that belongs to the whole list rather than to a row - a filter, an "add" button - shown
   * beside the title. Passed straight to `Card`, so a screen adopting this component does not have to
   * keep its own `Card` just to keep its header control. */
  action?: ReactNode
  query: PagedQueryLike
  isEmpty: boolean
  labels: ListCardLabels
  /** Which shape the placeholder takes while the first page loads. A card holding rows of text rather
   * than a table asks for `'list'`, the same choice `ListState` offers. */
  skeleton?: 'table' | 'list'
  skeletonRows?: number
  /** A note that belongs to the card rather than to the rows, shown below the list in every state. The
   * children are inside `ListState` and therefore only exist once the list does; a standing rule about
   * the table - "inactive rows are hidden" - is not a fact about the rows and must not vanish with
   * them. */
  footer?: ReactNode
  children: ReactNode
}>) {
  return (
    <Card title={title} action={action}>
      <ListState
        isPending={query.isPending}
        isError={query.isError}
        isEmpty={isEmpty}
        loadingLabel={labels.loading}
        errorText={labels.error}
        emptyText={labels.empty}
        error={query.error}
        onRetry={query.refetch ? () => void query.refetch?.() : undefined}
        skeleton={skeleton}
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
      {footer}
    </Card>
  )
}
