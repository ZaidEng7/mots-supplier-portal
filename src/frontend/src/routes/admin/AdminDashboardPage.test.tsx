// The administrator's dashboard, rendered against the server's own shapes.
//
// Each case is written against the wrong page it rules out. A page that rendered a hidden section as an empty card, or
// a failed one as zeroes, or dropped every section when one failed, fails the section cases. A page that showed "all
// clear" while a check could not run fails the not-run case, because that is the one moment "all clear" would be a
// claim the page had not checked. Scan again must not be offered with nothing stuck, and when pressed must say which
// documents could not go back to the scanner. The storage and scanner check must be asked for, never made on opening
// the page, which is why the first case also asserts no probe request was sent. The refresh case counts requests, so
// a page that never refetched, or refetched on every render, fails it.

import { describe, expect, it, vi, afterEach } from 'vitest'
import { act, fireEvent, screen, within } from '@testing-library/react'
import { expectRetryableFailure, mockFetch, renderPage, type RecordedRequest } from '../../test/renderPage'
import i18n from '../../i18n/config'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { AdminDashboardPage } = await import('./AdminDashboardPage')

const ok = <T,>(data: T) => ({ status: 'ok', data })
const hidden = { status: 'hidden', data: null }
const failed = { status: 'failed', data: null }

function account(overrides: Record<string, number> = {}) {
  return {
    active: 9, inactive: 1, activeWithARole: 9, activeSessions: 4, peopleWithActiveSessions: 3,
    pendingInvitations: 1, lockedOut: 0, cannotSignIn: 0, ...overrides,
  }
}

function dashboard(overrides: Record<string, unknown> = {}) {
  return {
    generatedAt: '2026-10-03T11:32:00Z',
    systemHealth: ok({
      jobs: { recurringEnabled: true, jobs: [
        { id: 'outbox-dispatch', verdict: 'ok', lateAfterMinutes: 15, lastState: 'Succeeded', lastExecution: '2026-10-03T11:30:00Z', nextExecution: null, link: '/back-office/operations' },
        { id: 'erp-supplier-sync', verdict: 'late', lateAfterMinutes: 180, lastState: 'Succeeded', lastExecution: '2026-10-03T07:00:00Z', nextExecution: null, link: '/back-office/operations' },
      ] },
      queue: { enqueued: 2, processing: 0, retrying: 1, failed: 0, liveServers: 1, heartbeatWithinMinutes: 5 },
      email: { failedInWindow: 0, retrying: 0, windowDays: 7 },
      outbox: { pending: 0, stuck: 0, stuckAfterMinutes: 15, failed: 0, oldestPendingAt: null },
      scans: { stuck: 3, stuckAfterMinutes: 15 },
      pendingMigrations: [],
      referenceLists: [{ table: 'categories', active: 12, inactive: 2 }, { table: 'incoterms', active: 0, inactive: 11 }],
      purchaseOrderTransport: { configured: false, failedSends: 0 },
      objectStorage: { reachable: true },
    }),
    erp: ok({
      connection: ok({ source: 'Database', enabled: true, host: 'erp.example.test', https: false, lastTestedAt: '2026-10-03T08:28:00Z', lastTestSucceeded: true }),
      sync: ok({ lastRunAt: '2026-10-03T11:00:00Z', outcome: 'Succeeded', trigger: 'Scheduled', counts: { erpSuppliers: 110, created: 0, updated: 110, suspended: 0, refused: 0, failed: 0 }, stale: false, unfinishedRunStartedAt: null, enabled: true }),
      push: ok({ switchOn: false, defaultGroup: 'All Supplier Groups', hostOnWriteHosts: false, waiting: 0, failed: 0, stalled: null, referenceCodes: [] }),
    }),
    peopleAndAccess: ok({
      staff: account({ lockedOut: 1 }),
      suppliers: account({ active: 104, inactive: 12 }),
      activeUsersByRole: [{ role: 'system_admin', activeUsers: 1 }, { role: 'procurement_officer', activeUsers: 3 }],
      staffInvitedNeverSignedIn: { linkStillValid: 1, linkExpired: 1, noLinkYet: 0, linkUsed: 0 },
      twoFactorRequiredRoles: ['system_admin'],
      supplierLoginsOnPlaceholderAddresses: 95,
      organisationsByType: [{ type: 'Ministry', active: 1, inactive: 0 }],
    }),
    security: ok({
      countedSince: '2026-10-02T00:00:00Z',
      events: [{ action: 'login_failed', last24Hours: 3, last7Days: 11, spiking: true }],
      sensitiveChanges: [{ id: 'c1', occurredAt: '2026-10-03T08:28:00Z', action: 'IntegrationConnectionUpdated', actorKind: 'User', actorName: 'Hala Admin', aggregateType: 'IntegrationConnection', referenceCode: null }],
    }),
    recentActivity: ok({
      last24Hours: 46,
      systemLast24Hours: 24,
      latest: [{ id: 'a1', occurredAt: '2026-10-03T11:21:00Z', action: 'staff_invited', actorKind: 'User', actorName: 'Hala Admin', aggregateType: 'User', referenceCode: 'SUP-2026-000116' }],
    }),
    needsAttention: ok({
      allClear: false,
      items: [
        { key: 'scans_stuck', count: 3, link: '/back-office/operations', references: [] },
        { key: 'erp_push_failed_or_stalled', count: 2, link: '/back-office/integrations', references: [{ code: 'SUP-2026-000009', link: '/back-office/review/SUP-2026-000009' }, { code: 'SUP-2026-000011', link: null }] },
      ],
      checksNotRun: [],
    }),
    ...overrides,
  }
}

const META = { version: '1.0.0', commit: '81f05ee4c0ffee', maintenance: null }

function routes(body: unknown, extra: Record<string, unknown> = {}) {
  return { '/api/v1/admin/dashboard': body, '/api/v1/meta': META, ...extra }
}

describe('AdminDashboardPage', () => {
  let restore: () => void
  afterEach(async () => {
    restore?.()
    vi.useRealTimers()
    await i18n.changeLanguage('en')
  })

  it('shows every visible section, when it was counted and the version, and asks nothing of the storage on opening', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch(routes(dashboard()), recorded)

    renderPage(<AdminDashboardPage />)

    expect(await screen.findByText('Needs attention')).toBeInTheDocument()
    for (const heading of ['System health', 'ERP', 'People and access', 'Security', 'Recent activity']) {
      expect(screen.getAllByText(heading).length).toBeGreaterThan(0)
    }
    expect(screen.getByText(/^Updated /)).toBeInTheDocument()
    expect(screen.getByText('Version 1.0.0 · 81f05ee')).toBeInTheDocument()
    expect(screen.getByText('Supplier sync from the ERP')).toBeInTheDocument()
    expect(screen.getByText('Late')).toBeInTheDocument()
    expect(screen.getByText('Staff member invited')).toBeInTheDocument()
    expect(screen.getByText('Wrong password at sign-in')).toBeInTheDocument()
    expect(recorded.some((r) => r.url.includes('storage-probe'))).toBe(false)
  })

  it('labels each attention item, gives its count, links where it is fixed, and lists its references', async () => {
    restore = mockFetch(routes(dashboard()))

    renderPage(<AdminDashboardPage />)

    const item = (await screen.findByText('Documents stuck waiting for a virus scan')).closest('li')!
    expect(within(item).getByText('3')).toBeInTheDocument()
    const link = item.querySelector('a[to="/back-office/operations"]')
    expect(link).toHaveTextContent('Operations')
    expect(within(item).getByText('Warning:')).toHaveClass('sr-only')
    expect(within(item).getByText(/^The supplier cannot use these documents/)).toBeInTheDocument()

    const push = screen.getByText('Approved suppliers whose creation in the ERP failed or stalled').closest('li')!
    expect(within(push).getByText('SUP-2026-000009')).toHaveAttribute('to', '/back-office/review/SUP-2026-000009')
    expect(within(push).getByText('SUP-2026-000011').tagName).toBe('SPAN')
  })

  it('leaves a hidden section out entirely and says a failed one could not load while the rest still show', async () => {
    restore = mockFetch(routes(dashboard({ erp: hidden, security: failed })))

    renderPage(<AdminDashboardPage />)

    expect(await screen.findByText('Recent activity')).toBeInTheDocument()
    expect(screen.queryByText('ERP')).not.toBeInTheDocument()
    expect(screen.queryByText('Shown to holders of integration management')).not.toBeInTheDocument()
    expect(screen.queryByText('Hourly supplier sync')).not.toBeInTheDocument()
    expect(screen.getByText('Security')).toBeInTheDocument()
    expect(screen.getAllByText('This section could not be loaded.')).toHaveLength(1)
    expect(screen.queryByText('Last 24 hours')).not.toBeInTheDocument()
    expect(screen.getByText('Supplier sync from the ERP')).toBeInTheDocument()
  })

  it('fails one ERP part on its own', async () => {
    const base = dashboard()
    const erp = (base.erp as { data: Record<string, unknown> }).data
    restore = mockFetch(routes(dashboard({ erp: ok({ ...erp, sync: failed }) })))

    renderPage(<AdminDashboardPage />)

    expect(await screen.findByText('This part could not be loaded. The other parts are unaffected.')).toBeInTheDocument()
    expect(screen.getByText('erp.example.test')).toBeInTheDocument()
    expect(screen.getByText('http, not https')).toBeInTheDocument()
    expect(screen.queryByText('Suppliers in the ERP')).not.toBeInTheDocument()
  })

  it('says all clear only when every check ran, and names the part whose checks could not run', async () => {
    restore = mockFetch(routes(dashboard({ needsAttention: ok({ allClear: true, items: [], checksNotRun: [] }) })))
    const first = renderPage(<AdminDashboardPage />)
    expect(await screen.findByText('All clear. Every check ran and nothing needs attention.')).toBeInTheDocument()
    first.unmount()
    restore()

    restore = mockFetch(routes(dashboard({
      needsAttention: ok({ allClear: false, items: [], checksNotRun: ['security_spike', 'erp_sync_failed', 'erp_sync_stale'] }),
    })))
    renderPage(<AdminDashboardPage />)

    expect(await screen.findByText('Some checks could not run: ERP, Security. Refresh to try again.')).toBeInTheDocument()
    expect(screen.queryByText('All clear. Every check ran and nothing needs attention.')).not.toBeInTheDocument()
    expect(screen.queryByText('All clear')).not.toBeInTheDocument()
  })

  it('offers Scan again only while documents are stuck, and says which could not be requeued', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch(routes(dashboard(), {
      '/api/v1/admin/scans/retry': { requeued: 2, stillPending: 0, quarantineFileMissing: ['SUP-2026-000042'] },
    }), recorded)

    renderPage(<AdminDashboardPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Scan again' }))

    expect(await screen.findByText('Documents sent back to the scanner: 2')).toBeInTheDocument()
    expect(screen.getByText('These could not be scanned again because their file is gone: SUP-2026-000042')).toBeInTheDocument()
    expect(recorded.filter((r) => r.url.includes('/scans/retry')).map((r) => r.method)).toEqual(['POST'])
  })

  it('does not offer Scan again with nothing stuck', async () => {
    const base = dashboard()
    const health = (base.systemHealth as { data: Record<string, unknown> }).data
    restore = mockFetch(routes(dashboard({ systemHealth: ok({ ...health, scans: { stuck: 0, stuckAfterMinutes: 15 } }) })))

    renderPage(<AdminDashboardPage />)

    expect(await screen.findByText('Virus scans')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Scan again' })).not.toBeInTheDocument()
  })

  it('checks storage and the scanner when asked, and keeps the answer with the time it was asked', async () => {
    restore = mockFetch(routes(dashboard(), {
      '/api/v1/admin/dashboard/storage-probe': { objectStorageReachable: true, virusScannerReachable: false, checkedAt: '2026-10-03T11:40:00Z' },
    }))

    renderPage(<AdminDashboardPage />)

    expect(await screen.findByText('Scanner not checked yet')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Check storage and scanner' }))

    expect(await screen.findByText('Virus scanner: not answering')).toBeInTheDocument()
    expect(screen.getByText(/^Checked /)).toBeInTheDocument()
    expect(screen.queryByText('Scanner not checked yet')).not.toBeInTheDocument()
  })

  it('says the dashboard could not load, and tries again on request', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch(routes({ __status: 500 }), recorded)

    renderPage(<AdminDashboardPage />)

    expect(await screen.findByText('Could not load the dashboard')).toBeInTheDocument()
    expect(screen.queryByText('Needs attention')).not.toBeInTheDocument()
    await expectRetryableFailure('/api/v1/admin/dashboard', recorded)
  })

  it('tries the whole dashboard again from a section that failed', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch(routes(dashboard({ security: failed })), recorded)

    renderPage(<AdminDashboardPage />)

    const failedCard = (await screen.findByText('This section could not be loaded.')).closest('div.overflow-hidden') as HTMLElement
    const reads = () => recorded.filter((r) => r.url.endsWith('/api/v1/admin/dashboard')).length
    const before = reads()
    fireEvent.click(within(failedCard).getByRole('button', { name: 'Try again' }))
    await vi.waitFor(() => expect(reads()).toBeGreaterThan(before))
  })

  it('keeps the last figures on screen when a later refresh fails, and says so', async () => {
    restore = mockFetch(routes(dashboard()))
    renderPage(<AdminDashboardPage />)
    expect(await screen.findByText('Supplier sync from the ERP')).toBeInTheDocument()

    restore()
    restore = mockFetch(routes({ __status: 500 }))
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    expect(await screen.findByText(/^The figures could not be refreshed/)).toBeInTheDocument()
    expect(screen.getByText('Supplier sync from the ERP')).toBeInTheDocument()
    expect(screen.queryByText('Could not load the dashboard')).not.toBeInTheDocument()
  })

  it('says recurring jobs are switched off, names a missing job, an empty reference list and an unsent transport', async () => {
    const base = dashboard()
    const health = (base.systemHealth as { data: Record<string, unknown> }).data
    restore = mockFetch(routes(dashboard({ systemHealth: ok({
      ...health,
      jobs: { recurringEnabled: false, jobs: [
        { id: 'rfq-timeline', verdict: 'missing', lateAfterMinutes: 15, lastState: null, lastExecution: null, nextExecution: null, link: '/back-office/operations' },
      ] },
    }) })))

    renderPage(<AdminDashboardPage />)

    expect(await screen.findByText('Switched off in this deployment')).toBeInTheDocument()
    expect(screen.getByText('Missing')).toBeInTheDocument()
    expect(screen.getByText('Every 5 min')).toBeInTheDocument()
    expect(screen.getByText('Not run yet')).toBeInTheDocument()
    expect(screen.getByText('No active codes: Delivery terms (Incoterms)')).toBeInTheDocument()
    expect(screen.getByText('Logged only')).toBeInTheDocument()
    expect(screen.getByText('Purchase orders are not sent to the ERP in this environment')).toBeInTheDocument()
  })

  it('says purchase-order sends are failing when the transport is real and sends failed', async () => {
    const base = dashboard()
    const health = (base.systemHealth as { data: Record<string, unknown> }).data
    restore = mockFetch(routes(dashboard({ systemHealth: ok({ ...health, purchaseOrderTransport: { configured: true, failedSends: 2 } }) })))

    renderPage(<AdminDashboardPage />)

    expect(await screen.findByText('Sends failing')).toBeInTheDocument()
    expect(screen.getByText('Failed sends: 2')).toBeInTheDocument()
    expect(screen.queryByText('Purchase orders are not sent to the ERP in this environment')).not.toBeInTheDocument()
    expect(screen.queryByText('Logged only')).not.toBeInTheDocument()
  })

  it('never lets an earlier storage check outvote the reading the dashboard just took', async () => {
    const base = dashboard()
    const health = (base.systemHealth as { data: Record<string, unknown> }).data
    restore = mockFetch(routes(dashboard({ systemHealth: ok({ ...health, objectStorage: { reachable: false } }) }), {
      '/api/v1/admin/dashboard/storage-probe': { objectStorageReachable: true, virusScannerReachable: true, checkedAt: '2026-10-03T11:00:00Z' },
    }))

    renderPage(<AdminDashboardPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Check storage and scanner' }))
    expect(await screen.findByText('File storage: reachable')).toBeInTheDocument()
    expect(screen.getByText('Not answering')).toBeInTheDocument()
    expect(screen.queryByText('Reachable')).not.toBeInTheDocument()
  })

  it('says from when the security counts run only while that is inside the week, and says so when nothing was ever counted', async () => {
    const base = dashboard()
    const security = (base.security as { data: Record<string, unknown> }).data
    restore = mockFetch(routes(dashboard({ security: ok({ ...security, countedSince: '2026-08-01T00:00:00Z' }) })))
    const first = renderPage(<AdminDashboardPage />)
    expect(await screen.findByText('Wrong password at sign-in')).toBeInTheDocument()
    expect(screen.queryByText(/^Counted from/)).not.toBeInTheDocument()
    first.unmount()
    restore()

    restore = mockFetch(routes(dashboard({ security: ok({ ...security, countedSince: null }) })))
    renderPage(<AdminDashboardPage />)
    expect(await screen.findByText(/^No sign-in event has been stored yet/)).toBeInTheDocument()
  })

  it('refetches every minute while it is open', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const recorded: RecordedRequest[] = []
    restore = mockFetch(routes(dashboard()), recorded)

    renderPage(<AdminDashboardPage />)
    await screen.findByText('Needs attention')
    const reads = () => recorded.filter((r) => r.url.endsWith('/api/v1/admin/dashboard')).length
    expect(reads()).toBe(1)

    await act(async () => { await vi.advanceTimersByTimeAsync(59_000) })
    expect(reads()).toBe(1)
    await act(async () => { await vi.advanceTimersByTimeAsync(2_000) })
    expect(reads()).toBe(2)
  })

  it('renders in Arabic', async () => {
    await i18n.changeLanguage('ar')
    restore = mockFetch(routes(dashboard()))

    renderPage(<AdminDashboardPage />)

    expect(await screen.findByText('بحاجة إلى متابعة')).toBeInTheDocument()
    expect(screen.getByText('مزامنة الموردين من نظام الوزارة')).toBeInTheDocument()
  })
})
