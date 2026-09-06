import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { getMeta } from '../api/meta'

/**
 * SCR-044 — planned maintenance notice.
 *
 * <p>A banner rather than a route, and mounted at the root, because a maintenance window is something
 * every page has to be able to say - including the login screen, which is exactly where someone lands
 * when a system is about to go down. A `/maintenance` route would be a page nobody navigates to.</p>
 *
 * <p>Renders nothing at all when no notice is configured, and nothing when the request fails: a
 * failed lookup of "is there an outage" is not itself news, and a banner that appeared on every
 * network hiccup would be trained out of the reader within a day.</p>
 *
 * <p>The message is the operator's own words in the reader's language. No fallback copy is invented
 * when only one language was configured - showing Arabic to an English reader is more use than showing
 * them a sentence the ministry never wrote.</p>
 */
export function MaintenanceBanner() {
  const { i18n } = useTranslation()
  // Long stale time on purpose: this is a notice an operator sets hours ahead, and polling it hard
  // would put a request on every page load to learn nothing.
  const metaQuery = useQuery({ queryKey: ['meta'], queryFn: getMeta, staleTime: 5 * 60 * 1000 })

  const notice = metaQuery.data?.maintenance
  if (!notice) return null

  // Blank counts as absent, not as a message. Configuration providers hand back the empty string for a
  // key that exists and was left unset, and `??` would have chosen it - an empty warning bar across
  // every page for an Arabic reader whenever only the English half was filled in. Caught against the
  // live endpoint, which returns exactly that shape.
  const text = (value: string | null) => (value && value.trim() ? value : null)
  const preferred = i18n.language.startsWith('ar') ? text(notice.messageAr) : text(notice.messageEn)
  const message = preferred ?? text(notice.messageAr) ?? text(notice.messageEn)
  if (!message) return null

  const window = [text(notice.from), text(notice.to)].filter(Boolean).join(' — ')

  return (
    <div
      role="status"
      className="px-4 py-2 text-[length:var(--text-body-sm)]"
      style={{ backgroundColor: 'var(--color-warning-bg)', color: 'var(--color-warning-fg)' }}
    >
      <span>{message}</span>
      {window ? <span className="ms-2 opacity-80">{window}</span> : null}
    </div>
  )
}
