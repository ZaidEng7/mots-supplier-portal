import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Field, Input, Select, SkeletonList, useToast } from '../../components/ui'
import { formatDateTime } from '../../lib/datetime'
import { getSystemSettings, updateSystemSetting, type SystemSetting } from '../../api/systemSettings'

/**
 * SCR-724, `/back-office/settings`, `system_admin`, P1 (FR-ADM-006).
 *
 * <p>Registration mode was a value nothing read, the default currency was a seed row, and the two
 * document-expiry windows were appsettings keys - so changing any of them was a redeploy.</p>
 *
 * <p>Each row states whether it is <em>overridden</em> or still running on the default. That
 * distinction is the point of the screen: "nobody has decided" and "an administrator chose 30" look
 * identical in a plain value column, and only the second one has an author and a date.</p>
 *
 * <p>The rules come from the server with each setting - bounds, allowed values, kind - so this screen
 * renders the right control without keeping a second copy of the catalogue that could disagree with
 * the one that validates.</p>
 */
export function SystemSettingsPage() {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'
  const queryClient = useQueryClient()
  const { notify } = useToast()

  const query = useQuery({ queryKey: ['system-settings'], queryFn: getSystemSettings })

  // Per-key drafts, so editing one setting does not discard what was typed into another.
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<Record<string, string>>({})

  const mutation = useMutation({
    mutationFn: ({ key, value }: { key: string; value: string }) => updateSystemSetting(key, value),
    onSuccess: (updated) => {
      queryClient.setQueryData<SystemSetting[]>(['system-settings'], (prev) =>
        prev?.map((s) => (s.key === updated.key ? updated : s)),
      )
      setDrafts((prev) => {
        const next = { ...prev }
        delete next[updated.key]
        return next
      })
      setErrors((prev) => {
        const next = { ...prev }
        delete next[updated.key]
        return next
      })
      notify({ kind: 'success', title: t('systemSettings.saved') })
    },
    onError: (error: Error & { reason?: string }, variables) => {
      // The server names the rule that was broken; showing "invalid" instead would leave an
      // administrator guessing which of the bounds they crossed.
      setErrors((prev) => ({
        ...prev,
        [variables.key]: t(`systemSettings.errors.${error.reason ?? 'unknown'}`, {
          defaultValue: t('systemSettings.errors.unknown'),
        }),
      }))
    },
  })

  if (query.isLoading) return <SkeletonList label={t('common.loading')} />

  if (query.isError || !query.data) {
    return (
      <Card title={t('systemSettings.title')}>
        <p>{t('systemSettings.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => void query.refetch()}>{t('systemSettings.retry')}</Button>
      </Card>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('systemSettings.title')}
        </h1>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('systemSettings.subtitle')}</p>
      </div>

      {query.data.map((setting) => {
        const draft = drafts[setting.key] ?? setting.value
        const dirty = draft !== setting.value
        const error = errors[setting.key]

        return (
          <Card key={setting.key} title={t(`systemSettings.keys.${setting.key}`, { defaultValue: setting.key })}>
            <div className="flex flex-col gap-3">
              <p style={{ color: 'var(--color-text-secondary)' }}>
                {t(`systemSettings.help.${setting.key}`, { defaultValue: '' })}
              </p>

              {setting.kind === 'Choice' && setting.allowedValues ? (
                <Field label={t('systemSettings.value')} error={error}>
                  {(inputProps) => (
                    <Select
                      {...inputProps}
                      value={draft}
                      onValueChange={(value) => setDrafts((prev) => ({ ...prev, [setting.key]: value }))}
                      options={setting.allowedValues!.map((value) => ({
                        value,
                        label: t(`systemSettings.choices.${setting.key}.${value}`, { defaultValue: value }),
                      }))}
                    />
                  )}
                </Field>
              ) : (
                <Field
                  label={t('systemSettings.value')}
                  error={error}
                  hint={
                    setting.kind === 'IntegerList'
                      ? t('systemSettings.hints.integerList')
                      : setting.minimum !== null && setting.maximum !== null
                        ? t('systemSettings.hints.range', { min: setting.minimum, max: setting.maximum })
                        : undefined
                  }
                >
                  {(inputProps) => (
                    <Input
                      {...inputProps}
                      value={draft}
                      onChange={(event) => setDrafts((prev) => ({ ...prev, [setting.key]: event.target.value }))}
                    />
                  )}
                </Field>
              )}

              <div className="flex flex-wrap items-center gap-3">
                {/* The distinction the screen exists for. */}
                {setting.isOverridden ? (
                  <Badge tone="info">
                    {setting.updatedAt
                      ? t('systemSettings.overriddenAt', { at: formatDateTime(setting.updatedAt, locale) })
                      : t('systemSettings.overridden')}
                  </Badge>
                ) : (
                  <Badge tone="neutral">{t('systemSettings.usingDefault', { value: setting.defaultValue })}</Badge>
                )}

                <Button
                  size="sm"
                  disabled={!dirty || mutation.isPending}
                  onClick={() => mutation.mutate({ key: setting.key, value: draft })}
                >
                  {t('systemSettings.save')}
                </Button>
              </div>
            </div>
          </Card>
        )
      })}
    </div>
  )
}
