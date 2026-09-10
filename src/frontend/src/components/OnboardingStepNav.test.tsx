import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import { renderPage, mockFetch, type RecordedRequest } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return {
    ...actual,
    useRouterState: () => '/onboarding',
    Link: ({ to, children, ...rest }: { to: string; children: React.ReactNode }) => <a href={to} {...rest}>{children}</a>,
  }
})

const { OnboardingStepNav } = await import('./OnboardingStepNav')

function supplier(overrides: Record<string, unknown> = {}) {
  return {
    supplierCode: 'SUP-2026-000001',
    displayNameAr: 'شركة', displayNameEn: 'Step Nav Co',
    description: null, website: null, logoStorageKey: null, supplierGroup: null,
    onboardingState: 'ProfileInProgress',
    lifecycleState: 'Active',
    currencyCode: null, legalInfo: null, primaryContactPhone: null, termsAcceptedAt: null,
    missingProfileFields: [],
    representatives: [], addresses: [], contacts: [], branches: [], bankAccounts: [], categoryCodes: [],
    ...overrides,
  }
}

/**
 * The five steps of an application, each carrying how it stands.
 *
 * <p>What this replaces is a row of five pills naming the steps and nothing else: a supplier could see
 * where they were and not what was left, so finding the one step still blocking them meant opening all
 * five.</p>
 */
describe('OnboardingStepNav', () => {
  let restore: (() => void) | undefined
  afterEach(() => { restore?.(); restore = undefined })

  const stepCard = (name: string) => screen.getByRole('link', { name: new RegExp(name) })

  it('says what is left on the step that owns it, and Complete on the ones that are done', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me': supplier({ missingProfileFields: ['legalInfo', 'termsAccepted', 'address'] }),
    })

    renderPage(<OnboardingStepNav />)

    // The cards render before the profile arrives, so wait for the status rather than for the card.
    await screen.findByText('2 things left')

    // Company owns legalInfo and termsAccepted: two of the three outstanding fields are its.
    expect(stepCard('Company')).toHaveTextContent('2 things left')
    // Addresses owns the third, on its own.
    expect(stepCard('Addresses')).toHaveTextContent('1 thing left')
    // Offerings owns categoryLink, which is not outstanding.
    expect(stepCard('Offerings')).toHaveTextContent('Complete')
  })

  /**
   * A step with no required field is not complete and not outstanding; it is optional, and saying
   * "Complete" about a page the supplier has never opened would be a claim about work nobody did.
   */
  it('calls a step with nothing required optional, rather than complete', async () => {
    restore = mockFetch({ '/api/v1/suppliers/me': supplier({ missingProfileFields: ['legalInfo'] }) })

    renderPage(<OnboardingStepNav />)

    await screen.findByText('1 thing left')

    expect(stepCard('Contacts')).toHaveTextContent('Optional')
    expect(stepCard('Banking')).toHaveTextContent('Optional')
  })

  /**
   * The denominator. Status is about work the supplier can still do; once the application is with a
   * reviewer, "1 thing left" describes a form they can no longer edit, which is worse than saying
   * nothing. A nav that always printed a status would pass every test above and be wrong here.
   */
  it('says nothing about a step once the application is no longer editable', async () => {
    const calls: RecordedRequest[] = []
    restore = mockFetch({
      '/api/v1/suppliers/me': supplier({ onboardingState: 'UnderReview', missingProfileFields: ['legalInfo'] }),
    }, calls)

    renderPage(<OnboardingStepNav />)

    // Asserting an absence needs proof the profile ARRIVED, or this passes on the first render - before
    // any request resolved - and would pass just as happily against a component that never reads the
    // profile at all. The recorded request is that proof.
    await waitFor(() => expect(calls.some((c) => c.url.includes('/suppliers/me'))).toBe(true))
    await waitFor(() => expect(screen.getAllByRole('link')).toHaveLength(5))

    const company = stepCard('Company')
    expect(company).toHaveTextContent('Company')
    expect(company).not.toHaveTextContent('left')
    expect(screen.queryByText('Optional')).toBeNull()
    expect(screen.queryByText('Complete')).toBeNull()
  })

  it('marks the step being looked at, and only that one', async () => {
    restore = mockFetch({ '/api/v1/suppliers/me': supplier() })

    renderPage(<OnboardingStepNav />)

    const nav = await screen.findByRole('navigation')
    const current = within(nav).getAllByRole('link').filter((a) => a.getAttribute('aria-current') === 'step')

    expect(current).toHaveLength(1)
    expect(current[0]).toHaveTextContent('Company')
  })
})
