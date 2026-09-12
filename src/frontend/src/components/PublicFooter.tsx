import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../lib/authStore'
import { Link } from '@tanstack/react-router'
import { useShellMounted } from './shellPresence'

/**
 * The footer that makes SCR-908 reachable.
 *
 * <p><b>Why it exists.</b> `/about` carries the build and commit this deployment is running, and its
 * route comment gives the reason it sits outside every authenticated layout: "the moment a user most
 * needs to say which build they are on is when they cannot sign in." That reasoning was sound and the
 * screen was still unreachable — nothing anywhere in the app linked to it, so the only way in was to
 * type the address. A support answer nobody can navigate to is not a support answer.</p>
 *
 * <p><b>Two footers, one at a time.</b> This one is mounted at the root, so an anonymous reader on the
 * sign-in page or an error screen has it. Inside a shell the same links are rendered by AppShell, in the
 * content column, and this one stands down.</p>
 *
 * <p>At the root it sat BELOW the shell, and the shell's sidebar is a full viewport tall - so on any
 * page shorter than the window the footer appeared as two links stranded in a band of empty white
 * running the full width of the screen, under the rail as well as under the page. That is what a reader
 * saw on every short screen in the product.</p>
 *
 * <p><b>Help is dropped inside a shell.</b> Both shells carry their own Help destination in the rail,
 * so offering it again six inches below is the same journey twice - one of the redundancies the Rams
 * audit named. An anonymous reader has no rail, so at the root it stays.</p>
 */
export function PublicFooter({ inShell = false }: Readonly<{ inShell?: boolean }>) {
  const { t } = useTranslation()
  const shellMounted = useShellMounted()
  // Help lives under the supplier shell, which is authenticated. Offered to a reader with no
  // session it was a link to the sign-in page: the landing page and the sign-in page itself both
  // carried it, so the first thing an unregistered supplier could click for help bounced them to a
  // form. Shown only to someone who can actually reach it, until there is a public help route.
  const signedIn = useAuthStore((state) => state.claims !== null)

  if (!inShell && shellMounted) return null

  return (
    <footer
      className={`flex flex-wrap items-center gap-x-6 gap-y-2 px-4 py-6 text-[length:var(--text-body-sm)] ${inShell ? 'justify-start sm:px-6' : 'mt-8 justify-center'}`}
      style={{ borderTop: '1px solid var(--color-border)', color: 'var(--color-text-secondary)' }}
    >
      <Link to="/about" style={{ color: 'var(--color-text-secondary)' }}>{t('about.title')}</Link>
      {inShell || !signedIn ? null : (
        <Link to="/help" style={{ color: 'var(--color-text-secondary)' }}>{t('help.title')}</Link>
      )}
    </footer>
  )
}
