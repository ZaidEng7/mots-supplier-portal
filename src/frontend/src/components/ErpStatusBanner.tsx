// SCR-045: the ERP-degraded banner, in global chrome.
//
// The inventory asks for a non-blocking banner for authenticated users. Two partial surfaces existed - a tile on
// SCR-700 and a card on the supplier dashboard - and neither is chrome, so a buyer in the middle of an RFQ saw nothing
// while sync was failing. This is the missing half.
//
// Non-blocking, and it renders nothing when there is nothing to say, because a banner that is always present is chrome
// nobody reads. It also renders nothing while the query is in flight or has failed: a status check that cannot answer
// must not assert that everything is fine, and must not cry wolf either - so it stays silent, and the surfaces that own
// the detail, SCR-700 and SCR-723, are where a failure is diagnosed.
//
// Polled rather than fetched once: a session outlives a sync failure, and a banner that only appears if you happen to
// reload is not a banner.
//
// Degraded wins when both conditions are true: an actual failed sync is the more urgent of the two, and stacking two
// banners in chrome is how chrome stops being read. It is role="status" rather than "alert", because the inventory's
// own wording is non-blocking, so it should not interrupt a screen reader mid-sentence.

import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { getSystemStatus } from '../api/systemStatus'

export function ErpStatusBanner() {
  const { t } = useTranslation()

  const { data } = useQuery({
    queryKey: ['system-status'],
    queryFn: getSystemStatus,
    refetchInterval: 60_000,
    retry: false,
  })

  if (!data) return null
  if (!data.erpDegraded && !data.erpNotConfigured) return null

  const message = data.erpDegraded ? t('erpBanner.degraded') : t('erpBanner.notConfigured')

  return (
    <div
      role="status"
      className="px-4 py-2 text-[length:var(--text-body-sm)] sm:px-6"
      style={{ backgroundColor: 'var(--color-warning-bg)', color: 'var(--color-warning-fg)', borderBottom: '1px solid var(--color-border)' }}
    >
      {message}
    </div>
  )
}
