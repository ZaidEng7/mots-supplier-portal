// SCR-045's banner, and what its not-configured line claims.
//
// The not-configured line says purchase orders are not sent to the ERP, in both languages. It used to say there was
// no real ERP integration at all, which an administrator read on the very screen that imports suppliers from the ERP
// every hour. The Arabic is asserted in its own words too, because the two halves are kept by hand and only one of them
// is what an English test renders.
//
// Degraded wins when both are true, and nothing renders when neither is: the query is waited on before the absence is
// asserted, because absence before the answer arrives means nothing.

import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import i18n from '../i18n/config'
import { renderPage, mockFetch } from '../test/renderPage'

const { ErpStatusBanner } = await import('./ErpStatusBanner')

describe('ErpStatusBanner', () => {
  let restore: (() => void) | undefined
  afterEach(() => restore?.())

  it('says purchase orders are not sent to the ERP, not that there is no ERP', async () => {
    restore = mockFetch({ '/api/v1/system/status': { erpDegraded: false, erpNotConfigured: true } })
    renderPage(<ErpStatusBanner />)

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Purchase orders are not sent to the ERP in this environment.',
    )
    expect(screen.getByRole('status')).not.toHaveTextContent('No real ERP integration')
  })

  it('says the same in Arabic, naming the ministry system', async () => {
    const restoreFetch = mockFetch({ '/api/v1/system/status': { erpDegraded: false, erpNotConfigured: true } })
    await i18n.changeLanguage('ar')
    restore = () => { restoreFetch(); void i18n.changeLanguage('en') }

    renderPage(<ErpStatusBanner />)

    expect(await screen.findByRole('status')).toHaveTextContent('لا تُرسَل أوامر الشراء إلى نظام الوزارة في هذه البيئة.')
  })

  it('says the sync is paused when a send has failed, even where purchase orders are not sent', async () => {
    restore = mockFetch({ '/api/v1/system/status': { erpDegraded: true, erpNotConfigured: true } })
    renderPage(<ErpStatusBanner />)

    expect(await screen.findByRole('status')).toHaveTextContent('ERP sync is paused.')
  })

  it('says nothing when there is nothing to say', async () => {
    restore = mockFetch({ '/api/v1/system/status': { erpDegraded: false, erpNotConfigured: false } })
    renderPage(<ErpStatusBanner />)
    await new Promise((resolve) => setTimeout(resolve, 10))
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})
