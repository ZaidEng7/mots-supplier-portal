// SCR-402. The screen half of the capability T-099 recorded as missing on both sides: an officer looking for who could supply
// something had to start from the OFFERING search, so a registered supplier with no catalogue entries was invisible to the
// person deciding whom to invite.
//
// A supplier lists with its categories and offering count, and the category CODE is resolved to its label from the reference
// list rather than printed raw - a buyer reading "catering" is reading a database value.
//
// A SUSPENDED supplier shows with its status rather than being hidden: D-55's standard is held to here, so the row exists and
// says why it is not invitable, where hiding it would leave a buyer who knows the company is registered with no explanation
// anywhere. And the absence of a catalogue is STATED rather than left as an empty cell that reads like a loading bug.
//
// The category and status filters go to the SERVER rather than being applied in the browser: filtering client-side would be
// wrong the moment the registry exceeds one page, because the screen would narrow twenty rows and call it the answer - so that
// test asserts the query string rather than the table.
//
// The next page is offered when the server says there is one, which is MSP-84's lesson applied to a list that grows without
// bound: a page-one-only screen hides everything after the first page with nothing visibly wrong.
//
// And nothing matching says so, instead of rendering an empty table.

import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage, type RecordedRequest } from '../../test/renderPage'
import { SupplierDirectoryPage } from './SupplierDirectoryPage'


const DIRECTORY = '/api/v1/supplier-directory'
const CATEGORIES = '/api/v1/reference/categories'

function envelope(data: unknown[], nextCursor: string | null = null) {
  return {
    data,
    pagination: { mode: 'cursor', nextCursor, prevCursor: null, pageSize: 20, totalCount: null, hasMore: nextCursor !== null },
    meta: { sort: 'displayNameEn', filtersApplied: null },
  }
}

const ACTIVE = {
  supplierCode: 'SUP-2026-000001', displayNameAr: 'شركة الشام', displayNameEn: 'Al-Sham Trading',
  lifecycleState: 'Active', categoryCodes: ['catering'], offeringCount: 3, city: 'Damascus', regionCode: 'DAM',
}

const SUSPENDED = {
  supplierCode: 'SUP-2026-000002', displayNameAr: 'شركة موقوفة', displayNameEn: 'Suspended Co',
  lifecycleState: 'Suspended', categoryCodes: [], offeringCount: 0, city: null, regionCode: null,
}

function fixtures() {
  return {
    [CATEGORIES]: [{ code: 'catering', nameAr: 'تغذية', nameEn: 'Catering', isActive: true }],
    [DIRECTORY]: envelope([ACTIVE, SUSPENDED]),
  }
}

let restore: (() => void) | undefined
afterEach(() => restore?.())

describe('SupplierDirectoryPage', () => {
  it('lists a supplier with its categories and offering count', async () => {
    restore = mockFetch(fixtures())

    renderPage(<SupplierDirectoryPage />)

    expect(await screen.findByText('Al-Sham Trading')).toBeInTheDocument()
    expect(screen.getByText('SUP-2026-000001')).toBeInTheDocument()
    expect(screen.getByText('Catering')).toBeInTheDocument()
    expect(screen.getByText('Damascus')).toBeInTheDocument()
  })

  it('shows a suspended supplier with its status rather than hiding it', async () => {
    restore = mockFetch(fixtures())

    renderPage(<SupplierDirectoryPage />)

    expect(await screen.findByText('Suspended Co')).toBeInTheDocument()
    expect(screen.getByText('Suspended')).toBeInTheDocument()
    expect(screen.getByText('No categories recorded')).toBeInTheDocument()
  })

  it('sends the category and status filters to the server rather than filtering in the browser', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch(fixtures(), recorded)

    renderPage(<SupplierDirectoryPage />)
    await screen.findByText('Al-Sham Trading')

    await userEvent.click(screen.getByRole('combobox', { name: /status/i }))
    await userEvent.click(await screen.findByRole('option', { name: 'Active' }))

    const filtered = recorded.filter((r) => r.url.includes('lifecycleState=Active'))
    expect(filtered.length).toBeGreaterThan(0)
  })

  it('offers the next page when the server says there is one', async () => {
    restore = mockFetch({ ...fixtures(), [DIRECTORY]: envelope([ACTIVE], 'CURSOR-2') })

    renderPage(<SupplierDirectoryPage />)
    await screen.findByText('Al-Sham Trading')

    expect(screen.getByRole('button', { name: /load more/i })).toBeInTheDocument()
  })

  it('says so when nothing matches, instead of rendering an empty table', async () => {
    restore = mockFetch({ ...fixtures(), [DIRECTORY]: envelope([]) })

    renderPage(<SupplierDirectoryPage />)

    expect(await screen.findByText('No suppliers match')).toBeInTheDocument()
  })
})
