import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { AppShell } from './AppShell'
import { BACK_OFFICE_CHROME, BACK_OFFICE_NAV } from './navigation'
import { useAuthStore } from '../lib/authStore'

/**
 * One permission lookup for the whole shell.
 *
 * <p>This was thirteen separate `useAuthStore` subscriptions, each repeating
 * `claims?.permissions.includes(x) ?? false`. Reading the claim list once and returning a predicate
 * says the same thing in one place, and adding a destination no longer means adding a subscription.</p>
 */
function usePermissions(): (permission: string) => boolean {
  const permissions = useAuthStore((state) => state.claims?.permissions)
  return (permission) => permissions?.includes(permission) ?? false
}

/**
 * Whether this account belongs to a buying body.
 *
 * <p>BRULE-029 scopes every procurement query by the caller's organization, and two personas
 * deliberately have none: the bootstrap administrator, and `ministry_viewer`, whose grant is
 * cross-organization by BRULE-086 and would be narrowed by pinning it to one. For those two the
 * procurement screens answer 404 - correctly, there is nothing in scope to return - so the rows that
 * lead to them are not offered. Hiding rather than widening is the reversible half of the choice:
 * whether a platform administrator should read across every organization's live procurements is a
 * policy question, and it should not be settled by a navigation gate.</p>
 */
function useHasOrganization(): boolean {
  return useAuthStore((state) => Boolean(state.claims?.organizationId))
}

/**
 * The internal side of the product, for Ministry staff.
 *
 * <p><b>What changed.</b> Thirty-one destinations were listed as one wrapping row of
 * identically-coloured links, with no grouping and no marker for the page you were on. Three of them
 * had shipped permissioned and unreachable because nothing linked to them at all, found by hand rather
 * than by any instrument. The destinations now live in `navigation.ts` as data, grouped by the question
 * a member of staff arrived with, and `reachability.test.tsx` fails when a route no navigation offers
 * has no written reason for it.</p>
 *
 * <p>The rail is dark in both themes. That is what tells a member of staff at a glance which side of
 * the product they are on, and it is the one thing about this shell that was already working.</p>
 */
export function BackOfficeShell({ children }: Readonly<{ children: ReactNode }>) {
  const can = usePermissions()
  const inABuyingBody = useHasOrganization()
  const { t } = useTranslation()

  return (
    <AppShell
      groups={BACK_OFFICE_NAV}
      chrome={BACK_OFFICE_CHROME}
      context={{ can, inABuyingBody }}
      title={t('appName')}
      subtitle={t('nav.backOffice')}
      home={{ to: '/back-office/dashboard', label: t('nav.dashboard') }}
      searchTo="/back-office/search"
    >
      {children}
    </AppShell>
  )
}
