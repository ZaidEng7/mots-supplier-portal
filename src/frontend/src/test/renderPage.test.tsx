// Proves the page-test harness on a real page, rather than on a component invented to suit it.
//
// TeamPage was chosen because it exercises everything the harness provides and nothing it does not: a react-query fetch, i18n,
// and the toast provider. It has no router dependency, which is why the harness deliberately omits a router - see renderPage
// for that reasoning. Its fetch returns the §5.2 list envelope rather than a bare array (MSP-84).
//
// The first test asserts on what a USER would see rather than on the query's internals: if the page renders before data
// arrives, or renders an empty state instead, it fails.
//
// The second is the harness's most important behaviour, asserted DIRECTLY: an undeclared request THROWS rather than being
// answered. The first version of this test rendered a page with an undeclared route and checked that its data was absent -
// which passed whether the harness threw or silently returned an empty array, verified by making it return one, so it asserted
// nothing. A test that cannot fail, written inside the harness whose purpose is catching them.

import { afterEach, describe, expect, it } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import { renderPage, mockFetch, listPage } from './renderPage'
import { TeamPage } from '../routes/TeamPage'

describe('page test harness', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders a page with its providers and the data it fetched', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/users': listPage([
        { userId: '11111111-1111-1111-1111-111111111111', email: 'first@example.com', fullName: 'First Member', isActive: true },
        { userId: '22222222-2222-2222-2222-222222222222', email: 'second@example.com', fullName: 'Second Member', isActive: false },
      ]),
    })

    renderPage(<TeamPage />)

    await waitFor(() => expect(screen.getByText('First Member')).toBeInTheDocument())
    expect(screen.getByText('second@example.com')).toBeInTheDocument()
  })

  it('throws on a request the test did not declare, rather than answering it', async () => {
    restore = mockFetch({ '/api/v1/declared': {} })

    await expect(fetch('/api/v1/not-declared')).rejects.toThrow(/No mock declared/)
  })
})
