// Task #7, Stage C: each mutation's onSuccess handler routes queryClient.invalidateQueries through invalidateQuietly, which
// is Task #19's no-floating-promises fix - and a call site nothing exercises is exactly the coverage gap Sonar's new-code
// ratchet flags, as TeamPage.test.tsx records for this same class of bug. These drive each of the three mutation flows -
// create an Organization, add an OrgUnit, create a SupplierOrgLink - through a real user interaction, so the callback and the
// invalidateQuietly call inside it actually run.
//
// The empty state shows with no Organizations. Creating one toasts.
//
// Adding an OrgUnit is asserted by the INPUT clearing rather than by the unit appearing: mockFetch is stateless - the same
// static body answers every call to a matched URL - so a refetch after the mutation still returns the pre-mutation
// organizations list, and the new unit cannot be asserted as "now visible" without a stateful mock. What IS real and worth
// proving is that the onSuccess handler, which is what clears the name and calls invalidateQuietly, actually executed.
//
// Looking a supplier up and creating a link toasts.
//
// The last test reaches a failure state that cannot be reached by rendering the page: that query is disabled until somebody
// actually looks a supplier up, so the test has to do what a user does first.

import { afterEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, expectRetryableFailure, type RecordedRequest } from '../../test/renderPage'

const { OrganizationsPage } = await import('./OrganizationsPage')

const ORG = {
  id: 'org-1',
  legalNameAr: 'منظمة الاختبار',
  legalNameEn: 'Test Org',
  organizationType: 'Hotel',
  contactEmail: null,
  contactPhone: null,
  isActive: true,
  orgUnits: [],
}

describe('OrganizationsPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows the empty state when no Organizations exist', async () => {
    restore = mockFetch({ '/api/v1/organizations': [] })

    renderPage(<OrganizationsPage />)

    expect(await screen.findByText('No Organizations yet')).toBeInTheDocument()
  })

  it('creating an Organization shows a success toast', async () => {
    restore = mockFetch({ '/api/v1/organizations': [] })

    renderPage(<OrganizationsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Create Organization' }))
    const dialog = await screen.findByRole('dialog')
    const [arName, enName] = within(dialog).getAllByRole('textbox')
    await userEvent.type(arName, 'منظمة جديدة')
    await userEvent.type(enName, 'New Org')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Organization created')).toBeInTheDocument()
  })

  it('adding an OrgUnit clears the input once the mutation succeeds', async () => {
    restore = mockFetch({
      '/api/v1/organizations/org-1/org-units': { ...ORG, orgUnits: [{ id: 'unit-1', organizationId: 'org-1', parentOrgUnitId: null, name: 'Procurement Committee' }] },
      '/api/v1/organizations': [ORG],
    })

    renderPage(<OrganizationsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Manage units' }))
    const dialog = await screen.findByRole('dialog')
    const nameInput = within(dialog).getByRole('textbox')
    await userEvent.type(nameInput, 'Procurement Committee')
    expect(nameInput).toHaveValue('Procurement Committee')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }))

    await screen.findByRole('textbox')
    expect(within(dialog).getByRole('textbox')).toHaveValue('')
  })

  it('looking up a Supplier and creating an Organization link shows a success toast', async () => {
    restore = mockFetch({
      '/api/v1/organizations/supplier-links/SUP-2026-000001': [],
      '/api/v1/organizations': [ORG],
    })

    renderPage(<OrganizationsPage />)

    await userEvent.type(await screen.findByLabelText('Supplier reference code'), 'SUP-2026-000001')
    await userEvent.click(screen.getByRole('button', { name: 'Look up' }))

    expect(await screen.findByText('No links for this supplier')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('combobox', { name: 'Organization' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Test Org' }))
    await userEvent.click(screen.getByRole('button', { name: 'Add link' }))

    expect(await screen.findByText('Link created')).toBeInTheDocument()
  })

  it('shows a retryable failure when a supplier lookup cannot load its links', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({
      '/api/v1/organizations': [],
      '/api/v1/organizations/supplier-links/SUP-2026-000001': { __status: 500 },
    }, recorded)

    renderPage(<OrganizationsPage />)

    await userEvent.type(await screen.findByLabelText('Supplier reference code'), 'SUP-2026-000001')
    await userEvent.click(screen.getByRole('button', { name: 'Look up' }))

    await expectRetryableFailure('/supplier-links/', recorded)
  })
})
