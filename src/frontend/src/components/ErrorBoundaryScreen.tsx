// What a reader sees when a route throws, when an address matches nothing, or when a shell refuses the wrong persona.
//
// The status-to-copy mapping is a lookup rather than a chain of ternaries: the mapping is the point, and a table shows
// it at a glance while a chain makes the reader evaluate conditions to find it.
//
// THE WAY HOME. This screen showed a code and a sentence and nothing else. Where it stands in for a shell - the layouts' 403,
// and the root's own error and not-found screens - it is the whole page, so a supplier who followed a shared /back-office
// link was left with nothing on the page to click. It now carries one link home, chosen from the session the way
// notificationRoutes chooses a prefix: a session with a supplierId goes to the supplier dashboard, any other session to the
// back-office dashboard, and no session to the landing page. The claim decides where the link points and nothing more; the
// destination's own layout still decides who may enter it.

import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../lib/authStore'
import type { AuthClaims } from '../lib/authStore'

function homeFor(claims: AuthClaims | null): '/' | '/dashboard' | '/back-office/dashboard' {
  if (!claims) return '/'
  return claims.supplierId ? '/dashboard' : '/back-office/dashboard'
}

export function ErrorBoundaryScreen({ code }: Readonly<{ code: '404' | '403' | '500' }>) {
  const { t } = useTranslation()
  const claims = useAuthStore((state) => state.claims)
  const MESSAGE_KEYS: Record<string, string> = { '404': 'notFound', '403': 'forbidden' }
  const messageKey = MESSAGE_KEYS[code] ?? 'serverError'

  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-2 py-24 text-center">
      <span className="num text-[length:var(--text-display)] font-[var(--fw-bold)]" style={{ color: 'var(--color-text-muted)' }}>
        {code}
      </span>
      <p className="text-[length:var(--text-h4)]" style={{ color: 'var(--color-text-secondary)' }}>
        {t(`errors.${messageKey}`)}
      </p>
      <Link to={homeFor(claims)} className="mt-2 text-[length:var(--text-body-sm)]">
        {t('nav.home')}
      </Link>
    </div>
  )
}
