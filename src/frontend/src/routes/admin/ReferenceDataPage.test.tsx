import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage, type RecordedRequest } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { ReferenceDataPage } = await import('./ReferenceDataPage')

const LIST = '/api/v1/admin/reference/categories'

const ACTIVE = { code: 'IT', nameAr: 'تقنية المعلومات', nameEn: 'IT', isActive: true, isRequired: null, expiryTracked: null }
const RETIRED = { code: 'FAX', nameAr: 'فاكس', nameEn: 'Fax machines', isActive: false, isRequired: null, expiryTracked: null }

/** SCR-710/711/712 (T-080). The admin write surface shipped a batch earlier with no screen on it, so
 * adding a document type still meant a request by hand. */
describe('ReferenceDataPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('lists deactivated codes alongside active ones, and offers reactivation rather than deletion', async () => {
    restore = mockFetch({ [LIST]: [ACTIVE, RETIRED] })

    renderPage(<ReferenceDataPage />)

    // The retired row is PRESENT, not filtered away - D-28: deactivation that hides the row reads as
    // deletion, and the next administrator recreates the code.
    expect(await screen.findByText('FAX')).toBeInTheDocument()
    expect(screen.getByText('Inactive')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reactivate' })).toBeInTheDocument()

    // The control: the active row is on the same page and offers the opposite action, so the
    // assertion above is about this row's state and not about the page having one button.
    expect(screen.getByText('IT')).toBeInTheDocument()
    expect(screen.getByText('Active')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Deactivate' })).toBeInTheDocument()

    // No delete, anywhere. There is no endpoint to call (D-28) and the screen says so.
    expect(screen.queryByRole('button', { name: /delete|remove/i })).not.toBeInTheDocument()
    expect(screen.getByText(/Codes cannot be deleted/)).toBeInTheDocument()
  })

  it('deactivates through the named sub-resource, and never issues a DELETE', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ [LIST]: [ACTIVE], [`${LIST}/IT/deactivate`]: { ...ACTIVE, isActive: false } }, recorded)

    renderPage(<ReferenceDataPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Deactivate' }))

    await waitFor(() => {
      expect(recorded.some((r) => r.method === 'POST' && r.url.endsWith('/IT/deactivate'))).toBe(true)
    })
    expect(recorded.filter((r) => r.method === 'DELETE')).toHaveLength(0)
  })

  it('lets the names be corrected but not the code, because the code is the foreign key', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ [LIST]: [ACTIVE], [`${LIST}/IT`]: { ...ACTIVE, nameEn: 'Information technology' } }, recorded)

    renderPage(<ReferenceDataPage />)

    // Editable: both names, per row.
    const english = await screen.findByLabelText('Name (English) — IT')
    await userEvent.clear(english)
    await userEvent.type(english, 'Information technology')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      const put = recorded.find((r) => r.method === 'PUT')
      expect(put?.url).toContain('/reference/categories/IT')
      expect(JSON.parse(put!.body).nameEn).toBe('Information technology')
    })

    // Not editable: the code has no input at all. D-28 - renaming it would silently change what a
    // historical award record says it was for, and there is no cascade to follow.
    expect(screen.queryByDisplayValue('IT')).not.toBeInTheDocument()
    // The control, so the assertion above is about the code and not about the row being read-only:
    // the row's own name field IS an input holding its value.
    expect(screen.getByDisplayValue('تقنية المعلومات')).toBeInTheDocument()
  })

  it('carries a document type\'s flags through a rename instead of clearing them', async () => {
    const recorded: RecordedRequest[] = []
    const TYPES = '/api/v1/admin/reference/document-types'
    const CR = { code: 'CR', nameAr: 'السجل التجاري', nameEn: 'Commercial registration', isActive: true, isRequired: true, expiryTracked: true }
    restore = mockFetch({ [TYPES]: [CR], [`${TYPES}/CR`]: CR }, recorded)

    renderPage(<ReferenceDataPage />)

    await userEvent.click(screen.getByRole('combobox', { name: 'Reference table' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Document types' }))

    const arabic = await screen.findByLabelText('Name (Arabic) — CR')
    await userEvent.clear(arabic)
    await userEvent.type(arabic, 'السجل التجارى')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      const put = recorded.find((r) => r.method === 'PUT')
      expect(put).toBeDefined()
      // Omitted would mean "not required" to the server, which would silently make a mandatory
      // document optional because somebody fixed a spelling.
      expect(JSON.parse(put!.body)).toMatchObject({ isRequired: true, expiryTracked: true })
    })
  })

  it('names the duplicate-code rule rather than reporting a generic failure', async () => {
    restore = mockFetch({
      [LIST]: [ACTIVE],
      [`${LIST}/IT`]: { __status: 409, code: 'DUPLICATE_RESOURCE' },
    })

    renderPage(<ReferenceDataPage />)

    await userEvent.type(await screen.findByLabelText('Code'), 'IT')
    await userEvent.type(screen.getByLabelText('Name (English)'), 'IT again')
    await userEvent.type(screen.getByLabelText('Name (Arabic)'), 'تقنية')
    await userEvent.click(screen.getByRole('button', { name: 'Add' }))

    expect(await screen.findByText('That code already exists on this table.')).toBeInTheDocument()
  })

  it('falls back to the generic message when the server names no rule', async () => {
    restore = mockFetch({
      [LIST]: [ACTIVE],
      [`${LIST}/NEW`]: { __status: 500 },
    })

    renderPage(<ReferenceDataPage />)

    await userEvent.type(await screen.findByLabelText('Code'), 'NEW')
    await userEvent.type(screen.getByLabelText('Name (English)'), 'New thing')
    await userEvent.type(screen.getByLabelText('Name (Arabic)'), 'جديد')
    await userEvent.click(screen.getByRole('button', { name: 'Add' }))

    // The control for the test above: the duplicate wording is specific to a code the server
    // named, and this one must NOT claim a duplicate.
    expect(await screen.findByText('Could not add the code')).toBeInTheDocument()
    expect(screen.queryByText('That code already exists on this table.')).not.toBeInTheDocument()
  })

  it('says the table is empty rather than rendering a table with no rows', async () => {
    restore = mockFetch({ [LIST]: [] })

    renderPage(<ReferenceDataPage />)

    expect(await screen.findByText('This table has no codes')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })
})
