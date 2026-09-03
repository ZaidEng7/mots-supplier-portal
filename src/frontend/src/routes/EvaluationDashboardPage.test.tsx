import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import i18n from '../i18n/config'
import { renderPage, mockFetch } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { EvaluationDashboardPage } = await import('./EvaluationDashboardPage')

function assignment(overrides: Record<string, unknown> = {}) {
  return {
    rfqReferenceCode: 'RFQ-2026-000001',
    rfqTitleAr: 'طلب تموين', rfqTitleEn: 'Catering RFQ',
    evaluationState: 'Assigned',
    evaluationTargetDate: '2026-09-30T12:00:00Z',
    assignedAt: '2026-09-01T08:00:00Z',
    submittedAt: null,
    scoresRecorded: 3, scoresExpected: 12,
    tab: 'Assigned',
    ...overrides,
  }
}

describe('EvaluationDashboardPage (SCR-500)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('empty: shows §4\'s "Nothing to evaluate", with no call to action', async () => {
    restore = mockFetch({ '/api/v1/my-evaluations': [] })

    renderPage(<EvaluationDashboardPage />)

    expect(await screen.findByText('Nothing to evaluate')).toBeInTheDocument()
    expect(screen.getByText('Proposals assigned to you for scoring will appear here.')).toBeInTheDocument()
  })

  it('ok: shows the assignment, its progress and a link into the scoring workspace', async () => {
    restore = mockFetch({ '/api/v1/my-evaluations': [assignment()] })

    renderPage(<EvaluationDashboardPage />)

    expect(await screen.findByText('Catering RFQ')).toBeInTheDocument()
    expect(screen.getByText('3 of 12 scored')).toBeInTheDocument()

    // The tender-stopper this screen exists to fix: a navigable path into EPIC-11's workspace.
    expect(screen.getByText('Start scoring')).toBeInTheDocument()
  })

  it('a submitted assignment offers to be viewed rather than scored', async () => {
    // IA §4.3: after EvaluatorSubmitted the workspace is read-only for that evaluator, so the entry
    // point must not invite them to score again.
    restore = mockFetch({
      '/api/v1/my-evaluations': [assignment({ submittedAt: '2026-09-02T10:00:00Z', tab: 'Submitted' })],
    })

    renderPage(<EvaluationDashboardPage />)

    expect(await screen.findByText('View evaluation')).toBeInTheDocument()
    expect(screen.queryByText('Start scoring')).not.toBeInTheDocument()
  })

  it('switching tabs asks the server for that tab', async () => {
    const requested: string[] = []
    const original = globalThis.fetch
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      requested.push(url)
      return new Response(JSON.stringify([]), { status: 200 })
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<EvaluationDashboardPage />)

    await screen.findByText('Nothing to evaluate')
    await userEvent.click(screen.getByRole('tab', { name: 'Submitted' }))

    // The tab is a server-side filter, not a client-side slice: the tab an assignment belongs in is
    // derived from its own submission state, and deriving it twice is how the two disagree.
    expect(requested.some((url) => url.includes('tab=Submitted'))).toBe(true)
  })

  it('renders Eastern Arabic numerals under Arabic', async () => {
    // R-1 covers counts. A progress reading "3 of 12" beside an Eastern-digit date is exactly the
    // inconsistency the ruling was made to prevent.
    const restoreFetch = mockFetch({ '/api/v1/my-evaluations': [assignment()] })
    await i18n.changeLanguage('ar')
    restore = () => { restoreFetch(); void i18n.changeLanguage('en') }

    renderPage(<EvaluationDashboardPage />)

    expect(await screen.findByText(/٣.*١٢/)).toBeInTheDocument()
  })
})
