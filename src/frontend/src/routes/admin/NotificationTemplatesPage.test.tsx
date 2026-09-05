import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { NotificationTemplatesPage } = await import('./NotificationTemplatesPage')

const TEMPLATE = {
  type: 'rfq.approved',
  titleAr: 'تمت الموافقة',
  titleEn: 'RFQ approved',
  bodyAr: 'تمت الموافقة على {rfqCode}',
  bodyEn: 'RFQ {rfqCode} was approved',
  shippedTitleAr: 'تمت الموافقة',
  shippedTitleEn: 'RFQ approved',
  shippedBodyAr: 'تمت الموافقة على {rfqCode}',
  shippedBodyEn: 'RFQ {rfqCode} was approved',
  isOverridden: false,
  updatedAt: null,
  availableTokens: ['rfqCode'],
}

/** SCR-715. Rewording a sentence a supplier reads on rejection used to be a redeploy. */
describe('NotificationTemplatesPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('answers "which of these has been changed" without opening anything', async () => {
    restore = mockFetch({
      '/api/v1/admin/notification-templates': [
        TEMPLATE,
        { ...TEMPLATE, type: 'award.rejected', isOverridden: true, updatedAt: '2026-09-01T10:00:00Z' },
      ],
    })

    renderPage(<NotificationTemplatesPage />)

    expect(await screen.findByText('rfq.approved')).toBeInTheDocument()
    expect(screen.getByText('Shipped wording')).toBeInTheDocument()
    expect(screen.getByText(/^Changed /)).toBeInTheDocument()
    // Collapsed: 29 types with four bilingual fields each is not a scannable page.
    expect(screen.queryByLabelText('Title (Arabic)')).not.toBeInTheDocument()
  })

  it('names the available tokens from the server, per type', async () => {
    restore = mockFetch({ '/api/v1/admin/notification-templates': [TEMPLATE] })

    renderPage(<NotificationTemplatesPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }))

    expect(screen.getByText('Available tokens: {rfqCode}')).toBeInTheDocument()
    expect(screen.getByLabelText('Title (Arabic)')).toBeInTheDocument()
    // What revert would restore, so the offer is not guesswork.
    expect(screen.getByText('Shipped wording (what revert restores)')).toBeInTheDocument()
  })

  it('names the tokens the notification cannot fill instead of reporting a generic failure', async () => {
    restore = mockFetch({
      '/api/v1/admin/notification-templates': [TEMPLATE],
      '/api/v1/admin/notification-templates/rfq.approved': {
        __status: 422, error: 'unknown_tokens', tokens: ['email', 'price'],
      },
    })

    renderPage(<NotificationTemplatesPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }))
    const titleAr = screen.getByLabelText('Title (Arabic)')
    await userEvent.clear(titleAr)
    await userEvent.type(titleAr, 'السعر {price}')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('This notification cannot fill: {email}, {price}')).toBeInTheDocument()
  })

  it('offers revert only on a type that has been overridden', async () => {
    restore = mockFetch({ '/api/v1/admin/notification-templates': [TEMPLATE] })

    renderPage(<NotificationTemplatesPage />)
    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }))

    // Nothing to revert to: the shipped wording is already what is in force.
    expect(screen.queryByRole('button', { name: 'Restore the shipped wording' })).not.toBeInTheDocument()
  })

  it('reverts an override back to the shipped wording', async () => {
    const calls: { url: string; method: string; body: string }[] = []
    restore = mockFetch({
      '/api/v1/admin/notification-templates': [{ ...TEMPLATE, isOverridden: true, titleEn: 'Reworded' }],
      '/api/v1/admin/notification-templates/rfq.approved': TEMPLATE,
    }, calls)

    renderPage(<NotificationTemplatesPage />)
    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }))
    await userEvent.click(screen.getByRole('button', { name: 'Restore the shipped wording' }))

    await waitFor(() => expect(screen.getByText('Shipped wording')).toBeInTheDocument())
    expect(calls.some((c) => c.method === 'DELETE' && c.url.includes('rfq.approved'))).toBe(true)
  })

  it('offers a retry instead of a blank page when the read fails', async () => {
    restore = mockFetch({ '/api/v1/admin/notification-templates': { __status: 500 } })

    renderPage(<NotificationTemplatesPage />)

    expect(await screen.findByText('Could not load the templates')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})
