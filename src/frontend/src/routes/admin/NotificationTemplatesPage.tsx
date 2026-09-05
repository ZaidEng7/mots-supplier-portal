import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Field, Input, SkeletonList, useToast } from '../../components/ui'
import { formatDateTime } from '../../lib/datetime'
import {
  listNotificationTemplates,
  revertNotificationTemplate,
  updateNotificationTemplate,
  type NotificationTemplate,
  type NotificationTemplateDraft,
} from '../../api/notificationTemplates'

/**
 * SCR-715, `/back-office/notification-templates`, `system_admin`, P1 (FR-ADM-007).
 *
 * <p>The 29 notification texts were a compiled catalogue, so rewording one - a sentence a supplier
 * reads when their application is rejected - was a redeploy.</p>
 *
 * <p>Each type shows the shipped words next to the current ones. That is what makes revert honest:
 * an administrator can see what they would be restoring before they do it.</p>
 *
 * <p>The available tokens come from the server per type. A token the payload cannot fill reaches the
 * supplier as the literal characters <code>{'{price}'}</code>, so the write refuses it and this screen
 * names which ones were wrong rather than reporting a generic failure.</p>
 */
export function NotificationTemplatesPage() {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'
  const queryClient = useQueryClient()
  const { notify } = useToast()

  const query = useQuery({ queryKey: ['notification-templates'], queryFn: listNotificationTemplates })

  const [drafts, setDrafts] = useState<Record<string, NotificationTemplateDraft>>({})
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [expanded, setExpanded] = useState<string | null>(null)

  const replace = (updated: NotificationTemplate) => {
    queryClient.setQueryData<NotificationTemplate[]>(['notification-templates'], (prev) =>
      prev?.map((template) => (template.type === updated.type ? updated : template)),
    )
    setDrafts((prev) => {
      const next = { ...prev }
      delete next[updated.type]
      return next
    })
    setErrors((prev) => {
      const next = { ...prev }
      delete next[updated.type]
      return next
    })
  }

  const saveMutation = useMutation({
    mutationFn: ({ type, draft }: { type: string; draft: NotificationTemplateDraft }) =>
      updateNotificationTemplate(type, draft),
    onSuccess: (updated) => {
      replace(updated)
      notify({ kind: 'success', title: t('notificationTemplates.saved') })
    },
    onError: (error: Error & { tokens?: string[] }, variables) => {
      setErrors((prev) => ({
        ...prev,
        [variables.type]:
          error.tokens && error.tokens.length > 0
            ? t('notificationTemplates.errors.unknownTokens', {
                tokens: error.tokens.map((token) => `{${token}}`).join(', '),
              })
            : t('notificationTemplates.errors.saveFailed'),
      }))
    },
  })

  const revertMutation = useMutation({
    mutationFn: (type: string) => revertNotificationTemplate(type),
    onSuccess: (updated) => {
      replace(updated)
      notify({ kind: 'success', title: t('notificationTemplates.reverted') })
    },
    onError: () => notify({ kind: 'danger', title: t('notificationTemplates.errors.revertFailed') }),
  })

  if (query.isLoading) return <SkeletonList label={t('common.loading')} />

  if (query.isError || !query.data) {
    return (
      <Card title={t('notificationTemplates.title')}>
        <p>{t('notificationTemplates.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => void query.refetch()}>{t('notificationTemplates.retry')}</Button>
      </Card>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('notificationTemplates.title')}
        </h1>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('notificationTemplates.subtitle')}</p>
      </div>

      {query.data.map((template) => {
        const draft: NotificationTemplateDraft = drafts[template.type] ?? {
          titleAr: template.titleAr,
          titleEn: template.titleEn,
          bodyAr: template.bodyAr,
          bodyEn: template.bodyEn,
        }
        const dirty =
          draft.titleAr !== template.titleAr ||
          draft.titleEn !== template.titleEn ||
          draft.bodyAr !== template.bodyAr ||
          draft.bodyEn !== template.bodyEn
        const isOpen = expanded === template.type
        const setField = (field: keyof NotificationTemplateDraft, value: string) =>
          setDrafts((prev) => ({ ...prev, [template.type]: { ...draft, [field]: value } }))

        return (
          <Card key={template.type} title={template.type}>
            <div className="flex flex-col gap-3">
              <div className="flex flex-wrap items-center gap-3">
                {template.isOverridden ? (
                  <Badge tone="info">
                    {template.updatedAt
                      ? t('notificationTemplates.overriddenAt', { at: formatDateTime(template.updatedAt, locale) })
                      : t('notificationTemplates.overridden')}
                  </Badge>
                ) : (
                  <Badge tone="neutral">{t('notificationTemplates.shipped')}</Badge>
                )}
                <Button
                  size="sm"
                  variant="ghost"
                  aria-expanded={isOpen}
                  onClick={() => setExpanded(isOpen ? null : template.type)}
                >
                  {isOpen ? t('notificationTemplates.collapse') : t('notificationTemplates.edit')}
                </Button>
              </div>

              {/* Collapsed by default: 29 types with four bilingual fields each is a page nobody can
                  scan, and the question an administrator arrives with is "which of these has been
                  changed", which the badge answers without opening anything. */}
              {isOpen ? (
                <div className="flex flex-col gap-3">
                  <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                    {template.availableTokens.length > 0
                      ? t('notificationTemplates.tokens', {
                          tokens: template.availableTokens.map((token) => `{${token}}`).join(', '),
                        })
                      : t('notificationTemplates.noTokens')}
                  </p>

                  <Field label={t('notificationTemplates.titleAr')} error={errors[template.type]}>
                    {(p) => <Input {...p} value={draft.titleAr} onChange={(e) => setField('titleAr', e.target.value)} />}
                  </Field>
                  <Field label={t('notificationTemplates.titleEn')}>
                    {(p) => <Input {...p} value={draft.titleEn} onChange={(e) => setField('titleEn', e.target.value)} />}
                  </Field>
                  <Field label={t('notificationTemplates.bodyAr')}>
                    {(p) => <Input {...p} value={draft.bodyAr} onChange={(e) => setField('bodyAr', e.target.value)} />}
                  </Field>
                  <Field label={t('notificationTemplates.bodyEn')}>
                    {(p) => <Input {...p} value={draft.bodyEn} onChange={(e) => setField('bodyEn', e.target.value)} />}
                  </Field>

                  {/* What revert would restore, in the locale being edited. */}
                  <div className="rounded-[var(--radius-md)] p-3" style={{ border: '1px solid var(--color-border)' }}>
                    <p className="text-[length:var(--text-body-sm)] font-[var(--fw-medium)]">
                      {t('notificationTemplates.shippedCopy')}
                    </p>
                    <p style={{ color: 'var(--color-text-secondary)' }}>{template.shippedTitleAr}</p>
                    <p style={{ color: 'var(--color-text-secondary)' }}>{template.shippedTitleEn}</p>
                  </div>

                  <div className="flex flex-wrap gap-3">
                    <Button
                      size="sm"
                      disabled={!dirty || saveMutation.isPending}
                      onClick={() => saveMutation.mutate({ type: template.type, draft })}
                    >
                      {t('notificationTemplates.save')}
                    </Button>
                    {template.isOverridden ? (
                      <Button
                        size="sm"
                        variant="ghost"
                        disabled={revertMutation.isPending}
                        onClick={() => revertMutation.mutate(template.type)}
                      >
                        {t('notificationTemplates.revert')}
                      </Button>
                    ) : null}
                  </div>
                </div>
              ) : null}
            </div>
          </Card>
        )
      })}
    </div>
  )
}
