import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, expectRetryableFailure, type RecordedRequest } from '../test/renderPage'
import { AT_RISK_HOURS, OVERDUE_HOURS, ReviewQueuePage, ageTone, formatAge } from './ReviewQueuePage'

/** FEAT-03.6/FR-ONB-012: the age-badge logic had zero test coverage before this - the backend
 * sourcing (EnteredQueueAt) is tested in ReviewQueuePaginationTests.cs, but the tone thresholds
 * and the AR/EN formatting that turn hours into what a reviewer actually reads were not. */
describe('ReviewQueuePage, when the queue cannot be loaded', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('says the fetch failed instead of saying the queue is empty', async () => {
    // The defect this pins: React Query does not throw to the router's error boundary, so a failed
    // fetch left `data` undefined and this screen rendered "nothing waiting for you" - the one thing a
    // reviewer must not be told wrongly, because they act on it by going away.
    restore = mockFetch({ '/api/v1/review/queue': { __status: 500 } })

    renderPage(<ReviewQueuePage />)

    expect(await screen.findByRole('alert')).toHaveTextContent('We could not load this. Try again.')
    expect(screen.queryByText('No applications awaiting review')).not.toBeInTheDocument()
  })
})

describe('ageTone', () => {
  it('is success below the at-risk threshold', () => {
    expect(ageTone(0)).toBe('success')
    expect(ageTone(AT_RISK_HOURS - 1)).toBe('success')
  })

  it('is warning at and above the at-risk threshold, below overdue', () => {
    expect(ageTone(AT_RISK_HOURS)).toBe('warning')
    expect(ageTone(OVERDUE_HOURS - 1)).toBe('warning')
  })

  it('is danger at and above the overdue threshold', () => {
    expect(ageTone(OVERDUE_HOURS)).toBe('danger')
    expect(ageTone(OVERDUE_HOURS + 1000)).toBe('danger')
  })
})

describe('formatAge', () => {
  it('renders whole hours below one day, in both locales', () => {
    expect(formatAge(5.9, false)).toBe('5h')
    expect(formatAge(5.9, true)).toBe('5 ساعة')
  })

  it('renders zero hours as 0h/0 ساعة rather than a negative or NaN value', () => {
    expect(formatAge(0, false)).toBe('0h')
    expect(formatAge(0, true)).toBe('0 ساعة')
  })

  it('clamps a negative input (clock skew) to zero rather than printing a negative age', () => {
    expect(formatAge(-2, false)).toBe('0h')
  })

  it('switches to whole days at 24h, in both locales', () => {
    expect(formatAge(24, false)).toBe('1d')
    expect(formatAge(24, true)).toBe('1 يوم')
    expect(formatAge(47.9, false)).toBe('1d')
    expect(formatAge(240, false)).toBe('10d')
  })
})


/**
 * F-6: a reviewer can reach an application they have already decided.
 *
 * <p>The queue lists the three states awaiting a decision, which is what it is for. A decided
 * application dropped out of it and no other list carried it, so the only route back to a decision a
 * reviewer had made was typing the supplier's reference code into the address bar.</p>
 */
describe('ReviewQueuePage state filter', () => {
  let restore: (() => void) | undefined
  afterEach(() => restore?.())

  it('offers the decided states, and labels the default as what it actually is', async () => {
    restore = mockFetch({
      '/api/v1/review/queue': { data: [], pagination: { mode: 'cursor', nextCursor: null, prevCursor: null, pageSize: 20, totalCount: null, hasMore: false, page: null }, meta: { sort: 'createdAt', filtersApplied: null } },
    })

    renderPage(<ReviewQueuePage />)

    await userEvent.click(await screen.findByRole('combobox', { name: /state/i }))

    expect(await screen.findByRole('option', { name: 'Approved' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Rejected' })).toBeInTheDocument()
    // The default option said "All" and meant "the three that need a decision", which is not all.
    expect(screen.getByRole('option', { name: 'Awaiting a decision' })).toBeInTheDocument()
  })

  it('shows a retryable failure rather than an empty screen', async () => {
    // Two halves that nothing asserted before: that the error branch RENDERS, and that the control
    // inside it does anything. `asyncStateCoverage` proves the branch exists in the source; a retry
    // button wired to nothing looks identical to one that works.
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/review/queue': { __status: 500 } }, recorded)

    renderPage(<ReviewQueuePage />)

    await expectRetryableFailure('/api/v1/review/queue', recorded)
  })
})
