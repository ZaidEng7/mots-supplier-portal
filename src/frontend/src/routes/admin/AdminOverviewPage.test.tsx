import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen } from '@testing-library/react'
import { mockFetch, renderPage } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { AdminOverviewPage } = await import('./AdminOverviewPage')

const JOBS = ['rfq-auto-close', 'reminder-sweep']

function overview(overrides: Record<string, unknown> = {}) {
  return {
    usersByRole: [{ role: 'procurement_officer', count: 4 }, { role: 'system_admin', count: 1 }],
    totalRoles: 8,
    referenceData: [
      { table: 'categories', active: 12, inactive: 2 },
      { table: 'currencies', active: 3, inactive: 0 },
    ],
    outbox: { pending: 0, failed: 0, oldestPendingAgeMinutes: null },
    jobs: {
      recurringJobsEnabled: true,
      expectedJobs: JOBS,
      registeredJobs: JOBS,
      missingJobs: [],
    },
    auditRowsLast24Hours: 143,
    ...overrides,
  }
}

/** SCR-700. `system_admin` had no landing page at all, so "it renders, and the operational facts are
 * on it" is the first thing worth asserting. */
describe('AdminOverviewPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders the operational KPIs, summing users across roles', async () => {
    restore = mockFetch({ '/api/v1/admin/overview': overview() })

    renderPage(<AdminOverviewPage />)

    expect(await screen.findByText('Platform administration')).toBeInTheDocument()
    expect(screen.getByText('Users')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()   // 4 + 1, not the row count
    expect(screen.getByText('143')).toBeInTheDocument()
    expect(screen.getByText('12 of 14')).toBeInTheDocument()
  })

  it('distinguishes a drained outbox from a stuck one', async () => {
    restore = mockFetch({ '/api/v1/admin/overview': overview() })

    renderPage(<AdminOverviewPage />)

    // Nothing pending: the age must not read as "0 minutes old", which would look like a message that
    // arrived this instant.
    expect(await screen.findByText('Nothing pending')).toBeInTheDocument()
    expect(screen.queryByText('Failed messages need attention')).not.toBeInTheDocument()
  })

  it('names the age and warns when the outbox is backed up', async () => {
    // The control for the test above.
    restore = mockFetch({
      '/api/v1/admin/overview': overview({
        outbox: { pending: 6, failed: 2, oldestPendingAgeMinutes: 180 },
      }),
    })

    renderPage(<AdminOverviewPage />)

    expect(await screen.findByText('180 min')).toBeInTheDocument()
    expect(screen.getByText('Failed messages need attention')).toBeInTheDocument()
  })

  it('surfaces recurring jobs being switched off, which today is only a startup log line', async () => {
    restore = mockFetch({
      '/api/v1/admin/overview': overview({
        jobs: { recurringJobsEnabled: false, expectedJobs: JOBS, registeredJobs: [], missingJobs: JOBS },
      }),
    })

    renderPage(<AdminOverviewPage />)

    expect(await screen.findByText('Recurring jobs are disabled')).toBeInTheDocument()
    expect(screen.getByText(/RFQs will not close automatically/)).toBeInTheDocument()
    // The flag being off explains ALL the missing ids, so it must not also cry "jobs missing".
    expect(screen.queryByText('Jobs missing from the schedule')).not.toBeInTheDocument()
  })

  it('names the missing ids when jobs are enabled but a job never registered', async () => {
    restore = mockFetch({
      '/api/v1/admin/overview': overview({
        jobs: {
          recurringJobsEnabled: true,
          expectedJobs: JOBS,
          registeredJobs: ['rfq-auto-close'],
          missingJobs: ['reminder-sweep'],
        },
      }),
    })

    renderPage(<AdminOverviewPage />)

    expect(await screen.findByText('Jobs missing from the schedule')).toBeInTheDocument()
    // Untranslated on purpose: the operator compares it against the deployment.
    expect(screen.getByText('reminder-sweep')).toBeInTheDocument()
  })

  it('warns when a reference table has no active codes', async () => {
    // A table at zero active codes blocks registration, and nothing else in the product says so.
    restore = mockFetch({
      '/api/v1/admin/overview': overview({
        referenceData: [{ table: 'currencies', active: 0, inactive: 3 }],
      }),
    })

    renderPage(<AdminOverviewPage />)

    expect(await screen.findByText(/registration will fail/)).toBeInTheDocument()
  })

  it('offers a retry instead of a blank page when the read fails', async () => {
    restore = mockFetch({ '/api/v1/admin/overview': { __status: 500 } })

    renderPage(<AdminOverviewPage />)

    expect(await screen.findByText('Could not load platform administration')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})
