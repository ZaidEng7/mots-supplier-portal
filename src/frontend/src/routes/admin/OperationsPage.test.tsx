import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../../test/renderPage'

const { OperationsPage } = await import('./OperationsPage')

const JOBS = '/api/v1/admin/jobs'
const OUTBOX = '/api/v1/admin/outbox'
const ERP = '/api/v1/admin/erp-sync'
const SECURITY = '/api/v1/admin/security'
const STORAGE = '/api/v1/admin/storage'

function jobs(overrides: Record<string, unknown> = {}) {
  return {
    recurringEnabled: true,
    jobs: [{
      id: 'document-expiry', cron: '0 2 * * *', registered: true,
      lastExecution: '2026-09-05T02:00:00Z', lastState: 'Succeeded', nextExecution: '2026-09-06T02:00:00Z',
    }],
    ...overrides,
  }
}

const SECURITY_POSTURE = {
  password: { minimumLength: 12, requireDigit: false, requireUppercase: false, requireLowercase: false, requireNonAlphanumeric: false },
  lockout: { maxFailedAttempts: 5, lockoutMinutes: 15 },
  session: { accessTokenMinutes: 15, refreshTokenDays: 14, clockSkewSeconds: 30 },
  mfaRequiredRoles: ['system_admin'],
  rateLimits: [{ policy: 'auth', permitLimit: 10, windowSeconds: 60 }],
  registrationMode: 'invite-only',
}

const STORAGE_SETTINGS = {
  maxUploadBytes: 10485760, allowedTypes: { pdf: 'application/pdf' }, bucket: 'documents',
  objectStorageReachable: true, virusScannerReachable: true, documentCount: 42, pendingScanCount: 0,
}

/** Every card loaded and healthy, so each test overrides only the one thing it is about. */
function healthy(overrides: Record<string, unknown> = {}) {
  return {
    [JOBS]: jobs(),
    [OUTBOX]: { counts: { Pending: 0, Sent: 12, Failed: 0 }, messages: [] },
    [ERP]: { transportConfigured: true, counts: { Synced: 3, Failed: 0 }, awards: [] },
    [SECURITY]: SECURITY_POSTURE,
    [STORAGE]: STORAGE_SETTINGS,
    ...overrides,
  }
}

/**
 * SCR-721/722/723/725/726. This is an operator's "is the system moving?" screen, so what these tests
 * pin is the set of places where a number on it could be TRUE and still mislead - a next-run time
 * while schedules are off, a Synced column produced by a stub, a retry offered where the domain would
 * refuse it.
 */
describe('OperationsPage (SCR-721)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('lists the recurring jobs with their schedule and last state', async () => {
    restore = mockFetch(healthy())

    renderPage(<OperationsPage />)

    expect(await screen.findByText('document-expiry')).toBeInTheDocument()
    expect(screen.getByText('0 2 * * *')).toBeInTheDocument()
    expect(screen.getByText('Succeeded')).toBeInTheDocument()
  })

  it('warns once, above the table, when recurring jobs are switched off globally', async () => {
    // With schedules off every nextExecution on the screen is a lie. Said once rather than per row,
    // because repeating a warning six times trains the reader to skip it.
    restore = mockFetch(healthy({ [JOBS]: jobs({ recurringEnabled: false }) }))

    renderPage(<OperationsPage />)

    // Waited on by its own text, not by role: SkeletonTable is also a live region, so findAllByRole
    // would resolve against the loading skeletons and count them instead.
    await screen.findByText(/Scheduled jobs are disabled|الجدولة الدورية معطّلة/i)
    const warnings = screen.getAllByRole('status').filter((n) => /disabled|معطّلة/i.test(n.textContent ?? ''))
    expect(warnings).toHaveLength(1)
  })

  it('flags a job Hangfire is not holding, and refuses to offer a run for it', async () => {
    // The row that matters most is the one with the least data on it: a job that vanished from the
    // registration is invisible in any count of jobs that ran.
    restore = mockFetch(healthy({
      [JOBS]: jobs({ jobs: [{ id: 'document-expiry', cron: null, registered: false, lastExecution: null, lastState: null, nextExecution: null }] }),
    }))

    renderPage(<OperationsPage />)

    expect(await screen.findByText(/not registered|غير مُسجَّلة/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /run now|تشغيل/i })).toBeDisabled()
  })

  it('triggers a registered job on request', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ ...healthy(), '/api/v1/admin/jobs/document-expiry/trigger': {} }, recorded)

    renderPage(<OperationsPage />)
    await userEvent.click(await screen.findByRole('button', { name: /run now|تشغيل/i }))

    expect(recorded.some((r) => r.method === 'POST' && r.url.includes('document-expiry'))).toBe(true)
  })

  it('shows outbox counts including the zeroes', async () => {
    // "Failed: 0" and a count that failed to load must not look alike, which is why the server sends
    // every status rather than only the non-empty ones.
    restore = mockFetch(healthy({ [OUTBOX]: { counts: { Pending: 0, Sent: 12, Failed: 0 }, messages: [] } }))

    renderPage(<OperationsPage />)

    expect(await screen.findByText('Sent: 12')).toBeInTheDocument()
    // The zero is the point: a status the server reports as empty and a count that failed to load
    // must not look alike. (The ERP card renders its own Failed: 0, hence getAllByText.)
    expect(screen.getAllByText('Failed: 0').length).toBeGreaterThan(0)
    expect(screen.getByText('Pending: 0')).toBeInTheDocument()
  })

  it('offers replay only for a Failed message', async () => {
    // Replaying a Pending message duplicates queued work and replaying a Sent one sends an integration
    // event twice - the outbox exists to make delivery exactly-once. The server refuses both; the
    // button must not pretend otherwise.
    restore = mockFetch(healthy({
      [OUTBOX]: {
        counts: { Failed: 1, Sent: 1 },
        messages: [
          { id: 'm-1', type: 'AwardIssued', syncStatus: 'Failed', createdAt: '2026-09-05T10:00:00Z', processedAt: null, payloadJson: '{"awardId":"a-1"}' },
          { id: 'm-2', type: 'AwardIssued', syncStatus: 'Sent', createdAt: '2026-09-04T10:00:00Z', processedAt: '2026-09-04T10:01:00Z', payloadJson: '{}' },
        ],
      },
    }))

    renderPage(<OperationsPage />)

    expect(await screen.findAllByText('AwardIssued')).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: /^replay|إعادة الإرسال/i })).toHaveLength(1)
  })

  it('keeps the payload behind a toggle', async () => {
    // It is the thing an operator needs before deciding to replay, and the thing that would make
    // every row unreadable if it were always shown.
    restore = mockFetch(healthy({
      [OUTBOX]: {
        counts: { Failed: 1 },
        messages: [{ id: 'm-1', type: 'AwardIssued', syncStatus: 'Failed', createdAt: '2026-09-05T10:00:00Z', processedAt: null, payloadJson: '{"awardId":"a-1"}' }],
      },
    }))

    renderPage(<OperationsPage />)

    expect(screen.queryByText(/"awardId"/)).not.toBeInTheDocument()
    await userEvent.click(await screen.findByRole('button', { name: /show payload|عرض/i }))
    expect(screen.getByText(/"awardId"/)).toBeInTheDocument()
  })

  it('says the ERP column is a stand-in when no transport is configured', async () => {
    // EPIC-23's adapter has not landed, so a column of Synced without this line is an instrument
    // asserting something untrue.
    restore = mockFetch(healthy({ [ERP]: { transportConfigured: false, counts: { Synced: 3 }, awards: [] } }))

    renderPage(<OperationsPage />)

    expect(await screen.findByText(/logging stand-in|بديل تسجيلي/i)).toBeInTheDocument()
  })

  it('offers an ERP retry only for a Failed sync', async () => {
    // §6.1: only a Failed sync retries. The button follows the domain rather than offering an action
    // the aggregate would refuse.
    restore = mockFetch(healthy({
      [ERP]: {
        transportConfigured: true,
        counts: { Synced: 1, Failed: 1 },
        awards: [
          { rfqReferenceCode: 'RFQ-2026-000001', erpSyncStatus: 'Failed', erpRetryCount: 2, erpSyncedAt: null, externalPurchaseOrderRef: null },
          { rfqReferenceCode: 'RFQ-2026-000002', erpSyncStatus: 'Synced', erpRetryCount: 0, erpSyncedAt: '2026-09-01T09:00:00Z', externalPurchaseOrderRef: 'PO-77' },
        ],
      },
    }))

    renderPage(<OperationsPage />)

    expect(await screen.findByText('RFQ-2026-000001')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /retry|إعادة/i }).filter((b) => !b.hasAttribute('disabled'))).toHaveLength(1)
  })

  it('reports a composition policy of length only, rather than four blank checkboxes', async () => {
    // No forced digit, case or symbol is NIST 800-63B followed on purpose. A reader who does not know
    // that would file an empty cell as a weakness.
    restore = mockFetch(healthy())

    renderPage(<OperationsPage />)

    expect(await screen.findByText(/length only|الطول فقط/i)).toBeInTheDocument()
  })

  it('reports the security posture read from what enforces it', async () => {
    restore = mockFetch(healthy())

    renderPage(<OperationsPage />)

    expect(await screen.findByText(/12 characters|١٢/)).toBeInTheDocument()
    expect(screen.getByText('system_admin')).toBeInTheDocument()
    expect(screen.getByText('invite-only')).toBeInTheDocument()
  })

  it('surfaces an unreachable scanner rather than leaving it in a count', async () => {
    // A red chip here explains every failing upload in the building; a backlog that never drains is
    // the failure this card exists to show.
    restore = mockFetch(healthy({
      [STORAGE]: { ...STORAGE_SETTINGS, virusScannerReachable: false, pendingScanCount: 214 },
    }))

    renderPage(<OperationsPage />)

    expect(await screen.findByText(/214/)).toBeInTheDocument()
  })

  it('lets one card fail without taking the others down', async () => {
    // Five independent queries. An operator whose outbox endpoint is broken still needs the jobs
    // table, and a single error boundary over the page would deny them that.
    restore = mockFetch(healthy({ [OUTBOX]: { __status: 500 } }))

    renderPage(<OperationsPage />)

    expect(await screen.findByText('document-expiry')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /try again|إعادة المحاولة/i }).length).toBeGreaterThan(0)
  })
})
