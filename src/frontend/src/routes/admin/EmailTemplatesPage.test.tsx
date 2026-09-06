import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../../test/renderPage'

const { EmailTemplatesPage } = await import('./EmailTemplatesPage')

const LIST = '/api/v1/admin/email-templates/'

function template(overrides: Record<string, unknown> = {}) {
  return {
    key: 'supplier.invitation',
    requiredTokens: ['verifyUrl'],
    optionalTokens: ['supplierName'],
    shipped: {
      subjectAr: 'دعوة للتسجيل', subjectEn: 'Invitation to register',
      bodyAr: 'مرحبًا {supplierName}، افتح {verifyUrl}', bodyEn: 'Hello {supplierName}, open {verifyUrl}',
    },
    override: null,
    ...overrides,
  }
}

/**
 * T-076. The failure this screen exists to prevent is silent: an email that loses {verifyUrl} locks the
 * recipient out of the account they are creating, and nothing in the system can tell, because the send
 * succeeded and the body was valid. So the required tokens are shown, and a refusal names which ones.
 */
describe('EmailTemplatesPage (T-076)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows the required tokens for each template', async () => {
    restore = mockFetch({ [LIST]: [template()] })

    renderPage(<EmailTemplatesPage />)

    expect(await screen.findByText(/\{verifyUrl\}/)).toBeInTheDocument()
    expect(screen.getByText(/\{supplierName\}/)).toBeInTheDocument()
  })

  it('marks a template as shipped wording until it is overridden', async () => {
    restore = mockFetch({ [LIST]: [template()] })

    renderPage(<EmailTemplatesPage />)

    expect(await screen.findByText(/shipped|الأصلي/i)).toBeInTheDocument()
    // Revert is offered only where there is something to revert TO: on a shipped row it would answer
    // 404, and calling it "revert" would imply the shipped words were themselves a change.
    expect(screen.queryByRole('button', { name: /restore original|استعادة الأصلي/i })).not.toBeInTheDocument()
  })

  it('offers revert once an override is in force, and shows the override as what is live', async () => {
    restore = mockFetch({
      [LIST]: [template({
        override: { subjectAr: 'دعوة معدّلة', subjectEn: 'Custom invitation', bodyAr: '{verifyUrl}', bodyEn: '{verifyUrl}' },
      })],
    })

    renderPage(<EmailTemplatesPage />)

    expect(await screen.findByText('Custom invitation')).toBeInTheDocument()
    expect(screen.queryByText('Invitation to register')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /restore original|استعادة الأصلي/i })).toBeInTheDocument()
  })

  it('pre-fills the editor with the copy in force rather than an empty form', async () => {
    // An empty editor would make every edit a rewrite from scratch, which is how a token gets dropped.
    restore = mockFetch({ [LIST]: [template()] })

    renderPage(<EmailTemplatesPage />)
    await userEvent.click(await screen.findByRole('button', { name: /^edit|تعديل/i }))

    expect(screen.getByDisplayValue('Invitation to register')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Hello {supplierName}, open {verifyUrl}')).toBeInTheDocument()
  })

  it('names the tokens the server refused, rather than saying only that a save failed', async () => {
    restore = mockFetch({
      [LIST]: [template()],
      '/api/v1/admin/email-templates/supplier.invitation': {
        __status: 422, code: 'MISSING_REQUIRED_TOKENS', tokens: ['verifyUrl'],
      },
    })

    renderPage(<EmailTemplatesPage />)
    await userEvent.click(await screen.findByRole('button', { name: /^edit|تعديل/i }))
    await userEvent.click(screen.getByRole('button', { name: /^save|حفظ/i }))

    // "A token is missing" is not something an administrator can act on; which token is.
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('verifyUrl')
  })

  it('sends both languages of subject and body on save', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({
      [LIST]: [template()],
      '/api/v1/admin/email-templates/supplier.invitation': { subjectAr: 'x', subjectEn: 'x', bodyAr: 'x', bodyEn: 'x' },
    }, recorded)

    renderPage(<EmailTemplatesPage />)
    await userEvent.click(await screen.findByRole('button', { name: /^edit|تعديل/i }))
    await userEvent.click(screen.getByRole('button', { name: /^save|حفظ/i }))

    const put = recorded.find((r) => r.method === 'PUT')
    expect(put).toBeDefined()
    // Both languages every time: a PUT carrying only the edited one would silently blank the other.
    expect(JSON.parse(put!.body)).toEqual({
      subjectAr: 'دعوة للتسجيل', subjectEn: 'Invitation to register',
      bodyAr: 'مرحبًا {supplierName}، افتح {verifyUrl}', bodyEn: 'Hello {supplierName}, open {verifyUrl}',
    })
  })

  it('offers a retry when the templates cannot be listed', async () => {
    restore = mockFetch({ [LIST]: { __status: 500 } })

    renderPage(<EmailTemplatesPage />)

    expect(await screen.findByRole('button', { name: /try again|إعادة المحاولة/i })).toBeInTheDocument()
  })
})
