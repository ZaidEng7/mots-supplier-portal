import { afterEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../../test/renderPage'

const { RolesPage } = await import('./RolesPage')

/** Regression test for a real bug: this page used to derive its permission checklist from the
 * union of what roles already had (roles.flatMap(r => r.permissions)), not the backend's full
 * Permissions.All catalog - so a permission not yet granted to any role (e.g. offering.search,
 * right after it was added to the catalog but before any role held it) was invisible here and
 * could only ever be granted via a direct DB write. Reuses that exact scenario: a roles response
 * where no role's own permission list contains offering.search, but allPermissions does. */
describe('RolesPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows a permission not yet granted to any role, and can grant it through the real UI', async () => {
    restore = mockFetch({
      '/api/v1/admin/roles/procurement_officer/permissions': { name: 'procurement_officer', permissions: ['rfq.publish', 'offering.search'] },
      '/api/v1/admin/roles': {
        roles: [
          { name: 'procurement_officer', permissions: ['rfq.publish'] },
          { name: 'system_admin', permissions: ['admin.roles.manage'] },
        ],
        allPermissions: ['rfq.publish', 'admin.roles.manage', 'offering.search'],
      },
    })

    renderPage(<RolesPage />)

    const heading = await screen.findByRole('heading', { name: 'Procurement Officer' })
    const card = heading.closest('div')!.parentElement as HTMLElement
    const offeringCheckbox = within(card).getByRole('checkbox', { name: /Search offerings/ })
    expect(offeringCheckbox).not.toBeChecked()

    await userEvent.click(offeringCheckbox)

    expect(await screen.findByRole('checkbox', { name: /Search offerings/, checked: true })).toBeInTheDocument()
  })

  it('renders an unmapped permission by its raw key rather than hiding it', async () => {
    restore = mockFetch({
      '/api/v1/admin/roles': {
        roles: [{ name: 'evaluator', permissions: [] }],
        allPermissions: ['some.brand.new.permission'],
      },
    })

    renderPage(<RolesPage />)

    expect(await screen.findByText('some.brand.new.permission')).toBeInTheDocument()
  })
})
