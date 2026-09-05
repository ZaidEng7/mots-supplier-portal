import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage, type RecordedRequest } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { AuditExplorerPage } = await import('./AuditExplorerPage')

const ROW = {
  id: 'a-1',
  occurredAt: '2026-09-01T10:00:00Z',
  aggregateType: 'Rfq',
  aggregateId: '01a00000-0000-7000-8000-000000000001',
  action: 'rfq_reassigned',
  fromState: null,
  toState: null,
  actorLabel: 'A Manager',
}

function page(rows: unknown[], overrides: Record<string, unknown> = {}) {
  return {
    data: rows,
    pagination: { hasMore: false, nextCursor: null, totalCount: null },
    meta: { filtersApplied: null },
    ...overrides,
  }
}

/** SCR-720. Three audit endpoints existed and no screen called any of them. */
describe('AuditExplorerPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('names the actor, and says "System" rather than leaving it blank', async () => {
    restore = mockFetch({
      '/api/v1/audit': page([ROW, { ...ROW, id: 'a-2', actorLabel: null, fromState: 'Draft', toState: 'InternalReview' }]),
    })

    renderPage(<AuditExplorerPage />)

    expect(await screen.findByText('A Manager')).toBeInTheDocument()
    // "Who did this" is the first question asked of an audit row, so a system action says so.
    expect(screen.getByText('System')).toBeInTheDocument()
    // And the transition renders as one, with the states the row actually carries.
    expect(screen.getByText('Draft → InternalReview')).toBeInTheDocument()
  })

  it('sends the filters to the server instead of narrowing the page it already has', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/audit': page([ROW]) }, recorded)

    renderPage(<AuditExplorerPage />)
    await screen.findByText('rfq_reassigned')

    await userEvent.type(screen.getByLabelText('Record type'), 'Rfq')
    await userEvent.type(screen.getByLabelText('Action'), 'rfq_reassigned')
    await userEvent.click(screen.getByRole('button', { name: 'Search' }))

    // Filtering in the browser would show only the current page's matches and read as the filter
    // working - the same silent narrowing the server's 422 refusal exists to prevent.
    await waitFor(() => {
      expect(recorded.some((r) => r.url.includes('aggregateType=Rfq') && r.url.includes('action=rfq_reassigned'))).toBe(true)
    })
    // The control: the first request carried no filters at all.
    expect(recorded[0].url).not.toContain('aggregateType')
  })

  it('shows a refused filter value against the field the server named', async () => {
    restore = mockFetch({
      '/api/v1/audit': {
        __status: 422,
        code: 'INVALID_FILTER_VALUE',
        detail: "'not-a-guid' is not a value the 'actorUserId' filter accepts.",
        errors: [{ field: 'actorUserId' }],
      },
    })

    renderPage(<AuditExplorerPage />)

    // Against the field, not as a page-level failure: a compliance officer with six filter boxes needs
    // to know which one to fix.
    expect(await screen.findByText("'not-a-guid' is not a value the 'actorUserId' filter accepts."))
      .toBeInTheDocument()
    expect(screen.queryByText('Could not load the audit log')).not.toBeInTheDocument()
  })

  it('falls back to a page-level failure when the server names no field', async () => {
    // The control for the test above: a 500 has no field to point at, and must not be rendered as one
    // filter's problem.
    restore = mockFetch({ '/api/v1/audit': { __status: 500 } })

    renderPage(<AuditExplorerPage />)

    expect(await screen.findByText('Could not load the audit log')).toBeInTheDocument()
  })

  it('tells an empty filtered search apart from an empty log', async () => {
    restore = mockFetch({ '/api/v1/audit': page([]) })

    renderPage(<AuditExplorerPage />)

    expect(await screen.findByText('No audit rows')).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Action'), 'nothing_matches_this')
    await userEvent.click(screen.getByRole('button', { name: 'Search' }))

    // "No audit rows" under a filter would tell an administrator the platform has recorded nothing.
    expect(await screen.findByText('No audit rows match these filters')).toBeInTheDocument()
  })

  it('echoes back the filters the server says it applied', async () => {
    restore = mockFetch({
      '/api/v1/audit': page([ROW], { meta: { filtersApplied: ['aggregateType=Rfq'] } }),
    })

    renderPage(<AuditExplorerPage />)

    // From meta, not from local state: what the server applied and what the boxes contain can differ,
    // and the first is the one that produced these rows.
    expect(await screen.findByText('Filters applied: aggregateType=Rfq')).toBeInTheDocument()
  })
})
