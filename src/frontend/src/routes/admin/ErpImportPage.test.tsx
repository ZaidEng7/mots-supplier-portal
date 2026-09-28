// The supplier import preview screen.
//
// THE FIRST TEST IS THAT NOTHING HAPPENS ON ARRIVAL, and it is the one most likely to be broken by a later
// refactor: turning this into a useQuery would look tidier and would call another ministry's server every time an
// administrator passed through this route, including when they only meant to read the previous result. The mock
// declares no route at all, so a request on mount fails the test loudly rather than quietly succeeding.
//
// THE REFUSAL REASON IS ASSERTED AS TEXT, not as a count. The number tells an operator how many suppliers cannot
// be imported; the sentence tells them why, and the why is the thing they act on - no email means asking the other
// team for addresses, not retrying anything here.
//
// A FAILED IMPORT MUST SAY IT WAS THE IMPORT. The first version reused the preview's wording for every failure, so
// somebody who had just pressed "Run the import" read "the preview could not be produced" and could not tell
// whether anything had been written. That happened for real, against an API that did not have the route yet.
//
// THE THREE FAILURES ARE ASSERTED SEPARATELY because they are three different jobs, and a screen that collapsed
// them into "could not load" would send an administrator to read logs for a problem the server already named. The
// 502 case also asserts the ERP's own words survive to the screen: they are what gets forwarded to the team who
// can fix it, and a translated approximation of another system's error is worse than the original.

import { describe, expect, it, afterEach } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage } from '../../test/renderPage'

const { ErpImportPage } = await import('./ErpImportPage')

const PREVIEW = '/api/v1/admin/erp-import/preview'
const RUN = '/api/v1/admin/erp-import/run'

const OUTCOME = {
  erpSupplierCount: 2,
  created: 1,
  updated: 0,
  refused: 1,
  failed: 0,
  rows: [
    {
      externalId: 'Damascus Supplies Co',
      name: 'Damascus Supplies Co',
      outcome: 'Created',
      referenceCode: 'SUP-2026-000001',
      notes: ['Approved without portal review, imported from the ERP.'],
    },
    {
      externalId: 'Tartous Beverages',
      name: 'Tartous Beverages',
      outcome: 'Refused',
      referenceCode: null,
      notes: ['The supplier has no email address, so no account can be created.'],
    },
  ],
}

const REPORT = {
  erpSupplierCount: 3,
  wouldCreate: 1,
  wouldUpdate: 1,
  refused: 1,
  rows: [
    {
      externalId: 'Damascus Supplies Co',
      name: 'Damascus Supplies Co',
      action: 'Create',
      notes: ['No address; city, governorate and coordinates stay empty.'],
      matchedReferenceCode: null,
    },
    {
      externalId: 'Homs Linen Mills',
      name: 'Homs Linen Mills',
      action: 'Update',
      notes: ['The Arabic name starts as the English one; the ERP holds only one name.'],
      matchedReferenceCode: 'SUP-2026-000004',
    },
    {
      externalId: 'Tartous Beverages',
      name: 'Tartous Beverages',
      action: 'Refuse',
      notes: [
        'The supplier has no email address, so no account can be created: a login needs a mailbox for the password link.',
      ],
      matchedReferenceCode: null,
    },
  ],
}

describe('ErpImportPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('reads nothing until the button is pressed', async () => {
    restore = mockFetch({})

    renderPage(<ErpImportPage />)

    expect(await screen.findByRole('button', { name: 'Run the preview' })).toBeInTheDocument()
    expect(screen.queryByText('Summary')).not.toBeInTheDocument()
  })

  it('reports the counts and every supplier with its reasons', async () => {
    restore = mockFetch({ [PREVIEW]: { __byMethod: { POST: REPORT } } })

    renderPage(<ErpImportPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Run the preview' }))

    expect(await screen.findByText('Summary')).toBeInTheDocument()
    expect(screen.getByText('Refused')).toBeInTheDocument()

    expect(screen.getByText('Damascus Supplies Co')).toBeInTheDocument()
    expect(screen.getByText('SUP-2026-000004')).toBeInTheDocument()
    expect(screen.getByText(/no email address/)).toBeInTheDocument()
  })

  it('says the connection is not configured rather than reporting nothing to import', async () => {
    restore = mockFetch({
      [PREVIEW]: { __byMethod: { POST: { __status: 503, detail: 'set Erp:Enabled' } } },
    })

    renderPage(<ErpImportPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Run the preview' }))

    expect(await screen.findByText('The connection is not configured')).toBeInTheDocument()
    expect(screen.queryByText('Summary')).not.toBeInTheDocument()
  })

  it('shows the ministry system its own words when it refuses', async () => {
    restore = mockFetch({
      [PREVIEW]: {
        __byMethod: {
          POST: { __status: 502, detail: 'The ERP refused a supplier read with 403 Forbidden (PermissionError).' },
        },
      },
    })

    renderPage(<ErpImportPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Run the preview' }))

    expect(await screen.findByText(/403 Forbidden \(PermissionError\)/)).toBeInTheDocument()
  })

  it('asks before importing, and imports nothing until the confirmation is accepted', async () => {
    restore = mockFetch({ [RUN]: { __byMethod: { POST: OUTCOME } } })

    renderPage(<ErpImportPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Run the import' }))

    expect(await screen.findByText('Confirm the import')).toBeInTheDocument()
    expect(screen.queryByText('What the import did')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByText('What the import did')).not.toBeInTheDocument()
  })

  it('reports what the import actually did, row by row', async () => {
    restore = mockFetch({ [RUN]: { __byMethod: { POST: OUTCOME } } })

    renderPage(<ErpImportPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Run the import' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Yes, run the import' }))

    expect(await screen.findByText('What the import did')).toBeInTheDocument()
    expect(screen.getByText('SUP-2026-000001')).toBeInTheDocument()
    // Twice on purpose: once as the count's label, once as this row's badge. Asserting one of them with getByText
    // fails on the ambiguity rather than on the behaviour, which is a test reporting a problem that is not there.
    expect(screen.getAllByText('Created')).toHaveLength(2)
    expect(screen.getByText(/no email address/)).toBeInTheDocument()
  })

  it('says it was the import that failed, not the preview', async () => {
    restore = mockFetch({ [RUN]: { __byMethod: { POST: { __status: 404 } } } })

    renderPage(<ErpImportPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Run the import' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Yes, run the import' }))

    expect(await screen.findByText('The import could not be run')).toBeInTheDocument()
    expect(screen.queryByText('The preview could not be produced')).not.toBeInTheDocument()
  })

  it('passes on the server\'s reason when the import is not configured', async () => {
    restore = mockFetch({
      [RUN]: {
        __byMethod: {
          POST: { __status: 503, detail: 'No initial password is configured: set ErpImport:InitialPassword.' },
        },
      },
    })

    renderPage(<ErpImportPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Run the import' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Yes, run the import' }))

    expect(await screen.findByText(/ErpImport:InitialPassword/)).toBeInTheDocument()
  })

  it('reports a portal fault as ours', async () => {
    restore = mockFetch({ [PREVIEW]: { __byMethod: { POST: { __status: 500 } } } })

    renderPage(<ErpImportPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Run the preview' }))

    expect(await screen.findByText('The preview could not be produced')).toBeInTheDocument()
  })
})
