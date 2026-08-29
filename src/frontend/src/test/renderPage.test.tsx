import { afterEach, describe, expect, it } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import { renderPage, mockFetch } from './renderPage'
import { TeamPage } from '../routes/TeamPage'

/**
 * Proves the page-test harness on a real page, rather than on a component invented to suit it.
 *
 * TeamPage was chosen because it exercises everything the harness provides and nothing it does not:
 * a react-query fetch, i18n, and the toast provider. It has no router dependency, which is why the
 * harness deliberately omits a router - see renderPage for that reasoning.
 */
describe('page test harness', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders a page with its providers and the data it fetched', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/users': [
        { userId: '11111111-1111-1111-1111-111111111111', email: 'first@example.com', fullName: 'First Member', isActive: true },
        { userId: '22222222-2222-2222-2222-222222222222', email: 'second@example.com', fullName: 'Second Member', isActive: false },
      ],
    })

    renderPage(<TeamPage />)

    // Asserts on what a user would see, not on the query's internals: if the page renders before
    // data arrives, or renders an empty state instead, this fails.
    await waitFor(() => expect(screen.getByText('First Member')).toBeInTheDocument())
    expect(screen.getByText('second@example.com')).toBeInTheDocument()
  })

  it('throws on a request the test did not declare, rather than answering it', async () => {
    // The harness's most important behaviour, asserted DIRECTLY.
    //
    // The first version of this test rendered a page with an undeclared route and checked that its
    // data was absent. That passed whether the harness threw or silently returned an empty array -
    // verified by making it return one - so it asserted nothing. A test that cannot fail, written
    // inside the harness whose purpose is catching them.
    restore = mockFetch({ '/api/v1/declared': {} })

    await expect(fetch('/api/v1/not-declared')).rejects.toThrow(/No mock declared/)
  })
})
