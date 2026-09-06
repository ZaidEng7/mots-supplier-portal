import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../test/renderPage'

const { DocumentsPage } = await import('./DocumentsPage')

const PROFILE = { id: 's-1', supplierCode: 'SUP-000001', displayNameEn: 'Gulf Catering Co.', displayNameAr: 'الخليج' }

function docType(overrides: Record<string, unknown> = {}) {
  return {
    documentTypeId: 'dt-1',
    code: 'CR',
    nameEn: 'Commercial registration',
    nameAr: 'السجل التجاري',
    isRequired: true,
    expiryTracked: true,
    latestDocument: null,
    ...overrides,
  }
}

function version(overrides: Record<string, unknown> = {}) {
  return {
    id: 'd-1', version: 1, state: 'Approved', originalFileName: 'cr.pdf',
    uploadedAt: '2026-08-01T09:00:00Z', expiryDate: '2027-01-01', rejectReason: null,
    ...overrides,
  }
}

/**
 * SCR-130/131/132/133. The rule with the most room to go wrong here is `needsAttention`, because
 * SCR-133 is a FILTER on this page rather than a second screen - so the definition of "expiring"
 * lives in exactly one place, and these are the cases that pin it.
 */
describe('DocumentsPage (SCR-130)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('lists the supplier document types with their state', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': [docType({ latestDocument: version() })],
    })

    renderPage(<DocumentsPage />)

    expect(await screen.findByText('Commercial registration')).toBeInTheDocument()
    // expiryTracked, so the date column is populated rather than an em dash.
    expect(screen.getByText(/2027/)).toBeInTheDocument()
  })

  it('counts a required type with nothing uploaded as needing attention', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': [docType({ latestDocument: null, isRequired: true })],
    })

    renderPage(<DocumentsPage />)

    expect(await screen.findByRole('button', { name: /needs attention|تحتاج/i })).toBeInTheDocument()
  })

  it('does not count an OPTIONAL type with nothing uploaded', async () => {
    // The control for the case above: without this, "needs attention" could simply mean "not
    // uploaded" and the banner would be permanent for every supplier.
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': [docType({ latestDocument: null, isRequired: false })],
    })

    renderPage(<DocumentsPage />)

    await screen.findByText('Commercial registration')
    expect(screen.queryByRole('button', { name: /needs attention|تحتاج/i })).not.toBeInTheDocument()
  })

  it.each(['Rejected', 'Expired', 'ExpiringSoon', 'ScanRejected'])(
    'counts a %s document as needing attention', async (state) => {
      restore = mockFetch({
        '/api/v1/suppliers/me': PROFILE,
        '/api/v1/suppliers/SUP-000001/documents': [docType({ latestDocument: version({ state }) })],
      })

      renderPage(<DocumentsPage />)

      expect(await screen.findByRole('button', { name: /needs attention|تحتاج/i })).toBeInTheDocument()
    },
  )

  it('does not count an approved document', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': [docType({ latestDocument: version({ state: 'Approved' }) })],
    })

    renderPage(<DocumentsPage />)

    await screen.findByText('Commercial registration')
    expect(screen.queryByRole('button', { name: /needs attention|تحتاج/i })).not.toBeInTheDocument()
  })

  it('filters the table to the types needing attention and back again', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': [
        docType({ documentTypeId: 'dt-1', code: 'CR', nameEn: 'Commercial registration', latestDocument: version({ state: 'Expired' }) }),
        docType({ documentTypeId: 'dt-2', code: 'VAT', nameEn: 'VAT certificate', latestDocument: version({ id: 'd-2', state: 'Approved' }) }),
      ],
    })

    renderPage(<DocumentsPage />)

    const toggle = await screen.findByRole('button', { name: /needs attention|تحتاج/i })
    expect(screen.getByText('VAT certificate')).toBeInTheDocument()

    await userEvent.click(toggle)
    expect(screen.queryByText('VAT certificate')).not.toBeInTheDocument()
    expect(screen.getByText('Commercial registration')).toBeInTheDocument()
    expect(toggle).toHaveAttribute('aria-pressed', 'true')

    await userEvent.click(screen.getByRole('button', { name: /show all|عرض الكل/i }))
    expect(screen.getByText('VAT certificate')).toBeInTheDocument()
  })

  it('shows a rejection reason on the row it belongs to', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': [
        docType({ latestDocument: version({ state: 'Rejected', rejectReason: 'The scan is illegible past page two.' }) }),
      ],
    })

    renderPage(<DocumentsPage />)

    expect(await screen.findByText('The scan is illegible past page two.')).toBeInTheDocument()
  })

  it('opens the version history for one type on request', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': [docType({ latestDocument: version({ version: 2 }) })],
      '/api/v1/suppliers/SUP-000001/documents/types/CR/history': [
        version({ id: 'd-2', version: 2, state: 'Approved', originalFileName: 'cr-v2.pdf' }),
        version({ id: 'd-1', version: 1, state: 'Rejected', originalFileName: 'cr-v1.pdf', rejectReason: 'Wrong document' }),
      ],
    })

    renderPage(<DocumentsPage />)

    await userEvent.click(await screen.findByRole('button', { name: /^history|السجل/i }))

    expect(await screen.findByText('cr-v1.pdf')).toBeInTheDocument()
    // The reason from two versions ago is the whole point of SCR-132: it is not on the current row.
    expect(screen.getByText('Wrong document')).toBeInTheDocument()
  })

  it('offers a retry when the list cannot be read', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': { __status: 500 },
    })

    renderPage(<DocumentsPage />)

    expect(await screen.findByRole('button', { name: /try again|إعادة المحاولة/i })).toBeInTheDocument()
  })

  it('uploads a chosen file against its type, with the expiry date entered beside it', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': [docType({ latestDocument: null })],
    }, recorded)

    renderPage(<DocumentsPage />)

    // fireEvent.change, not userEvent.type: a date input takes a value, not keystrokes, and typing
    // into one silently leaves it empty.
    fireEvent.change(await screen.findByLabelText(/^expires|تاريخ الانتهاء/i), { target: { value: '2027-06-30' } })
    expect(screen.getByLabelText(/^expires|تاريخ الانتهاء/i)).toHaveValue('2027-06-30')

    await userEvent.upload(
      screen.getByLabelText(/^upload/i),
      new File(['scan'], 'cr.pdf', { type: 'application/pdf' }),
    )

    // The request body is FormData, which the harness records as empty, so what is asserted is that
    // the write went to the collection - the field names are the client's contract and are covered
    // where that client is exercised.
    expect(recorded.some((r) => r.method === 'POST' && r.url.endsWith('/SUP-000001/documents'))).toBe(true)
    expect(await screen.findByText(/document uploaded|تم رفع/i)).toBeInTheDocument()
  })

  it("reports why an upload was refused, in the server's own words", async () => {
    // The refusals here are specific - an expiry date in the past, a type the allow-list does not
    // carry - and a generic "upload failed" would leave the supplier with nothing to act on.
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      // The POST goes to the collection, with the type id in the form body, so the list and the
      // upload share one URL - hence __byMethod: the row has to render before there is anything to
      // click, and a single fixture cannot be both an array and a refusal.
      '/api/v1/suppliers/SUP-000001/documents': {
        __byMethod: {
          GET: [docType({ latestDocument: null })],
          // `detail`, because that is where §7 puts the prose - RFC 9457's "human-readable explanation
          // of this occurrence". A fixture using `message` would make this test pass against a client
          // that reads the wrong field.
          POST: { __status: 422, code: 'INVALID_EXPIRY', detail: 'The expiry date is in the past.' },
        },
      },
    })

    renderPage(<DocumentsPage />)

    await userEvent.upload(
      await screen.findByLabelText(/^upload/i),
      new File(['scan'], 'cr.pdf', { type: 'application/pdf' }),
    )

    expect(await screen.findByText('The expiry date is in the past.')).toBeInTheDocument()
  })

  it('labels the control "replace" once a version exists', async () => {
    // Not cosmetic: "upload" on a type that already has an approved document invites a supplier to
    // think they are adding a second one, when the write supersedes what is there.
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': [docType({ latestDocument: version() })],
    })

    renderPage(<DocumentsPage />)

    expect(await screen.findByLabelText(/replace — Commercial registration/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/^upload — Commercial registration/i)).not.toBeInTheDocument()
  })

  it('fetches the download URL rather than linking straight to it', async () => {
    // The URL needs the Authorization header, so a plain anchor would arrive unauthenticated. The
    // window.open is stubbed because jsdom does not implement it.
    const open = vi.spyOn(window, 'open').mockReturnValue(null)
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': [docType({ latestDocument: version() })],
      '/api/v1/documents/d-1/download-url': { url: 'https://storage.example.test/signed' },
    })

    renderPage(<DocumentsPage />)
    await userEvent.click(await screen.findByRole('button', { name: /download|تنزيل/i }))

    await waitFor(() => expect(open).toHaveBeenCalled())
    expect(open.mock.calls[0][0]).toBe('https://storage.example.test/signed')
    open.mockRestore()
  })

  it('says so when the download URL cannot be obtained', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null)
    restore = mockFetch({
      '/api/v1/suppliers/me': PROFILE,
      '/api/v1/suppliers/SUP-000001/documents': [docType({ latestDocument: version() })],
      '/api/v1/documents/d-1/download-url': { __status: 500 },
    })

    renderPage(<DocumentsPage />)
    await userEvent.click(await screen.findByRole('button', { name: /download|تنزيل/i }))

    expect(await screen.findByText(/could not|تعذّر/i)).toBeInTheDocument()
    expect(open).not.toHaveBeenCalled()
    open.mockRestore()
  })
})
