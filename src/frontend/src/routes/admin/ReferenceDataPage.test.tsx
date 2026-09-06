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

// The nulls are explicit because the API sends them explicitly: the DTO's optional members serialise
// as `null` for a table that has no such column, and the screen tests `!== null`. A fixture that simply
// omitted them would leave `undefined`, which is NOT null, and would render document-type controls on
// every other tab - a difference no browser pass would notice, since the seeded data is well-formed.
const ACTIVE = { code: 'IT', nameAr: 'تقنية المعلومات', nameEn: 'IT', isActive: true, isRequired: null, expiryTracked: null, isAwardCritical: null }
const RETIRED = { code: 'FAX', nameAr: 'فاكس', nameEn: 'Fax machines', isActive: false, isRequired: null, expiryTracked: null, isAwardCritical: null }

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

/**
 * BRULE-023 and BRULE-016, both on the document-types tab. These two share a property that makes them
 * worth unit tests rather than a click-through: neither does anything visible when you use it. The
 * award-critical flag changes what a SCHEDULED JOB will do to a supplier days or months later, and the
 * category links are recorded and deliberately not applied at all yet. A screen whose controls have no
 * immediate effect is a screen where a wiring mistake looks exactly like correct behaviour.
 */
describe('ReferenceDataPage document types (BRULE-023, BRULE-016)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  const DOC_TYPES = '/api/v1/admin/reference/document-types'
  const LINKS = '/api/v1/admin/document-type-categories'
  const CATEGORIES = '/api/v1/admin/reference/categories'

  const CR = {
    code: 'CR', nameAr: 'السجل التجاري', nameEn: 'Commercial registration',
    isActive: true, isRequired: true, expiryTracked: true, isAwardCritical: true,
  }
  const PROFILE = {
    code: 'PROFILE', nameAr: 'ملف الشركة', nameEn: 'Company profile',
    isActive: true, isRequired: false, expiryTracked: false, isAwardCritical: false,
  }

  async function openDocumentTypes() {
    // Named, not positional: rows on this page carry their own selects, so `findByRole('combobox')`
    // races the row render and can resolve against the wrong control once data arrives.
    await userEvent.click(await screen.findByRole('combobox', { name: /reference table|الجدول المرجعي/i }))
    await userEvent.click(await screen.findByRole('option', { name: /document types|أنواع المستندات/i }))
  }

  function fixtures(overrides: Record<string, unknown> = {}) {
    return {
      [CATEGORIES]: [{ code: 'IT', nameAr: 'تقنية', nameEn: 'IT', isActive: true, isRequired: null, expiryTracked: null, isAwardCritical: null }],
      [DOC_TYPES]: [CR, PROFILE],
      [LINKS]: [{ documentTypeCode: 'CR', categoryCodes: ['IT'] }],
      ...overrides,
    }
  }

  it('marks the award-critical types and offers the opposite action on each', async () => {
    restore = mockFetch(fixtures())

    renderPage(<ReferenceDataPage />)
    await openDocumentTypes()

    // The name is an editable input on this screen, not text, so it is found by its value.
    expect(await screen.findByDisplayValue('Commercial registration')).toBeInTheDocument()
    // One row is flagged and one is not, so each button is the control for the other.
    expect(screen.getByRole('button', { name: /remove award-critical|إلغاء الوسم/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /mark award-critical|تحديده كحرج/i })).toBeInTheDocument()
  })

  it('sends the whole item when it toggles the flag, not the flag alone', async () => {
    // The endpoint is a full update. A PUT carrying only isAwardCritical would blank the names and the
    // expiry rule - and the screen would look right until the next person opened the row.
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ ...fixtures(), [`${DOC_TYPES}/PROFILE`]: { ...PROFILE, isAwardCritical: true } }, recorded)

    renderPage(<ReferenceDataPage />)
    await openDocumentTypes()
    await userEvent.click(await screen.findByRole('button', { name: /mark award-critical|تحديده كحرج/i }))

    const put = recorded.find((r) => r.method === 'PUT' && r.url.endsWith('/PROFILE'))
    expect(JSON.parse(put!.body)).toEqual({
      nameAr: 'ملف الشركة', nameEn: 'Company profile',
      isRequired: false, expiryTracked: false, isAwardCritical: true,
    })
  })

  it('explains that the flag acts through a later job rather than now', async () => {
    // It is the only flag on this screen whose effect is to suspend a live supplier, and it fires days
    // or months later - exactly when nobody remembers setting it.
    restore = mockFetch(fixtures())

    renderPage(<ReferenceDataPage />)
    await openDocumentTypes()

    expect(await screen.findByText(/suspend|تعليق/i)).toBeInTheDocument()
  })

  it('shows the recorded category links per document type', async () => {
    restore = mockFetch(fixtures())

    renderPage(<ReferenceDataPage />)
    await openDocumentTypes()

    await screen.findByDisplayValue('Commercial registration')
    // Chips rather than a multi-select: with three categories, a select hides which ones are on.
    expect(screen.getAllByRole('button', { name: 'IT' }).length).toBe(2)
  })

  it('sends the complete category set when a link is toggled', async () => {
    // The endpoint replaces the set. Sending only the changed code would silently drop the others.
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ ...fixtures(), [`${LINKS}/PROFILE`]: { documentTypeCode: 'PROFILE', categoryCodes: ['IT'] } }, recorded)

    renderPage(<ReferenceDataPage />)
    await openDocumentTypes()
    await screen.findByDisplayValue('Company profile')

    const chips = screen.getAllByRole('button', { name: 'IT' })
    await userEvent.click(chips[chips.length - 1])

    const put = recorded.find((r) => r.method === 'PUT' && r.url.includes('document-type-categories'))
    expect(JSON.parse(put!.body)).toEqual({ categoryCodes: ['IT'] })
  })

  it('says the links are recorded and not yet applied', async () => {
    // BRULE-016 is deliberately inert. An administrator who records links and sees no change in any
    // supplier's required documents would reasonably conclude the screen is broken.
    restore = mockFetch(fixtures())

    renderPage(<ReferenceDataPage />)
    await openDocumentTypes()

    expect(await screen.findByText(/not yet|recorded|لم تُطبَّق/i)).toBeInTheDocument()
  })

  it('offers no award-critical control on a table that has no such flag', async () => {
    // isAwardCritical is null for categories, and a button that writes null back would be a control
    // inventing a value the table does not have.
    restore = mockFetch(fixtures())

    renderPage(<ReferenceDataPage />)

    await screen.findByDisplayValue('IT')
    // The explanatory paragraph below the table is document-types only too, so this asks for the
    // control rather than for the words "award" anywhere on the page.
    expect(screen.queryByRole('button', { name: /award-critical|الوسم كحرج|كحرج للترسية/i })).not.toBeInTheDocument()
  })
})
