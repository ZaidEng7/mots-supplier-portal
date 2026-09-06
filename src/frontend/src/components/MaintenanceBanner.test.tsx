import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { renderPage, mockFetch } from '../test/renderPage'

const { MaintenanceBanner } = await import('./MaintenanceBanner')

/** SCR-044. The interesting cases are the ones where a banner must NOT appear. */
describe('MaintenanceBanner', () => {
  let restore: (() => void) | undefined
  afterEach(() => restore?.())

  it('says nothing when no notice is configured', async () => {
    restore = mockFetch({ '/api/v1/meta': { version: '1.0.0', commit: null, maintenance: null } })
    renderPage(<MaintenanceBanner />)
    // Waited on rather than asserted immediately: the query has to actually resolve for the absence
    // to mean anything.
    await new Promise((resolve) => setTimeout(resolve, 10))
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('says nothing when the configured message is blank', async () => {
    // The shape a configuration provider actually returns for keys left unset — empty strings, not
    // nulls. `??` accepted those, and an Arabic reader got an empty warning bar on every page whenever
    // only the English half was filled in.
    restore = mockFetch({
      '/api/v1/meta': { version: '1.0.0', commit: null, maintenance: { messageAr: '', messageEn: '', from: '', to: '' } },
    })
    renderPage(<MaintenanceBanner />)
    await new Promise((resolve) => setTimeout(resolve, 10))
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('shows the operator’s words, and the window beside them', async () => {
    restore = mockFetch({
      '/api/v1/meta': {
        version: '1.0.0',
        commit: null,
        maintenance: { messageAr: '', messageEn: 'Down on Friday evening.', from: 'Fri 21:00', to: 'Sat 01:00' },
      },
    })
    renderPage(<MaintenanceBanner />)

    expect(await screen.findByRole('status')).toHaveTextContent('Down on Friday evening.')
    expect(screen.getByRole('status')).toHaveTextContent('Fri 21:00 — Sat 01:00')
  })
})
