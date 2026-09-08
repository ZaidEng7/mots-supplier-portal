import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, SkeletonList, useToast } from '../components/ui'
import { getNotificationPreferences, setNotificationPreferences, type NotificationPreference } from '../api/notifications'
import { invalidateQuietly } from '../lib/queryClient'

/**
 * SCR-901, `/settings/notifications`, every authenticated persona.
 *
 * <p><b>This screen was refused for two batches, and correctly.</b> D-48/D-52: FR-NOT-004 says "opt-out of
 * non-critical only" and nothing classified the 32 notification types, so building it would have meant
 * deciding inside a preferences screen whether a supplier may switch off the message telling them they have
 * won. D-60 made that decision; phase 1 recorded the classification; this is the screen it was for.</p>
 *
 * <p><b>The always-on types are LISTED, not hidden.</b> Half of what a preferences screen owes its reader is
 * what they will be told regardless — and the four families D-60 protects (invitations, clarification
 * requests, award outcomes, document expiry) are exactly the ones somebody would go looking for first. A
 * screen that omitted them would read as broken to the one user who checked.</p>
 *
 * <p><b>The whole set is sent on save,</b> not a request per toggle: the state of this screen is one
 * decision, and N independent requests would let it end up half-applied with no way to tell.</p>
 */
export function NotificationPreferencesPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const { notify } = useToast()
  const queryClient = useQueryClient()

  const preferencesQuery = useQuery({
    queryKey: ['notification-preferences'],
    queryFn: getNotificationPreferences,
  })

  /** Local edits, keyed by type. Empty until the reader touches something, so a failed save leaves the
   * screen showing what the SERVER holds rather than an optimistic guess. */
  const [pending, setPending] = useState<Record<string, boolean> | null>(null)

  const save = useMutation({
    mutationFn: (mutedTypes: string[]) => setNotificationPreferences(mutedTypes),
    onSuccess: () => {
      setPending(null)
      invalidateQuietly(queryClient, { queryKey: ['notification-preferences'] })
      notify({ kind: 'success', title: t('notificationPreferences.saved') })
    },
    onError: () => notify({ kind: 'danger', title: t('notificationPreferences.saveFailed') }),
  })

  if (preferencesQuery.isPending) return <SkeletonList label={t('common.loading')} />
  if (preferencesQuery.isError || !preferencesQuery.data) {
    return (
      <Card title={t('notificationPreferences.title')}>
        <p>{t('notificationPreferences.loadFailed')}</p>
      </Card>
    )
  }

  const types = preferencesQuery.data.types
  const muteable = types.filter((type) => type.muteable)
  const alwaysOn = types.filter((type) => !type.muteable)

  const isMuted = (type: NotificationPreference) => pending?.[type.type] ?? type.muted
  const dirty = pending !== null && Object.entries(pending).some(([type, muted]) =>
    muted !== (types.find((candidate) => candidate.type === type)?.muted ?? false))

  const toggle = (type: NotificationPreference) =>
    setPending((current) => ({ ...(current ?? {}), [type.type]: !isMuted(type) }))

  const onSave = () => save.mutate(muteable.filter(isMuted).map((type) => type.type))

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('notificationPreferences.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('notificationPreferences.subtitle')}
        </p>
      </div>

      <Card title={t('notificationPreferences.optional')}>
        <p className="mb-4 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('notificationPreferences.optionalHint')}
        </p>

        <ul className="flex flex-col gap-3">
          {muteable.map((type) => (
            <li key={type.type} className="flex items-center justify-between gap-4">
              <label className="flex items-center gap-3" htmlFor={`notification-${type.type}`}>
                <input
                  id={`notification-${type.type}`}
                  type="checkbox"
                  checked={!isMuted(type)}
                  onChange={() => toggle(type)}
                />
                <span style={{ color: 'var(--color-text-primary)' }}>
                  {isArabic ? type.titleAr : type.titleEn}
                </span>
              </label>
            </li>
          ))}
        </ul>

        <div className="mt-6 flex items-center gap-3">
          <Button onClick={onSave} disabled={!dirty || save.isPending}>
            {t('notificationPreferences.save')}
          </Button>
          {dirty ? (
            <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('notificationPreferences.unsaved')}
            </span>
          ) : null}
        </div>
      </Card>

      {/* Listed, not hidden. D-60's four families are the ones a user would look for first, and a screen
          that left them out would read as broken to exactly that reader. */}
      <Card title={t('notificationPreferences.alwaysOn')}>
        <p className="mb-4 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('notificationPreferences.alwaysOnHint')}
        </p>
        <ul className="flex flex-wrap gap-2">
          {alwaysOn.map((type) => (
            <li key={type.type}>
              <Badge tone="info">{isArabic ? type.titleAr : type.titleEn}</Badge>
            </li>
          ))}
        </ul>
      </Card>
    </div>
  )
}
