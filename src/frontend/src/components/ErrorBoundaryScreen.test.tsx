// The refusal a shell shows in its own place points each persona back to its own home.
//
// The defect this closes. When the supplier layout or the back-office layout refused a session, it rendered this screen where
// the shell would have been, and the screen was a code and a sentence with nothing to click. A supplier who followed a shared
// /back-office link, or a member of staff who followed a supplier's /dashboard link, reached a page with no sidebar, no top bar
// and no link, and the only way out was the browser's own back button or typing an address.
//
// THE DENOMINATOR is the two personas the layouts refuse, written as a two-row table and asserted before the rule: a supplier
// session, which carries a supplierId, and a staff session, which does not. The two rows must name two different homes,
// because a link that sent both to the same place would pass a check of each row taken alone.
//
// THE RULE renders the 403 for each session and expects exactly one link on the screen, pointing at that persona's home. The
// router's Link is replaced by a plain anchor whose href is the `to` it was given, so the assertion reads the destination the
// screen chose rather than whatever a router would resolve it to.
//
// THE CONTROL is the matcher on both ends. The screen as it was - the code and the sentence and no anchor - reports no link at
// all, and the same screen with a link to the other persona's home reports that other address. A matcher that found the right
// link in everything would keep both rows green.

import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { cleanup, render, screen, within } from '@testing-library/react'
import { useAuthStore } from '../lib/authStore'
import type { AuthClaims } from '../lib/authStore'
import { ErrorBoundaryScreen } from './ErrorBoundaryScreen'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children, ...rest }: Readonly<{ to: string; children: ReactNode }>) => <a href={to} {...rest}>{children}</a>,
}))

interface Persona {
  persona: string
  claims: AuthClaims
  home: string
}

const PERSONAS: readonly Persona[] = [
  {
    persona: 'supplier',
    claims: { userId: 'user-supplier', email: 'owner@supplier.test', supplierId: 'SUP-2026-000039', permissions: ['proposal.submit'] },
    home: '/dashboard',
  },
  {
    persona: 'staff',
    claims: { userId: 'user-staff', email: 'officer@buyer.test', organizationId: 'ORG-1', permissions: ['rfq.read'] },
    home: '/back-office/dashboard',
  },
]

const linkHrefs = (container: HTMLElement) =>
  within(container).queryAllByRole('link').map((link) => link.getAttribute('href'))

const signIn = (claims: AuthClaims) =>
  useAuthStore.setState({ accessToken: 'token', claims, status: 'authenticated', expired: false })

afterEach(() => {
  cleanup()
  useAuthStore.getState().clearSession()
})

describe('the 403 screen points home', () => {
  it('covers two personas with two different homes', () => {
    expect(PERSONAS.map(({ persona, home }) => [persona, home])).toEqual([
      ['supplier', '/dashboard'],
      ['staff', '/back-office/dashboard'],
    ])
    expect(new Set(PERSONAS.map(({ home }) => home)).size).toBe(2)
  })

  it.each(PERSONAS)('sends a $persona session to $home', ({ claims, home }) => {
    signIn(claims)

    const { container } = render(<ErrorBoundaryScreen code="403" />)

    expect(screen.getByText('403')).toBeVisible()
    expect(linkHrefs(container)).toEqual([home])
  })
})

describe('the link matcher', () => {
  it('finds no link on the screen as it was, and the other address on a link to the other home', () => {
    const [supplier, staff] = PERSONAS

    const before = render(<div><span>403</span><p>errors.forbidden</p></div>)
    expect(linkHrefs(before.container)).toEqual([])
    before.unmount()

    const wrongHome = render(<div><span>403</span><a href={staff.home}>nav.home</a></div>)
    expect(linkHrefs(wrongHome.container)).toEqual([staff.home])
    expect(linkHrefs(wrongHome.container)).not.toEqual([supplier.home])
  })
})
