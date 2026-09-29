// The connected-systems screen.
//
// THE FIRST TEST IS THAT AN UNTOUCHED SECRET FIELD SENDS null, which is the behaviour a reader cannot see and the
// one that costs a credential when it is wrong. The form can never prefill the value, so the field is empty on
// every edit; sending that empty string would clear the stored secret each time somebody fixed a typo in the
// address, and nothing would fail until the next run.
//
// THE SECOND IS THAT THE SCREEN SAYS WHICH SETTINGS ARE IN FORCE. An administrator who saves an address and sees
// no change has to be told that the deployment's own settings still win, rather than left to conclude the save
// did not work.
//
// THE LAST IMPORT IS SHOWN, FAILURE INCLUDED. The hourly run happens when nobody is watching, and a failure never
// shown looks exactly like a run with nothing to do - so the card must put it in front of the next person to look.
//
// THE THIRD IS THAT A FAILED TEST READS AS AN ANSWER AND KEEPS THE DETAIL. The detail is the other system's own
// words, and it is the part that says whether to check the address or to ring the other team.

import { describe, expect, it, afterEach } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage, type RecordedRequest } from '../../test/renderPage'

const { IntegrationsPage } = await import('./IntegrationsPage')

const LIST = '/api/v1/admin/integrations'

const ERP = {
  key: 'erp',
  displayName: 'Seven Gates ERP',
  baseUrl: 'http://erp.example:8001',
  apiKey: 'the-key',
  hasSecret: true,
  secretSetAt: '2026-09-28T09:00:00Z',
  isEnabled: true,
  source: 'Database',
  updatedAt: '2026-09-28T09:00:00Z',
  lastTestedAt: null,
  lastTestSucceeded: null,
  lastTestDetail: null,
  lastSyncAt: null,
  lastSyncOutcome: null,
  lastSyncSummary: null,
}

describe('IntegrationsPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('keeps the stored secret when the field is left alone', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ [LIST]: { __byMethod: { GET: [ERP], PUT: ERP } } }, recorded)

    renderPage(<IntegrationsPage />)

    await userEvent.clear(await screen.findByLabelText('Address'))
    await userEvent.type(screen.getByLabelText('Address'), 'http://erp.example:9001')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    const put = recorded.find((request) => request.method === 'PUT')
    expect(put).toBeDefined()
    expect(JSON.parse(put!.body)).toMatchObject({
      baseUrl: 'http://erp.example:9001',
      apiSecret: null,
    })
  })

  it('sends a new secret when one is typed', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ [LIST]: { __byMethod: { GET: [ERP], PUT: ERP } } }, recorded)

    renderPage(<IntegrationsPage />)

    await userEvent.type(await screen.findByLabelText('Secret'), 'a-new-secret')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    const put = recorded.find((request) => request.method === 'PUT')
    expect(JSON.parse(put!.body)).toMatchObject({ apiSecret: 'a-new-secret' })
  })

  it('shows how the last import went, including a failure nobody was awake to see', async () => {
    restore = mockFetch({
      [LIST]: {
        __byMethod: {
          GET: [
            {
              ...ERP,
              lastSyncAt: '2026-09-29T23:00:00Z',
              lastSyncOutcome: 'Failed',
              lastSyncSummary: 'The import failed: No initial password is configured.',
            },
          ],
        },
      },
    })

    renderPage(<IntegrationsPage />)

    expect(await screen.findByText('Last import')).toBeInTheDocument()
    expect(screen.getByText('Failed')).toBeInTheDocument()
    expect(screen.queryByText('Needs attention')).not.toBeInTheDocument()
    expect(screen.getByText(/No initial password is configured/)).toBeInTheDocument()
  })

  it('shows a run that finished but left something for a person as needing attention, not as failed', async () => {
    restore = mockFetch({
      [LIST]: {
        __byMethod: {
          GET: [
            {
              ...ERP,
              lastSyncAt: '2026-09-29T23:00:00Z',
              lastSyncOutcome: 'NeedsAttention',
              lastSyncSummary: '80 in the ERP: 0 created. 1 possible rename(s) held for a person.',
            },
          ],
        },
      },
    })

    renderPage(<IntegrationsPage />)

    expect(await screen.findByText('Needs attention')).toBeInTheDocument()
    expect(screen.queryByText('Failed')).not.toBeInTheDocument()
  })

  it('says when the deployment settings are still in force', async () => {
    restore = mockFetch({
      [LIST]: { __byMethod: { GET: [{ ...ERP, source: 'Configuration', baseUrl: '' }] } },
    })

    renderPage(<IntegrationsPage />)

    expect(await screen.findByText('From deployment settings')).toBeInTheDocument()
  })

  it('reports a failed test as an answer and keeps the other system its own words', async () => {
    restore = mockFetch({
      [LIST]: { __byMethod: { GET: [ERP] } },
      '/api/v1/admin/integrations/erp/test': {
        __byMethod: {
          POST: {
            succeeded: false,
            detail: 'Reached http://erp, but it refused to read addresses (403 PermissionError); suppliers and contacts can be read.',
          },
        },
      },
    })

    renderPage(<IntegrationsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Test connection' }))

    expect(await screen.findByText('Not ready for the import')).toBeInTheDocument()
    expect(screen.getByText(/PermissionError/)).toBeInTheDocument()
  })
})
