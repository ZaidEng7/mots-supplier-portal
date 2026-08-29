import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'

// The route param, mocked rather than served by a real router - see renderPage for why the harness
// deliberately has no router.
vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'SUP-2026-000038' }), Link: 'a' }
})

const { ReviewApplicationPage } = await import('./ReviewApplicationPage')

function viewFor(lifecycleState: string) {
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
    erpSync: { status: 'NotSynced', lastSyncedAt: null, externalId: null },
    documents: [],
    annotationHistory: [],
  }
}

/**
 * MSP-63: the reviewer's lifecycle actions, asserted through the rendered page.
 *
 * This is the first page-level test in the project, and it exists because the two defects this
 * feature produced were both invisible to unit tests: the profile grid crashed on render, and the
 * reason dialog carried a stale reason between actions. Both needed the page.
 */
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

  it('offers no lifecycle action once deactivated, because it is terminal', async () => {
    // The assertion that matters most. Deactivated is terminal in the domain; a button here would
    // promise the reviewer something the server refuses with 409.
    restore = mockFetch({ '/api/v1/review/SUP-2026-000038': viewFor('Deactivated') })

    renderPage(<ReviewApplicationPage />)

    await waitFor(() => expect(screen.getByText('Deactivated')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: /تعليق|Suspend/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /إعادة التفعيل|Reactivate/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /إلغاء التفعيل|Deactivate/ })).not.toBeInTheDocument()
  })

  it('requires a reason before the lifecycle action can be confirmed', async () => {
    // BRULE-096 through the page rather than the component: the reason becomes the audit record.
    restore = mockFetch({ '/api/v1/review/SUP-2026-000038': viewFor('Active') })

    renderPage(<ReviewApplicationPage />)

    const open = await screen.findByRole('button', { name: /تعليق|Suspend/ })
    await userEvent.click(open)

    const dialog = await screen.findByRole('dialog')
    const confirm = within(dialog).getAllByRole('button').at(-1)!
    expect(confirm).toBeDisabled()
  })
})
