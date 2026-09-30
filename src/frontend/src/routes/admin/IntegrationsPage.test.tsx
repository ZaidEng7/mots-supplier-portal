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
//
// THE SWITCH THAT CREATES APPROVED SUPPLIERS IN THE ERP gets four. It cannot be turned on before a group is chosen,
// because the ERP refuses a supplier without one. Turning it on asks first, with the number of approved suppliers
// waiting, and agreeing is what saves it - with the group - while cancelling sends nothing. Turning it off is an
// ordinary edit, carried by Save, which leaves the group out when nobody changed it. And a group read the ERP refused
// shows the ERP's words and keeps the saved group chosen, rather than an empty list that reads as an ERP with no
// groups. Every test declares the group read, because the ERP card always asks for it and the harness refuses a
// request nobody described.
//
// A CARD LEFT OPEN MUST NOT PUT BACK WHAT SOMEBODY ELSE CHANGED. Save carries no version, and the server reads a switch
// or a group that is sent as a request to set it, so a card that sent what it loaded with would turn the writes back
// on after another administrator paused them - with no question, under the name of whoever only changed a secret. So a
// save of another field sends neither, whether or not the list was read again since; a card whose list was read again
// shows the server's switch and group; and a group the person did choose is sent on its own. What Save compares with is
// the save's own answer as soon as it arrives, so a list that cannot be read again afterwards does not leave the card
// comparing with the value before it. serverHolding answers the list with what the last save stored, as the server
// does, and lets a test move the server's values under the card.

import { describe, expect, it, afterEach } from 'vitest'
import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage, type RecordedRequest } from '../../test/renderPage'

const { IntegrationsPage } = await import('./IntegrationsPage')

const LIST = '/api/v1/admin/integrations'
const GROUPS = '/api/v1/admin/integrations/erp/supplier-groups'
const GROUP_LIST = { groups: ['Local Suppliers - SYP', 'Services - SYP'] }

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
  createSuppliersInErp: false,
  defaultSupplierGroup: null as string | null,
  suppliersWaitingForErp: 0,
}

function serverHolding(initial: typeof ERP, saved: typeof ERP = initial) {
  let current = initial
  return {
    moveTo: (next: typeof ERP) => {
      current = next
    },
    route: {
      __byMethod: {
        get GET() {
          return [current]
        },
        get PUT() {
          current = saved
          return saved
        },
      },
    },
  }
}

function sentBody(recorded: RecordedRequest[]): Record<string, unknown> {
  const put = recorded.find((request) => request.method === 'PUT')
  expect(put).toBeDefined()
  return JSON.parse(put!.body) as Record<string, unknown>
}

describe('IntegrationsPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('keeps the stored secret when the field is left alone', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ [LIST]: { __byMethod: { GET: [ERP], PUT: ERP } }, [GROUPS]: GROUP_LIST }, recorded)

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
    restore = mockFetch({ [LIST]: { __byMethod: { GET: [ERP], PUT: ERP } }, [GROUPS]: GROUP_LIST }, recorded)

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
      [GROUPS]: GROUP_LIST,
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
      [GROUPS]: GROUP_LIST,
    })

    renderPage(<IntegrationsPage />)

    expect(await screen.findByText('Needs attention')).toBeInTheDocument()
    expect(screen.queryByText('Failed')).not.toBeInTheDocument()
  })

  it('says when the deployment settings are still in force', async () => {
    restore = mockFetch({
      [LIST]: { __byMethod: { GET: [{ ...ERP, source: 'Configuration', baseUrl: '' }] } },
      [GROUPS]: GROUP_LIST,
    })

    renderPage(<IntegrationsPage />)

    expect(await screen.findByText('From deployment settings')).toBeInTheDocument()
  })

  it('reports a failed test as an answer and keeps the other system its own words', async () => {
    restore = mockFetch({
      [LIST]: { __byMethod: { GET: [ERP] } },
      [GROUPS]: GROUP_LIST,
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

  it('will not turn on creating suppliers in the ERP before a group is chosen', async () => {
    restore = mockFetch({ [LIST]: { __byMethod: { GET: [ERP] } }, [GROUPS]: GROUP_LIST })

    renderPage(<IntegrationsPage />)

    const toggle = await screen.findByRole('switch', { name: 'Create approved suppliers in the ERP' })
    expect(toggle).toBeDisabled()
    expect(toggle).not.toBeChecked()
    expect(screen.getByText(/Choose a supplier group first/)).toBeInTheDocument()

    await userEvent.click(await screen.findByRole('combobox', { name: 'ERP supplier group' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Services - SYP' }))

    expect(toggle).toBeEnabled()
  })

  it('asks before creating suppliers in the ERP, says how many are waiting, and saves the switch with its group', async () => {
    const recorded: RecordedRequest[] = []
    const waiting = { ...ERP, defaultSupplierGroup: 'Local Suppliers - SYP', suppliersWaitingForErp: 3 }
    const server = serverHolding(waiting, { ...waiting, createSuppliersInErp: true })
    restore = mockFetch({ [LIST]: server.route, [GROUPS]: GROUP_LIST }, recorded)

    renderPage(<IntegrationsPage />)

    await userEvent.click(await screen.findByRole('switch', { name: 'Create approved suppliers in the ERP' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/3 approved suppliers are waiting/)).toBeInTheDocument()
    expect(within(dialog).getByText(/Local Suppliers - SYP/)).toBeInTheDocument()
    expect(recorded.filter((request) => request.method === 'PUT')).toEqual([])

    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn on and save' }))

    const put = recorded.find((request) => request.method === 'PUT')
    expect(put).toBeDefined()
    expect(JSON.parse(put!.body)).toMatchObject({
      createSuppliersInErp: true,
      defaultSupplierGroup: 'Local Suppliers - SYP',
    })
    expect(await screen.findByRole('switch', { name: 'Create approved suppliers in the ERP' })).toBeChecked()
  })

  it('turns it off again against what the save answered, even when the list cannot be read again', async () => {
    const recorded: RecordedRequest[] = []
    const filed = { ...ERP, defaultSupplierGroup: 'Local Suppliers - SYP' }
    let saved = false
    const listUnreadableAfterSave = {
      __byMethod: {
        get GET() {
          return saved ? { __status: 503 } : [filed]
        },
        get PUT() {
          saved = true
          return { ...filed, createSuppliersInErp: true }
        },
      },
    }
    restore = mockFetch({ [LIST]: listUnreadableAfterSave, [GROUPS]: GROUP_LIST }, recorded)

    renderPage(<IntegrationsPage />)

    const toggle = await screen.findByRole('switch', { name: 'Create approved suppliers in the ERP' })
    await userEvent.click(toggle)
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Turn on and save' }))
    await waitFor(() => expect(toggle).toBeChecked())

    await userEvent.click(toggle)
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    const puts = recorded.filter((request) => request.method === 'PUT')
    expect(puts).toHaveLength(2)
    expect(JSON.parse(puts[1].body)).toMatchObject({ createSuppliersInErp: false })
  })

  it('sends nothing and leaves the switch off when the question is cancelled', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch(
      {
        [LIST]: { __byMethod: { GET: [{ ...ERP, defaultSupplierGroup: 'Local Suppliers - SYP' }] } },
        [GROUPS]: GROUP_LIST,
      },
      recorded,
    )

    renderPage(<IntegrationsPage />)

    const toggle = await screen.findByRole('switch', { name: 'Create approved suppliers in the ERP' })
    await userEvent.click(toggle)
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }))

    expect(toggle).not.toBeChecked()
    expect(recorded.filter((request) => request.method === 'PUT')).toEqual([])
  })

  it('carries turning it off in the ordinary save, and leaves the group to the server', async () => {
    const recorded: RecordedRequest[] = []
    const on = { ...ERP, createSuppliersInErp: true, defaultSupplierGroup: 'Local Suppliers - SYP' }
    const server = serverHolding(on, { ...on, createSuppliersInErp: false })
    restore = mockFetch({ [LIST]: server.route, [GROUPS]: GROUP_LIST }, recorded)

    renderPage(<IntegrationsPage />)

    await userEvent.click(await screen.findByRole('switch', { name: 'Create approved suppliers in the ERP' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    const body = sentBody(recorded)
    expect(body).toMatchObject({ createSuppliersInErp: false })
    expect(body).not.toHaveProperty('defaultSupplierGroup')
  })

  it('does not send the switch or the group with a save of another field', async () => {
    const recorded: RecordedRequest[] = []
    const on = { ...ERP, createSuppliersInErp: true, defaultSupplierGroup: 'Local Suppliers - SYP' }
    restore = mockFetch({ [LIST]: serverHolding(on).route, [GROUPS]: GROUP_LIST }, recorded)

    renderPage(<IntegrationsPage />)

    await userEvent.type(await screen.findByLabelText('Secret'), 'a-new-secret')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    const body = sentBody(recorded)
    expect(body).toMatchObject({ apiSecret: 'a-new-secret' })
    expect(body).not.toHaveProperty('createSuppliersInErp')
    expect(body).not.toHaveProperty('defaultSupplierGroup')
  })

  it('does not turn the writes back on from a card left open while somebody else turned them off', async () => {
    const recorded: RecordedRequest[] = []
    const on = { ...ERP, createSuppliersInErp: true, defaultSupplierGroup: 'Local Suppliers - SYP' }
    const server = serverHolding(on)
    restore = mockFetch({ [LIST]: server.route, [GROUPS]: GROUP_LIST }, recorded)

    const { queryClient } = renderPage(<IntegrationsPage />)
    const toggle = await screen.findByRole('switch', { name: 'Create approved suppliers in the ERP' })
    expect(toggle).toBeChecked()

    server.moveTo({ ...on, createSuppliersInErp: false })
    await act(() => queryClient.invalidateQueries({ queryKey: ['integrations'] }))
    await waitFor(() => expect(toggle).not.toBeChecked())

    await userEvent.type(screen.getByLabelText('Secret'), 'a-new-secret')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(sentBody(recorded)).not.toHaveProperty('createSuppliersInErp')
  })

  it('shows a group somebody else chose rather than saving the old one back over it', async () => {
    const recorded: RecordedRequest[] = []
    const filed = { ...ERP, defaultSupplierGroup: 'Local Suppliers - SYP' }
    const server = serverHolding(filed)
    restore = mockFetch({ [LIST]: server.route, [GROUPS]: GROUP_LIST }, recorded)

    const { queryClient } = renderPage(<IntegrationsPage />)
    const group = await screen.findByRole('combobox', { name: 'ERP supplier group' })
    expect(group).toHaveTextContent('Local Suppliers - SYP')

    server.moveTo({ ...filed, defaultSupplierGroup: 'Services - SYP' })
    await act(() => queryClient.invalidateQueries({ queryKey: ['integrations'] }))
    await waitFor(() => expect(group).toHaveTextContent('Services - SYP'))

    await userEvent.type(screen.getByLabelText('Secret'), 'a-new-secret')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(sentBody(recorded)).not.toHaveProperty('defaultSupplierGroup')
  })

  it('sends a group the person chose on its own, without the switch', async () => {
    const recorded: RecordedRequest[] = []
    const filed = { ...ERP, defaultSupplierGroup: 'Local Suppliers - SYP' }
    restore = mockFetch(
      { [LIST]: serverHolding(filed, { ...filed, defaultSupplierGroup: 'Services - SYP' }).route, [GROUPS]: GROUP_LIST },
      recorded,
    )

    renderPage(<IntegrationsPage />)

    await userEvent.click(await screen.findByRole('combobox', { name: 'ERP supplier group' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Services - SYP' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    const body = sentBody(recorded)
    expect(body).toMatchObject({ defaultSupplierGroup: 'Services - SYP' })
    expect(body).not.toHaveProperty('createSuppliersInErp')
  })

  it('shows why the groups could not be read and keeps the saved group chosen', async () => {
    restore = mockFetch({
      [LIST]: { __byMethod: { GET: [{ ...ERP, defaultSupplierGroup: 'Local Suppliers - SYP' }] } },
      [GROUPS]: {
        __status: 502,
        title: 'The ERP\u2019s supplier groups could not be read.',
        detail: 'The ERP refused a supplier group read with 403 Forbidden (PermissionError).',
      },
    })

    renderPage(<IntegrationsPage />)

    expect(await screen.findByText(/403 Forbidden \(PermissionError\)/)).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'ERP supplier group' })).toHaveTextContent('Local Suppliers - SYP')
  })
})
