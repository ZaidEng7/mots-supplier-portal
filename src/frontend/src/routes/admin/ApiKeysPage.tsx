// The API keys other systems sign in with, at /back-office/api-keys, for system_admin.
//
// THE SECRET IS SHOWN ONCE AND THE SCREEN SAYS SO. The server stores a hash and has no route that reads a key
// back, so the value in the panel after creating one is the only copy that will exist. A screen that displayed
// it quietly, the way it displays everything else, would leave an administrator assuming they could come back
// for it - and the moment they navigate away the credential is unrecoverable. So it gets its own panel, a copy
// button, and a sentence saying plainly that it will not be shown again.
//
// THERE IS NO EDIT. A key's reach and expiry are fixed at issue, and rotation is: create the second key, let
// both work while the caller switches over, revoke the first. That is why creating one is a small form at the
// top rather than a dialog behind a menu - the flow only works if issuing a key is trivial.
//
// REVOKED KEYS STAY IN THE LIST, greyed by their badge rather than removed. The audit trail names keys by
// prefix, and a list that hid revoked ones would leave a year-old audit row pointing at nothing.
//
// LAST USED IS A COLUMN because it is the question that decides whether a key can be revoked safely. "Never"
// is a real and useful answer: a key issued weeks ago that has never been used is either not yet configured or
// forgotten, and both are worth seeing.
//
// EXPIRY IS SHOWN AS A DATE, NOT A COUNTDOWN. The default is a year, and the failure this protects against is a
// nightly load stopping at 3am on a date nobody wrote down. A date can be put in a calendar.

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Field, Input, PageHeading, SkeletonList, useToast } from '../../components/ui'
import { formatDateTime } from '../../lib/datetime'
import { createApiKey, getApiKeys, revokeApiKey, type ApiKeySummary, type CreatedApiKey } from '../../api/apiKeys'

function stateOf(key: ApiKeySummary): 'revoked' | 'expired' | 'active' {
  if (key.revokedAt !== null) return 'revoked'
  if (new Date(key.expiresAt).getTime() <= Date.now()) return 'expired'
  return 'active'
}

export function ApiKeysPage() {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'
  const queryClient = useQueryClient()
  const { notify } = useToast()

  const query = useQuery({ queryKey: ['api-keys'], queryFn: getApiKeys })

  const [name, setName] = useState('')
  const [issued, setIssued] = useState<CreatedApiKey | null>(null)

  const create = useMutation({
    mutationFn: () => createApiKey(name.trim()),
    onSuccess: (result) => {
      setIssued(result)
      setName('')
      void queryClient.invalidateQueries({ queryKey: ['api-keys'] })
    },
    onError: () => notify({ kind: 'danger', title: t('apiKeys.createFailed') }),
  })

  const revoke = useMutation({
    mutationFn: (id: string) => revokeApiKey(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['api-keys'] })
      notify({ kind: 'success', title: t('apiKeys.revoked') })
    },
    onError: () => notify({ kind: 'danger', title: t('apiKeys.revokeFailed') }),
  })

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('apiKeys.title')} subtitle={t('apiKeys.subtitle')} />

      <Card title={t('apiKeys.createTitle')}>
        <div className="flex flex-col gap-3">
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('apiKeys.createHelp')}</p>

          <Field label={t('apiKeys.name')} hint={t('apiKeys.nameHint')}>
            {(inputProps) => (
              <Input
                {...inputProps}
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
            )}
          </Field>

          <div>
            <Button
              size="sm"
              disabled={name.trim().length === 0 || create.isPending}
              onClick={() => create.mutate()}
            >
              {t('apiKeys.create')}
            </Button>
          </div>
        </div>
      </Card>

      {issued && (
        <Card title={t('apiKeys.issuedTitle')}>
          <div className="flex flex-col gap-3">
            <p style={{ color: 'var(--color-text-secondary)' }}>{t('apiKeys.issuedWarning')}</p>

            <code
              dir="ltr"
              className="break-all p-3"
              style={{
                background: 'var(--color-bg-inset)',
                borderRadius: 'var(--radius-md)',
                border: '1px solid var(--color-border)',
              }}
            >
              {issued.secret}
            </code>

            <div className="flex flex-wrap gap-3">
              <Button
                size="sm"
                variant="ghost"
                onClick={() => {
                  void navigator.clipboard?.writeText(issued.secret)
                  notify({ kind: 'success', title: t('apiKeys.copied') })
                }}
              >
                {t('apiKeys.copy')}
              </Button>
              <Button size="sm" variant="ghost" onClick={() => setIssued(null)}>
                {t('apiKeys.dismiss')}
              </Button>
            </div>
          </div>
        </Card>
      )}

      {query.isLoading && <SkeletonList label={t('common.loading')} />}

      {query.isError && (
        <Card title={t('apiKeys.title')}>
          <p>{t('apiKeys.loadFailed')}</p>
          <Button size="sm" variant="ghost" onClick={() => void query.refetch()}>{t('apiKeys.retry')}</Button>
        </Card>
      )}

      {query.data?.length === 0 && (
        <Card title={t('apiKeys.title')}>
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('apiKeys.none')}</p>
        </Card>
      )}

      {query.data?.map((key) => {
        const state = stateOf(key)

        return (
          <Card key={key.id} title={key.name}>
            <div className="flex flex-col gap-3">
              <div className="flex flex-wrap items-center gap-3">
                <code dir="ltr">{key.prefix}</code>
                <Badge tone={state === 'active' ? 'success' : 'neutral'}>{t(`apiKeys.states.${state}`)}</Badge>
              </div>

              <dl className="grid gap-x-6 gap-y-1" style={{ gridTemplateColumns: 'max-content 1fr' }}>
                <dt style={{ color: 'var(--color-text-secondary)' }}>{t('apiKeys.created')}</dt>
                <dd>{formatDateTime(key.createdAt, locale)}</dd>

                <dt style={{ color: 'var(--color-text-secondary)' }}>{t('apiKeys.expires')}</dt>
                <dd>{formatDateTime(key.expiresAt, locale)}</dd>

                <dt style={{ color: 'var(--color-text-secondary)' }}>{t('apiKeys.lastUsed')}</dt>
                <dd>{key.lastUsedAt ? formatDateTime(key.lastUsedAt, locale) : t('apiKeys.never')}</dd>

                <dt style={{ color: 'var(--color-text-secondary)' }}>{t('apiKeys.permissions')}</dt>
                <dd dir="ltr">{key.permissions.join(', ')}</dd>
              </dl>

              {state !== 'revoked' && (
                <div>
                  <Button
                    size="sm"
                    variant="ghost"
                    disabled={revoke.isPending}
                    onClick={() => revoke.mutate(key.id)}
                  >
                    {t('apiKeys.revoke')}
                  </Button>
                </div>
              )}
            </div>
          </Card>
        )
      })}
    </div>
  )
}
