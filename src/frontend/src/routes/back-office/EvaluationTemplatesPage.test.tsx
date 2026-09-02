import { afterEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../../test/renderPage'

const { EvaluationTemplatesPage } = await import('./EvaluationTemplatesPage')

const DRAFT_TEMPLATE = {
  id: 'tpl-1', familyId: 'fam-1', version: 1, nameAr: 'قالب', nameEn: 'Quality Template',
  status: 'Draft', isReferenced: false,
  criteria: [
    { id: 'c-1', nameAr: 'جودة', nameEn: 'Quality', dimension: 'Technical', weight: 60, maxScore: 10, threshold: null, scoringType: 'Numeric', guidanceAr: null, guidanceEn: null, sortOrder: 1 },
  ],
}

const REFERENCED_TEMPLATE = {
  id: 'tpl-2', familyId: 'fam-2', version: 1, nameAr: 'قالب مرجعي', nameEn: 'Bound Template',
  status: 'Active', isReferenced: true,
  criteria: [
    { id: 'c-2', nameAr: 'سعر', nameEn: 'Price', dimension: 'Commercial', weight: 100, maxScore: 10, threshold: null, scoringType: 'Numeric', guidanceAr: null, guidanceEn: null, sortOrder: 1 },
  ],
}

/** FEAT-11.1: covers the invariants this page exists to surface - the domain's weight-sum
 * rejection reaching the user as a real toast (not just passing server-side), and a referenced
 * template's edit affordances being replaced by Fork rather than merely hidden. */
describe('EvaluationTemplatesPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders a template with its status, weight total, and criteria', async () => {
    restore = mockFetch({ '/api/v1/evaluation-templates': [DRAFT_TEMPLATE] })

    renderPage(<EvaluationTemplatesPage />)

    expect(await screen.findByText('Quality Template (v1)')).toBeInTheDocument()
    expect(screen.getByText('Draft')).toBeInTheDocument()
    expect(screen.getByText('Weight total: 60')).toBeInTheDocument()
    expect(screen.getByText('Quality')).toBeInTheDocument()
  })

  it('creating a template shows a success toast', async () => {
    restore = mockFetch({ '/api/v1/evaluation-templates': [] })

    renderPage(<EvaluationTemplatesPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'New Template' }))
    const dialog = await screen.findByRole('dialog')
    const [arName, enName] = within(dialog).getAllByRole('textbox')
    await userEvent.type(arName, 'قالب جديد')
    await userEvent.type(enName, 'New Template')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Template created')).toBeInTheDocument()
  })

  /** Reproduces the domain's real rejection text (EvaluationTemplate.Activate's own message
   * shape - "Criterion weights must sum to exactly 100...") arriving through
   * EvaluationTemplateApiError, not a client-invented validation string. mockFetch is stateless
   * and always returns 200 (renderPage.tsx), so a non-200 refusal needs a custom fetch mock. */
  it('shows the domain weight-sum rejection message when adding a criterion fails', async () => {
    const original = globalThis.fetch
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      if (url.includes('/criteria')) {
        return new Response(JSON.stringify({ code: 'INVALID_STATE', detail: 'Adding this criterion would exceed 100% before activation; current total would be 110.' }), { status: 409 })
      }
      if (url.includes('/api/v1/evaluation-templates')) {
        return new Response(JSON.stringify([DRAFT_TEMPLATE]), { status: 200 })
      }
      throw new Error(`No mock declared for ${url}`)
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<EvaluationTemplatesPage />)

    await userEvent.type(await screen.findByLabelText('Name (English)'), 'Overweight')
    await userEvent.type(screen.getByLabelText('Name (Arabic)'), 'ثقيل')
    await userEvent.type(screen.getByLabelText('Weight'), '50')
    await userEvent.type(screen.getByLabelText('Max score'), '10')
    await userEvent.click(screen.getByRole('button', { name: 'Add criterion' }))

    expect(await screen.findByText('Adding this criterion would exceed 100% before activation; current total would be 110.')).toBeInTheDocument()
  })

  it('hides the edit form on a referenced template and offers Fork instead of Activate/Archive-then-edit', async () => {
    restore = mockFetch({ '/api/v1/evaluation-templates': [REFERENCED_TEMPLATE] })

    renderPage(<EvaluationTemplatesPage />)

    expect(await screen.findByText('Bound Template (v1)')).toBeInTheDocument()
    expect(screen.getByText('Bound to an RFQ')).toBeInTheDocument()
    expect(screen.queryByLabelText('Name (English)')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add criterion' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create new version' })).toBeInTheDocument()
  })
})
