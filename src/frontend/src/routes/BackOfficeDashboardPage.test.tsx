import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { renderPage } from '../test/renderPage'

const { BackOfficeDashboardPage } = await import('./BackOfficeDashboardPage')
const { useAuthStore } = await import('../lib/authStore')

/**
 * T-084. This is the shared back-office landing page, and it renders entirely from the ACCESS TOKEN's
 * claims - no queries. So what is worth asserting is that it reads them and survives their absence: a
 * dashboard that throws on a session whose token has no permissions would lock a persona out of the shell
 * rather than showing them an empty one.
 */
describe('BackOfficeDashboardPage', () => {
  afterEach(() => {
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle', expired: false, lastEmail: null })
  })

  it('greets the signed-in user and lists the permissions their token carries', () => {
    useAuthStore.setState({
      accessToken: 'token',
      status: 'authenticated',
      claims: { userId: 'u-1', email: 'officer@example.test', permissions: ['rfq.read', 'rfq.create'] },
    })

    renderPage(<BackOfficeDashboardPage />)

    expect(screen.getByText(/officer@example.test/)).toBeInTheDocument()
    expect(screen.getByText('rfq.read')).toBeInTheDocument()
    expect(screen.getByText('rfq.create')).toBeInTheDocument()
  })

  it('renders for a session with no claims at all rather than throwing', () => {
    // The case that matters: claims come from decoding a JWT, and decodeClaims returns null for anything it
    // cannot parse. A page that assumed an object would take the whole shell down with it.
    useAuthStore.setState({ accessToken: 'token', status: 'authenticated', claims: null })

    renderPage(<BackOfficeDashboardPage />)

    expect(screen.getByText('—')).toBeInTheDocument()
  })
})
