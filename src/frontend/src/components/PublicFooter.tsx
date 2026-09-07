import { useTranslation } from 'react-i18next'
import { Link } from '@tanstack/react-router'

/**
 * The footer that makes SCR-908 reachable.
 *
 * <p><b>Why it exists.</b> `/about` carries the build and commit this deployment is running, and its
 * route comment gives the reason it sits outside every authenticated layout: "the moment a user most
 * needs to say which build they are on is when they cannot sign in." That reasoning was sound and the
 * screen was still unreachable — nothing anywhere in the app linked to it, so the only way in was to
 * type the address. A support answer nobody can navigate to is not a support answer.</p>
 *
 * <p>Mounted at the ROOT, below the Outlet, so it is present on the sign-in page and the error screens
 * as well as inside both shells. Help is here too for the same reason: both shells carry their own Help
 * link, but an anonymous user has no shell at all.</p>
 */
export function PublicFooter() {
  const { t } = useTranslation()

  return (
    <footer
      className="mt-8 flex flex-wrap items-center justify-center gap-x-6 gap-y-2 px-4 py-6 text-[length:var(--text-body-sm)]"
      style={{ borderTop: '1px solid var(--color-border)', color: 'var(--color-text-secondary)' }}
    >
      <Link to="/about" style={{ color: 'var(--color-text-secondary)' }}>{t('about.title')}</Link>
      <Link to="/help" style={{ color: 'var(--color-text-secondary)' }}>{t('help.title')}</Link>
    </footer>
  )
}
