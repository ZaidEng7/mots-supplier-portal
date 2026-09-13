// The three pieces every list screen in this product repeats: a heading, a labelled filter control, and a body that
// is either loading, broken, empty or a table. They were written out longhand on each screen until SCR-402 and
// SCR-307 made the fourth and fifth copy, at which point the duplication stopped being incidental - the same markup
// drifting apart screen by screen is how a heading ends up one size on one page and another size on the next.
//
// Deliberately thin. Each of these holds layout and nothing else: no query, no state, no fetching. A screen still
// decides what it asks for, what it calls its filters, and what its empty case means, because those are the parts
// that differ and the parts a reader needs to see on the screen itself.
//
// THE HEADING carries the screen title and its one-line explanation of what the reader is looking at, the primary
// action beside the title rather than adrift below it, and the facts that identify a particular record - a status
// chip, a reference code, an owner.
//
// Its size is --text-h1, not --text-h2. The audit measured <h1> rendering at THREE different sizes across the
// product for one job - the h1 token on 5 screens, h2 on 45 and h3 on 7 - and this component was itself one of the
// wrong ones, so the six screens already using it were being made consistent with each other and inconsistent with
// the scale. RECONCILIATION.md's shared rule: the page title is --text-h1, one size, once per page, and never a
// smaller heading for the page's own name.
//
// break-words on it is not decoration. A page title is not always prose: the back-office dashboard greets you with
// your own email address, and a supplier's legal name can be one long token in either script. At 24px those fit a
// 320px viewport and at 30px they do not - the reflow guard caught this the moment the size changed, with the
// document scrolling sideways by 47px. Breaking inside a word is the right answer for a heading that may contain
// data.
//
// A FILTER's htmlFor decides which element its caption is: a real <label> when it names a control with an id - a text
// input, say - and a plain <span> otherwise. A <label for> pointing at nothing is worse than no label, because a
// screen reader announces the association and then lands the user nowhere. The free-text filter every list screen has
// is one element, and it always carries a real <label for>: an unlabelled search box is the most common accessibility
// defect in a table toolbar, and a placeholder is not a label, because it disappears the moment somebody types.
//
// THE BODY's four states are kept distinct on purpose. "We could not load this" and "there is nothing here" are
// different facts about the world, and a screen that renders an empty table for a failed request tells the reader
// the second when the first is true. The error takes the thrown value, so a failure can say what the server said, and
// optionally a refetch, offered as the way out; the skeleton takes the shape the content is, because a table skeleton
// over a card list is a promise the screen does not keep, which is the whole point of a skeleton.
//
// The three states that are not the caller's content carry their own padding, because the card around them no longer
// does: a list card holds a table edge to edge, and a skeleton, an error or an empty line pressed against that edge
// reads as broken rather than as flush. The error state DELEGATES rather than rendering its own paragraph, because
// QueryError carries role="alert" and the retry control, and a second, quieter error presentation here meant that
// adopting ListState LOST both.
//
// QUERYERROR is "we could not load this", said once, everywhere. The defect it closes: eighteen screens distinguished
// loading from empty and stopped there, so a failed fetch rendered the EMPTY state - "you have no invitations" when
// the truth was "we could not ask". React Query does not throw to the router's error boundary unless a query opts in,
// so nothing escalated: the page looked calm and said something false. One component and one string pair rather than
// eighteen, because the words a person needs here are the same on every screen and eighteen variants would be
// eighteen more strings to translate and keep aligned. onRetry is optional, because not every caller holds a refetch
// worth offering.
//
// It shows the SERVER's message when there is one, which is the audit's §C5: a reader learned THAT a screen failed
// and never WHY. The why was already on the error - every api module builds its message from the server's RFC 9457
// detail - and the read paths were discarding it in favour of a string written to be a last resort.
// common.loadFailed is still the fallback; it is no longer the whole message.
//
// THE NEXT-PAGE BUTTON renders nothing when there is no next page. It takes isLoading rather than disabled: six
// screens wrote this button by hand and every one of them showed a spinner while the page was in flight, because a
// control that only greys out looks broken on a slow connection, so the shared version keeps the behaviour the
// hand-written ones had. It is padded rather than margined, because it sits below a table that now reaches the card's
// edges and needs the inset the card stopped providing.
//
// THE PAGED-QUERY SHAPE it reads is structural rather than React Query's own type: that keeps this file independent
// of the library, and it lets a screen with a plain useQuery pass the two fields it does have. error and refetch are
// there for the same structural reason, and both optional, so a plain useQuery result still satisfies it.
//
// THE LIST CARD holds one paged list: its title, the four body states and the next-page button. Two screens wrote
// this scaffolding out identically and a third was about to. The parts that differ between list screens are the table
// and the words; the parts that do not are the ones collected here, so a new list screen inherits the paging
// affordance rather than reimplementing it - and forgetting the next-page button is the specific defect MSP-84
// recorded, where a list silently ended at twenty rows.
//
// Its title is optional and usually absent. A list screen's <h1> already names the list, and a card that repeats it
// puts two names for one thing six inches apart, which the Rams audit counted as one of five removable things on the
// tender list alone. A title is passed only when the card holds something the page heading does not describe - a
// second list on the same screen, say - and when it is absent the table inside names itself from its own caption, so
// nothing is lost to a screen reader. See Table.
//
// The action slot is a control that belongs to the whole list rather than to a row - a filter, an "add" button - shown
// beside the title, and passed straight to Card, so a screen adopting this component does not have to keep its own
// Card just to keep its header control. The footnote is a note that belongs to the card rather than to the rows and
// is shown below the list in every state: the children are inside ListState and therefore only exist once the list
// does, and a standing rule about the table - "inactive rows are hidden" - is not a fact about the rows and must not
// vanish with them.
//
// The card is FLUSH, because the one thing a list card holds is a table and a table brings its own edges. The
// template's list screen is a single framed surface; a padded card round a bordered table is two frames a few pixels
// apart, which is what this looked like before. The template also puts a count in this header - "18 tenders" - and
// there is no honest count to put there: these lists are paged, and the envelope's totalCount is null unless the
// request asks for it, which no request does. A header reading "18 tenders" over seven loaded rows would be the
// screen saying something it does not know, so it says nothing.

import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from './Button'
import { Card } from './Card'
import { Input } from './Input'
import { SkeletonList, SkeletonTable } from './Skeleton'
import { errorDetail } from '../../api/problem'


export function PageHeading({ title, subtitle, actions, meta }: Readonly<{
  title: string
  subtitle?: string
  actions?: ReactNode
  meta?: ReactNode
}>) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0">
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

export function FilterBar({ children }: Readonly<{ children: ReactNode }>) {
  return <div className="flex flex-wrap gap-4">{children}</div>
}

export function ListState({
  isPending, isError, isEmpty, loadingLabel, errorText, emptyText, error, onRetry, skeleton = 'table', skeletonRows = 5, children,
}: Readonly<{
  isPending: boolean
  isError: boolean
  isEmpty: boolean
  loadingLabel: string
  errorText: string
  emptyText: string
  error?: unknown
  onRetry?: () => void
  skeleton?: 'table' | 'list'
  skeletonRows?: number
  children: ReactNode
}>) {
  const padded = (node: ReactNode) => <div className="p-4">{node}</div>

  if (isPending) {
    return padded(skeleton === 'list'
      ? <SkeletonList label={loadingLabel} rows={skeletonRows} />
      : <SkeletonTable label={loadingLabel} rows={skeletonRows} />)
  }
  if (isError) return padded(<QueryError error={error} errorText={errorText} onRetry={onRetry} />)
  if (isEmpty) return padded(<p style={{ color: 'var(--color-text-secondary)' }}>{emptyText}</p>)
  return <>{children}</>
}

export function QueryError({ error, errorText, onRetry }: Readonly<{
  error?: unknown
  errorText?: string
  onRetry?: () => void
}>) {
  const { t } = useTranslation()
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
    <div className="p-4">
      <Button variant="secondary" isLoading={isFetching} onClick={onClick}>
        {label}
      </Button>
    </div>
  )
}

export interface PagedQueryLike {
  isPending: boolean
  isError: boolean
  error?: unknown
  hasNextPage?: boolean
  isFetchingNextPage?: boolean
  fetchNextPage?: () => unknown
  refetch?: () => unknown
}

export interface ListCardLabels {
  loading: string
  error: string
  empty: string
  loadMore?: string
}

export function ListCard({
  title, action, query, isEmpty, labels, skeleton, skeletonRows, footer, children,
}: Readonly<{
  title?: string
  action?: ReactNode
  query: PagedQueryLike
  isEmpty: boolean
  labels: ListCardLabels
  skeleton?: 'table' | 'list'
  skeletonRows?: number
  footer?: ReactNode
  children: ReactNode
}>) {
  return (
    <Card flush title={title} action={action}>
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
