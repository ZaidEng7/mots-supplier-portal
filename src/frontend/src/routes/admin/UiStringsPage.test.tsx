import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../../test/renderPage'

const { UiStringsPage } = await import('./UiStringsPage')

const LIST = '/api/v1/admin/ui-strings/'

/**
 * SCR-716. The screen searches the compiled i18n bundle, so its interesting behaviour is in the key
 * list rather than in the network: a search that returns everything is a browser hang, and a save
 * offered for an unchanged string writes an override that does nothing but hide a future correction.
 */
describe('UiStringsPage (SCR-716)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('does not search on a single character', async () => {
    // Two-character minimum: one letter matches most of a bundle of over a thousand keys, and
    // rendering that list is the hang this guard exists to prevent.
    restore = mockFetch({ [LIST]: [] })

    renderPage(<UiStringsPage />)
    await userEvent.type(await screen.findByLabelText(/search|بحث/i), 'a')

    expect(screen.queryByRole('listitem')).not.toBeInTheDocument()
  })

  it('lists matching keys and caps the list at fifty', async () => {
    restore = mockFetch({ [LIST]: [] })

    renderPage(<UiStringsPage />)
    // A prefix broad enough to match far more than the cap, so the cap is what is being asserted.
    await userEvent.type(await screen.findByLabelText(/search|بحث/i), 'e')
    await userEvent.type(screen.getByLabelText(/search|بحث/i), 'r')

    const items = screen.getAllByRole('listitem')
    expect(items.length).toBeGreaterThan(0)
    expect(items.length).toBeLessThanOrEqual(50)
  })

  it('shows the shipped string and pre-fills the editor with it', async () => {
    restore = mockFetch({ [LIST]: [] })

    renderPage(<UiStringsPage />)
    await userEvent.type(await screen.findByLabelText(/search|بحث/i), 'common.loading')
    await userEvent.click(screen.getAllByRole('button', { name: /common\.loading/ })[0])

    // Pre-filled rather than blank: an administrator fixing one word should not retype the sentence
    // around it, which is how a correction introduces a second error.
    const value = screen.getByLabelText(/replacement text|النص المُعدَّل/i) as HTMLInputElement
    expect(value.value.length).toBeGreaterThan(0)
  })

  it('refuses to save a value identical to the shipped string', async () => {
    // An override equal to the shipped text is invisible in the product and permanent in the data:
    // it would keep overriding a string that someone later corrects at source.
    restore = mockFetch({ [LIST]: [] })

    renderPage(<UiStringsPage />)
    await userEvent.type(await screen.findByLabelText(/search|بحث/i), 'common.loading')
    await userEvent.click(screen.getAllByRole('button', { name: /common\.loading/ })[0])

    expect(screen.getByRole('button', { name: /save override|حفظ التعديل/i })).toBeDisabled()
  })

  it('saves an edited value against the selected language and key', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({
      [LIST]: [],
      '/api/v1/admin/ui-strings/ar/common.loading': { language: 'ar', key: 'common.loading', value: 'جارٍ' },
    }, recorded)

    renderPage(<UiStringsPage />)
    await userEvent.type(await screen.findByLabelText(/search|بحث/i), 'common.loading')
    await userEvent.click(screen.getAllByRole('button', { name: /common\.loading/ })[0])

    const value = screen.getByLabelText(/replacement text|النص المُعدَّل/i)
    await userEvent.clear(value)
    await userEvent.type(value, 'Loading, please wait')
    await userEvent.click(screen.getByRole('button', { name: /save override|حفظ التعديل/i }))

    const put = recorded.find((r) => r.method === 'PUT')
    expect(put?.url).toContain('/ui-strings/ar/common.loading')
    expect(JSON.parse(put!.body)).toEqual({ value: 'Loading, please wait' })
  })

  it('says an empty override list is the correct state, not a failure', async () => {
    restore = mockFetch({ [LIST]: [] })

    renderPage(<UiStringsPage />)

    // No overrides means the product reads exactly as it was built, which is right for a fresh
    // deployment - so this must not look like the error case below.
    expect(await screen.findByText(/no overrides|لا توجد/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /try again|إعادة المحاولة/i })).not.toBeInTheDocument()
  })

  it('lists existing overrides and offers restore for each', async () => {
    restore = mockFetch({
      [LIST]: [{ language: 'ar', key: 'common.loading', value: 'جارٍ التحميل…' }],
    })

    renderPage(<UiStringsPage />)

    expect(await screen.findByText('common.loading')).toBeInTheDocument()
    expect(screen.getByText('جارٍ التحميل…')).toBeInTheDocument()
    // "Restore", not "delete": the click brings back the shipped string, and naming it after the
    // storage would make an administrator hesitate over the one action that is always safe.
    expect(screen.getByRole('button', { name: /restore|استعادة/i })).toBeInTheDocument()
  })

  it('offers a retry when the overrides cannot be listed', async () => {
    restore = mockFetch({ [LIST]: { __status: 500 } })

    renderPage(<UiStringsPage />)

    expect(await screen.findByRole('button', { name: /try again|إعادة المحاولة/i })).toBeInTheDocument()
  })

  it('says so when a save is refused', async () => {
    restore = mockFetch({
      [LIST]: [],
      '/api/v1/admin/ui-strings/ar/common.loading': { __status: 500 },
    })

    renderPage(<UiStringsPage />)
    await userEvent.type(await screen.findByLabelText(/search|بحث/i), 'common.loading')
    await userEvent.click(screen.getAllByRole('button', { name: /common\.loading/ })[0])

    const value = screen.getByLabelText(/replacement text|النص المُعدَّل/i)
    await userEvent.clear(value)
    await userEvent.type(value, 'Loading, please wait')
    await userEvent.click(screen.getByRole('button', { name: /save override|حفظ التعديل/i }))

    expect(await screen.findByText(/could not save|تعذّر/i)).toBeInTheDocument()
  })

  it('clears the editor after a successful save', async () => {
    // An editor still holding the key it just saved invites a second save of the same text, and the
    // list below is the confirmation that it took.
    restore = mockFetch({
      [LIST]: [],
      '/api/v1/admin/ui-strings/ar/common.loading': { language: 'ar', key: 'common.loading', value: 'x' },
    })

    renderPage(<UiStringsPage />)
    await userEvent.type(await screen.findByLabelText(/search|بحث/i), 'common.loading')
    await userEvent.click(screen.getAllByRole('button', { name: /common\.loading/ })[0])

    const value = screen.getByLabelText(/replacement text|النص المُعدَّل/i)
    await userEvent.clear(value)
    await userEvent.type(value, 'Loading, please wait')
    await userEvent.click(screen.getByRole('button', { name: /save override|حفظ التعديل/i }))

    await screen.findByText(/override saved|حُفظ التعديل/i)
    expect(screen.queryByLabelText(/replacement text|النص المُعدَّل/i)).not.toBeInTheDocument()
  })

  it('restores the shipped string for one override', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({
      [LIST]: [{ language: 'ar', key: 'common.loading', value: 'جارٍ التحميل…' }],
      '/api/v1/admin/ui-strings/ar/common.loading': { __byMethod: { DELETE: {} } },
    }, recorded)

    renderPage(<UiStringsPage />)
    await userEvent.click(await screen.findByRole('button', { name: /restore|استعادة/i }))

    expect(recorded.some((r) => r.method === 'DELETE' && r.url.endsWith('/ar/common.loading'))).toBe(true)
    expect(await screen.findByText(/original text restored|أُعيد النص الأصلي/i)).toBeInTheDocument()
  })

  it('says so when the restore is refused', async () => {
    restore = mockFetch({
      [LIST]: [{ language: 'ar', key: 'common.loading', value: 'جارٍ التحميل…' }],
      '/api/v1/admin/ui-strings/ar/common.loading': { __byMethod: { DELETE: { __status: 500 } } },
    })

    renderPage(<UiStringsPage />)
    await userEvent.click(await screen.findByRole('button', { name: /restore|استعادة/i }))

    expect(await screen.findByText(/could not restore|تعذّر/i)).toBeInTheDocument()
  })

  it('abandons a selection on cancel', async () => {
    restore = mockFetch({ [LIST]: [] })

    renderPage(<UiStringsPage />)
    await userEvent.type(await screen.findByLabelText(/search|بحث/i), 'common.loading')
    await userEvent.click(screen.getAllByRole('button', { name: /common\.loading/ })[0])
    await userEvent.click(screen.getByRole('button', { name: /^cancel|^إلغاء/i }))

    expect(screen.queryByLabelText(/replacement text|النص المُعدَّل/i)).not.toBeInTheDocument()
  })
})
