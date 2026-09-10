import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { MobileTabBar } from '../components/MobileTabBar'
import { AppShell } from './AppShell'
import { SUPPLIER_CHROME, SUPPLIER_NAV } from './navigation'

/**
 * The supplier's side of the product.
 *
 * <p>Every destination now stands in a grouped rail rather than in a header row that had to be read
 * left to right to be searched. What has not changed is the grouping itself: §D3 settled that a
 * supplier arriving to bid should not have to pass the company-profile links to reach the tender ones,
 * and those two groups are the ones the rail draws.</p>
 *
 * <p>Below `md` the rail gives way to the tab bar, which is DESIGN-SYSTEM.md §5.5's answer for this
 * persona and already carries five destinations at its documented cap. The top bar's own disclosure
 * covers the rest of the list at that width.</p>
 */
export function SupplierShell({ children }: Readonly<{ children: ReactNode }>) {
  const { t } = useTranslation()

  return (
    <AppShell
      // `msp-density-supplier` sets --density-body to 16px for everything inside. RECONCILIATION.md's
      // reasoning: this reader is an outside company completing a legal application a few times a year
      // under deadline, not an officer scanning tables all day. The back office keeps the 14px default,
      // so only one of the two shells has to say anything.
      densityClass="msp-density-supplier"
      groups={SUPPLIER_NAV}
      chrome={SUPPLIER_CHROME}
      // Nothing on this side is permissioned in the navigation: a supplier account reaches every
      // destination the rail offers, and the API scopes each one to their own company.
      context={{ can: () => true, inABuyingBody: false }}
      title={t('appName')}
      // The owning Ministry rather than a second copy of the product name: the first draft put
      // `appName` above a subtitle that also read "Supplier portal", which said one thing twice.
      subtitle={t('nav.supplierArea')}
      home={{ to: '/dashboard', label: t('nav.dashboard') }}
      footer={<MobileTabBar />}
    >
      {children}
    </AppShell>
  )
}
