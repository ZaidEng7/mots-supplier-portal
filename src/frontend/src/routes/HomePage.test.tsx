import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { renderPage, mockFetch } from '../test/renderPage'

const { HomePage } = await import('./HomePage')

/**
 * T-084. The walking-skeleton page, and the only screen in the product whose job is to say whether the
 * backend is reachable at all - so its FAILURE branch is the one worth testing. A health banner that reads
 * "healthy" when the request failed is worse than no banner.
 */
describe('HomePage', () => {
  let restore: (() => void) | undefined
  afterEach(() => {
    restore?.()
    vi.unstubAllGlobals()
  })

  it('reports healthy and lists the reference data it read', async () => {
    restore = mockFetch({
      '/health/ready': 'Healthy',
      '/api/v1/reference/currencies': [
        { id: '1', code: 'SYP', nameAr: 'ليرة سورية', nameEn: 'Syrian Pound' },
      ],
    })

    renderPage(<HomePage />)

    expect(await screen.findByText('Healthy')).toBeInTheDocument()
    expect(await screen.findByText('Syrian Pound')).toBeInTheDocument()
    expect(screen.getByText('SYP')).toBeInTheDocument()
  })

  it('reports unhealthy when the check fails, and does not also claim healthy', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      // Only health fails. The currencies read still succeeds, so this asserts the banner reacts to its
      // OWN query rather than to any failure on the page.
      if (String(input).includes('/health/')) return new Response('', { status: 503 })
      return new Response(JSON.stringify([]), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }))

    renderPage(<HomePage />)

    expect(await screen.findByText('Unavailable')).toBeInTheDocument()
    expect(screen.queryByText('Healthy')).not.toBeInTheDocument()
  })
})
