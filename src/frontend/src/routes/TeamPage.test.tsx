import { afterEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'

const { TeamPage } = await import('./TeamPage')

/**
 * Task #19: the invite/disable mutations' onSuccess handlers were the source of two of the eight
 * no-floating-promises findings this ticket fixed (queryClient.invalidateQueries called unawaited,
 * now routed through invalidateQuietly). Both call sites sat inside onSuccess callbacks nothing
 * exercised - Sonar's new-code coverage ratchet flagged exactly that gap. This drives a real
 * invite through the page so the callback, and the invalidateQuietly call inside it, actually run.
 */
describe('TeamPage invite flow', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows a success toast once an invite is sent', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/users': { items: [], hasMore: false, nextCursor: null },
    })

    renderPage(<TeamPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Invite member' }))

    const dialog = await screen.findByRole('dialog')
    const [fullName, email] = within(dialog).getAllByRole('textbox')
    await userEvent.type(fullName, 'Test Invitee')
    await userEvent.type(email, 'invitee@example.com')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send invite' }))

    expect(await screen.findByText('Invite sent')).toBeInTheDocument()
  })

  it('shows a success toast once a member is disabled', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/users/user-1/disable': {},
      '/api/v1/suppliers/me/users': {
        items: [{ userId: 'user-1', email: 'member@example.com', fullName: 'Existing Member', isActive: true }],
        hasMore: false,
        nextCursor: null,
      },
    })

    renderPage(<TeamPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Disable' }))

    expect(await screen.findByText('Member disabled')).toBeInTheDocument()
  })
})
