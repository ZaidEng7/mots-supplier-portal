// MSP-63: the reviewer's lifecycle actions, asserted through the rendered page.
//
// This is the first page-level test in the project, and it exists because the two defects this feature produced were both
// invisible to unit tests: the profile grid crashed on render, and the reason dialog carried a stale reason between
// actions. Both needed the page. The route param is mocked rather than served by a real router - see renderPage for why
// the harness deliberately has no router.
//
// An active supplier is offered suspension and nothing else; a suspended one reactivate and deactivate; and a deactivated
// one NOTHING, which is the assertion that matters most - Deactivated is terminal in the domain, so a button here would
// promise the reviewer something the server refuses with 409.
//
// The last test is BRULE-096 through the page rather than the component: a reason is required before the action can be
// confirmed, because the reason becomes the audit record.
//
// THE PUSH TO THE ERP has its own block. No chip at all while nothing was asked of the ERP; a chip per state after that,
// the created one carrying the ERP's name for the supplier. A failed push shows what the ERP said, and Retry only to
// somebody holding admin.integrations.manage: a reviewer without it sees the failure and no button, because the server
// would refuse them, and so does somebody holding only integration.retry, the award's retry, which the server no longer
// accepts for the push. Pressing Retry posts to the retry route and reads the view again, which is how the chip moves on.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../test/renderPage'
import { useAuthStore } from '../lib/authStore'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'SUP-2026-000038' }), Link: 'a' }
})

const { ReviewApplicationPage } = await import('./ReviewApplicationPage')

function viewFor(lifecycleState: string, liftsWhenErpApproves = false) {
  return {
    supplier: {
      referenceCode: 'SUP-2026-000038',
      displayNameAr: 'شركة',
      displayNameEn: 'Lifecycle Demo Co',
      description: null, website: null, logoStorageKey: null, supplierGroup: null,
      onboardingState: 'Approved',
      lifecycleState,
      currencyCode: null, legalInfo: null, primaryContactPhone: null,
      representatives: [], addresses: [], contacts: [], branches: [], bankAccounts: [], categoryCodes: [],
    },
    erpSync: { syncStatus: 'Synced', lastSyncedAt: null, externalId: 'SUP-2026-00038', liftsWhenErpApproves },
    documents: [],
    annotationHistory: [],
  }
}

describe('ReviewApplicationPage lifecycle actions', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('offers suspension on an active supplier and nothing else', async () => {
    restore = mockFetch({ '/api/v1/review/SUP-2026-000038': viewFor('Active') })

    renderPage(<ReviewApplicationPage />)

    await waitFor(() => expect(screen.getByRole('button', { name: /تعليق|Suspend/ })).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: /إعادة التفعيل|Reactivate/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /إلغاء التفعيل|Deactivate/ })).not.toBeInTheDocument()
  })

  it('offers reactivate and deactivate once suspended', async () => {
    restore = mockFetch({ '/api/v1/review/SUP-2026-000038': viewFor('Suspended') })

    renderPage(<ReviewApplicationPage />)

    await waitFor(() => expect(screen.getByRole('button', { name: /إعادة التفعيل|Reactivate/ })).toBeInTheDocument())
    expect(screen.getByRole('button', { name: /إلغاء التفعيل|Deactivate/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^(تعليق|Suspend)$/ })).not.toBeInTheDocument()
  })

  it('offers keeping a supplier suspended while the ERP approval would lift the suspension, and says why', async () => {
    restore = mockFetch({ '/api/v1/review/SUP-2026-000038': viewFor('Suspended', true) })

    renderPage(<ReviewApplicationPage />)

    const keep = await screen.findByRole('button', { name: /إبقاء التعليق|Keep suspended/ })
    expect(screen.getAllByText(/lifts by itself|سيُرفع تلقائياً/).length).toBeGreaterThan(0)

    await userEvent.click(keep)

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getAllByText(/إبقاء التعليق|Keep suspended/).length).toBeGreaterThan(0)
    expect(within(dialog).queryByText(/^(تعليق|Suspend)$/)).not.toBeInTheDocument()
  })

  it('offers no lifecycle action once deactivated, because it is terminal', async () => {
    restore = mockFetch({ '/api/v1/review/SUP-2026-000038': viewFor('Deactivated') })

    renderPage(<ReviewApplicationPage />)

    await waitFor(() => expect(screen.getByText('Deactivated')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: /تعليق|Suspend/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /إعادة التفعيل|Reactivate/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /إلغاء التفعيل|Deactivate/ })).not.toBeInTheDocument()
  })

  it('requires a reason before the lifecycle action can be confirmed', async () => {
    restore = mockFetch({ '/api/v1/review/SUP-2026-000038': viewFor('Active') })

    renderPage(<ReviewApplicationPage />)

    const open = await screen.findByRole('button', { name: /تعليق|Suspend/ })
    await userEvent.click(open)

    const dialog = await screen.findByRole('dialog')
    const confirm = within(dialog).getAllByRole('button').at(-1)!
    expect(confirm).toBeDisabled()
  })
})

describe('ReviewApplicationPage push to the ERP', () => {
  let restore: () => void
  afterEach(() => {
    restore?.()
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle', expired: false, lastEmail: null })
  })

  const VIEW = '/api/v1/review/SUP-2026-000038'
  const RETRY = '/api/v1/review/SUP-2026-000038/retry-erp-push'
  const LAST_ERROR = 'The ERP refused it with 417 ExpectationFailed: Value missing for Supplier: Supplier Group'

  function viewWithPush(erpPushStatus: string, externalId: string | null = null, erpPushLastError: string | null = null) {
    const view = viewFor('Active')
    return { ...view, erpSync: { ...view.erpSync, externalId, erpPushStatus, erpPushLastError } }
  }

  function signInWith(permissions: string[]) {
    useAuthStore.setState({
      accessToken: 'token',
      status: 'authenticated',
      claims: { userId: 'u-1', email: 'staff@example.test', permissions },
    })
  }

  it('shows no ERP chip while nothing was asked of the ERP', async () => {
    restore = mockFetch({ [VIEW]: viewWithPush('NotRequested', 'SUP-2026-00038') })

    renderPage(<ReviewApplicationPage />)

    await screen.findByText('Lifecycle Demo Co')
    expect(screen.queryByText(/^ERP:|erpPush/)).not.toBeInTheDocument()
  })

  it.each([
    ['Requested', null, 'ERP: waiting'],
    ['Linked', 'SUP-2026-00041', 'ERP: partly created'],
    ['Created', 'SUP-2026-00041', 'ERP: created (SUP-2026-00041)'],
  ])('shows a %s push as a chip beside the states', async (status, externalId, label) => {
    restore = mockFetch({ [VIEW]: viewWithPush(status, externalId) })

    renderPage(<ReviewApplicationPage />)

    expect(await screen.findByText(/^ERP:/)).toHaveTextContent(label)
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
  })

  it('shows a failed push with what the ERP said, and lets somebody allowed to retry it do so', async () => {
    signInWith(['supplier.review', 'admin.integrations.manage'])
    const recorded: RecordedRequest[] = []
    restore = mockFetch(
      {
        [VIEW]: viewWithPush('Failed', null, LAST_ERROR),
        [RETRY]: { __byMethod: { POST: viewWithPush('Requested', null, LAST_ERROR).erpSync } },
      },
      recorded,
    )

    renderPage(<ReviewApplicationPage />)

    expect(await screen.findByText(/^ERP:/)).toHaveTextContent('ERP: failed')
    expect(screen.getByText(LAST_ERROR)).toBeInTheDocument()

    const readsBefore = recorded.filter((request) => request.method === 'GET').length
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))

    await waitFor(() => expect(recorded.some((request) => request.method === 'POST' && request.url.includes(RETRY))).toBe(true))
    await waitFor(() =>
      expect(recorded.filter((request) => request.method === 'GET').length).toBeGreaterThan(readsBefore),
    )
  })

  it.each([
    ['a reviewer', ['supplier.review']],
    ['somebody who may only retry award sends', ['supplier.review', 'integration.retry']],
  ])('shows %s the failure but no Retry, because starting the push again is not theirs', async (_who, permissions) => {
    signInWith(permissions)
    restore = mockFetch({ [VIEW]: viewWithPush('Failed', null, LAST_ERROR) })

    renderPage(<ReviewApplicationPage />)

    expect(await screen.findByText(LAST_ERROR)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
  })
})
