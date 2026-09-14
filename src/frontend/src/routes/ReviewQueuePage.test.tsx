// Three groups: the failure branch, the age badge, and F-6's state filter.
//
// THE FAILURE says the fetch failed instead of saying the queue is empty. The defect this pins: React Query does not
// throw to the router's error boundary, so a failed fetch left `data` undefined and this screen rendered "nothing waiting
// for you" - the one thing a reviewer must not be told wrongly, because they act on it by going away.
//
// THE AGE BADGE is FEAT-03.6 and FR-ONB-012's, and it had zero test coverage before this: the backend sourcing of
// EnteredQueueAt is tested in ReviewQueuePaginationTests.cs, but the tone thresholds and the Arabic and English
// formatting that turn hours into what a reviewer actually reads were not. The tone is success below the at-risk
// threshold, warning at and above it and below overdue, and danger at and above overdue. The wording renders whole hours
// below one day in both locales, renders zero hours as 0h or ٠ ساعة rather than a negative or NaN, clamps a negative
// input from clock skew to zero rather than printing a negative age, and switches to whole days at 24h in both locales.
//
// THE STATE FILTER is F-6: a reviewer can reach an application they have already decided. The queue lists the three
// states awaiting a decision, which is what it is for, and a decided application dropped out of it with no other list
// carrying it - so the only route back to a decision a reviewer had made was typing the supplier's reference code into
// the address bar. The test offers the decided states and labels the default as what it actually is.

import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, expectRetryableFailure, type RecordedRequest } from '../test/renderPage'
import { AT_RISK_HOURS, OVERDUE_HOURS, ReviewQueuePage, ageTone, formatAge } from './ReviewQueuePage'

describe('ReviewQueuePage, when the queue cannot be loaded', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('says the fetch failed instead of saying the queue is empty', async () => {
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
    expect(screen.getByRole('option', { name: 'Awaiting a decision' })).toBeInTheDocument()
  })

  it('shows a retryable failure rather than an empty screen', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/review/queue': { __status: 500 } }, recorded)

    renderPage(<ReviewQueuePage />)

    await expectRetryableFailure('/api/v1/review/queue', recorded)
  })
})
