import { describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { render, screen } from '@testing-library/react'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children }: { to: string; children: ReactNode }) => <a href={to}>{children}</a>,
}))

const { PublicFooter } = await import('./PublicFooter')
const { useDeclareShellMounted } = await import('./shellPresence')
const { useAuthStore } = await import('../lib/authStore')

/** Stands in for a shell: the thing a shell does to this footer is declare itself. */
function ShellProbe() {
  useDeclareShellMounted()
  return <div>a shell</div>
}

/**
 * There are two of these and only one may be on screen.
 *
 * <p><b>The defect.</b> This footer was mounted at the root, below the Outlet, so inside an
 * authenticated shell it drew BELOW the whole shell - and the shell's sidebar is a full viewport tall.
 * On any page shorter than the window a reader got two links stranded in a band of empty white running
 * the full width of the screen, under the rail as well as under the page. It is the first thing in the
 * screenshots that made the product look unfinished.</p>
 *
 * <p>The shell now renders its own, in the content column. This one stands down while that is on
 * screen, and it decides that from the shell saying so rather than from the session: a signed-in reader
 * on `/about` is authenticated and has no shell, and that page is reached FROM this footer.</p>
 */
describe('the public footer', () => {
  it('carries About when there is no shell', () => {
    render(<PublicFooter />)

    expect(screen.getByRole('link', { name: 'about.title' })).toBeInTheDocument()
  })

  /**
   * Help is under the supplier shell, which is authenticated, so a reader with no session following
   * it arrived at the sign-in form - from the landing page, and from the sign-in page itself. The
   * link is now shown only to someone who can reach what it points at.
   */
  it('does not offer Help to a reader with no session', () => {
    render(<PublicFooter />)

    expect(screen.queryByRole('link', { name: 'help.title' })).toBeNull()
  })

  it('offers Help once there is a session, because an error screen has no rail either', () => {
    useAuthStore.setState({ claims: { email: 'someone@mots.local', permissions: [] } as never })

    render(<PublicFooter />)

    expect(screen.getByRole('link', { name: 'help.title' })).toBeInTheDocument()

    useAuthStore.setState({ claims: null })
  })

  it('stands down while a shell is on screen', () => {
    const { container } = render(<><ShellProbe /><PublicFooter /></>)

    expect(screen.getByText('a shell')).toBeInTheDocument()
    expect(container.querySelector('footer')).toBeNull()
  })

  it('comes back when the shell goes away', () => {
    // Signing out returns a reader to /login, which has no shell and does need the build number.
    const { rerender, container } = render(<><ShellProbe /><PublicFooter /></>)
    expect(container.querySelector('footer')).toBeNull()

    rerender(<><PublicFooter /></>)
    expect(container.querySelector('footer')).not.toBeNull()
  })

  it('drops Help inside a shell, because the rail already carries it', () => {
    // One of the redundancies the Rams audit named: the same destination offered twice, six inches
    // apart. An anonymous reader has no rail, which is why the root copy keeps it.
    render(<PublicFooter inShell />)

    expect(screen.getByRole('link', { name: 'about.title' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'help.title' })).toBeNull()
  })
})
