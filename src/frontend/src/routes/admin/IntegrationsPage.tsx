// The systems this product talks to, at /back-office/integrations, for system_admin.
//
// THE POINT OF THE SCREEN IS THAT AN ADDRESS CAN CHANGE WITHOUT A DEPLOYMENT. Another ministry moves its server
// on its own schedule, and waiting for a release to follow them is how an integration stays broken for a week.
//
// THE SECRET FIELD IS ALWAYS EMPTY AND SAYS WHY. Nothing can prefill it, because nothing can read a stored
// credential back - so the field's help text tells the reader that leaving it alone keeps the existing one. Not
// saying so is how somebody clears a credential by editing a URL.
//
// TEST COMES AFTER SAVE, AND THE SCREEN SAYS SO. It tests what the application will actually use rather than what
// is currently typed in the form, because a test of unsaved input proves something about a value that may never
// be stored.
//
// A FAILED TEST IS SHOWN AS AN ANSWER, NOT AS AN ERROR. The detail is the ERP's own words where there are any,
// because "403 PermissionError" tells an administrator to ask the other team, while "could not connect" sends
// them to check their typing.
//
// A FAILED LOAD SAYS SO RATHER THAN SHOWING AN EMPTY PAGE. Without that, a screen whose request failed reads as
// "this product has no integrations", which is the same picture as a working screen on a fresh deployment - and
// the reader has no way to tell them apart. A sweep in this repository catches exactly that omission, and caught
// this one.
//
// A RUN THAT HELD BACK ITS SUSPENSIONS IS NOT SHOWN AS A SUCCESS. The server records it as not succeeded, because an
// empty or half-empty list from the ERP is what a broken read looks like, and a green badge over it would reassure
// the one person who needs to look. "Needs attention" covers that and an outright failure; the summary says which.
//
// THE LAST IMPORT IS SHOWN ON THE CARD, because the nightly run happens when nobody is looking and a failure that is
// never shown looks exactly like a night with nothing to do. This is where the person who would fix it already
// comes, so this is where it goes.
//
// THE SOURCE IS SHOWN ON EVERY ROW. A deployment can still be running from its own settings, and an administrator
// who saves an address and sees nothing change needs to be told that rather than left to guess.

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Badge, Button, Card, Field, Input, PageHeading, QueryError, SkeletonList, useToast,
} from '../../components/ui'
import { formatDateTime } from '../../lib/datetime'
import {
  getIntegrations,
  testIntegration,
  updateIntegration,
  type Integration,
  type IntegrationTestResult,
} from '../../api/integrations'

export function IntegrationsPage() {
  const { t } = useTranslation()
  const query = useQuery({ queryKey: ['integrations'], queryFn: getIntegrations })

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('integrations.title')} subtitle={t('integrations.subtitle')} />

      {query.isPending && <SkeletonList rows={2} label={t('integrations.title')} />}

      {query.isError && <QueryError error={query.error} onRetry={() => void query.refetch()} />}

      {query.data?.map((integration) => (
        <IntegrationCard key={integration.key} integration={integration} />
      ))}
    </div>
  )
}

function IntegrationCard({ integration }: Readonly<{ integration: Integration }>) {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'
  const queryClient = useQueryClient()
  const { notify } = useToast()

  const [baseUrl, setBaseUrl] = useState(integration.baseUrl)
  const [apiKey, setApiKey] = useState(integration.apiKey)
  const [apiSecret, setApiSecret] = useState('')
  const [isEnabled, setIsEnabled] = useState(integration.isEnabled)
  const [tested, setTested] = useState<IntegrationTestResult | null>(null)

  const save = useMutation({
    mutationFn: () =>
      updateIntegration(integration.key, {
        baseUrl: baseUrl.trim(),
        apiKey: apiKey.trim(),
        apiSecret: apiSecret.trim() === '' ? null : apiSecret.trim(),
        isEnabled,
      }),
    onSuccess: () => {
      setApiSecret('')
      setTested(null)
      void queryClient.invalidateQueries({ queryKey: ['integrations'] })
      notify({ kind: 'success', title: t('integrations.saved') })
    },
    onError: () => notify({ kind: 'danger', title: t('integrations.saveFailed') }),
  })

  const test = useMutation({
    mutationFn: () => testIntegration(integration.key),
    onSuccess: (result) => {
      setTested(result)
      void queryClient.invalidateQueries({ queryKey: ['integrations'] })
    },
    onError: () => notify({ kind: 'danger', title: t('integrations.testFailed') }),
  })

  return (
    <Card title={integration.displayName}>
      <div className="flex flex-col gap-4">
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={integration.isEnabled ? 'success' : 'neutral'}>
            {t(integration.isEnabled ? 'integrations.enabled' : 'integrations.disabled')}
          </Badge>
          <Badge tone={integration.source === 'Database' ? 'info' : 'warning'}>
            {t(
              integration.source === 'Database'
                ? 'integrations.sourceHere'
                : 'integrations.sourceDeployment',
            )}
          </Badge>
        </div>

        <Field label={t('integrations.baseUrl')} hint={t('integrations.baseUrlHint')}>
          {(inputProps) => (
            <Input {...inputProps} value={baseUrl} onChange={(event) => setBaseUrl(event.target.value)} />
          )}
        </Field>

        <Field label={t('integrations.apiKey')} hint={t('integrations.apiKeyHint')}>
          {(inputProps) => (
            <Input {...inputProps} value={apiKey} onChange={(event) => setApiKey(event.target.value)} />
          )}
        </Field>

        <Field
          label={t('integrations.apiSecret')}
          hint={
            integration.hasSecret
              ? t('integrations.apiSecretSetHint', {
                  when: formatDateTime(integration.secretSetAt, locale),
                })
              : t('integrations.apiSecretUnsetHint')
          }
        >
          {(inputProps) => (
            <Input
              {...inputProps}
              type="password"
              autoComplete="new-password"
              value={apiSecret}
              onChange={(event) => setApiSecret(event.target.value)}
            />
          )}
        </Field>

        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={isEnabled}
            onChange={(event) => setIsEnabled(event.target.checked)}
          />
          <span>{t('integrations.enabledLabel')}</span>
        </label>

        <div className="flex flex-wrap gap-2">
          <Button onClick={() => save.mutate()} disabled={save.isPending}>
            {save.isPending ? t('integrations.saving') : t('integrations.save')}
          </Button>
          <Button variant="secondary" onClick={() => test.mutate()} disabled={test.isPending}>
            {test.isPending ? t('integrations.testing') : t('integrations.test')}
          </Button>
        </div>

        <p style={{ color: 'var(--color-text-secondary)' }}>{t('integrations.testHint')}</p>

        {(tested ?? lastTestOf(integration)) !== null && (
          <TestOutcome result={(tested ?? lastTestOf(integration))!} />
        )}

        {integration.lastSyncAt !== null && (
          <div className="flex flex-col gap-1">
            <div className="flex flex-wrap items-center gap-2">
              <span style={{ fontWeight: 600 }}>{t('integrations.lastImport')}</span>
              <Badge tone={integration.lastSyncSucceeded ? 'success' : 'warning'}>
                {t(integration.lastSyncSucceeded ? 'integrations.importSucceeded' : 'integrations.importNeedsAttention')}
              </Badge>
              <span style={{ color: 'var(--color-text-secondary)' }}>
                {formatDateTime(integration.lastSyncAt, locale)}
              </span>
            </div>
            {integration.lastSyncSummary !== null && (
              <p style={{ color: 'var(--color-text-secondary)' }}>{integration.lastSyncSummary}</p>
            )}
          </div>
        )}

        {integration.updatedAt !== null && (
          <p style={{ color: 'var(--color-text-secondary)' }}>
            {t('integrations.lastChanged', { when: formatDateTime(integration.updatedAt, locale) })}
          </p>
        )}
      </div>
    </Card>
  )
}

function lastTestOf(integration: Integration): IntegrationTestResult | null {
  if (integration.lastTestSucceeded === null) return null

  return { succeeded: integration.lastTestSucceeded, detail: integration.lastTestDetail ?? '' }
}

function TestOutcome({ result }: Readonly<{ result: IntegrationTestResult }>) {
  const { t } = useTranslation()

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2">
        <Badge tone={result.succeeded ? 'success' : 'danger'}>
          {t(result.succeeded ? 'integrations.reachable' : 'integrations.unreachable')}
        </Badge>
      </div>
      {result.detail !== '' && (
        <p style={{ fontFamily: 'var(--font-mono, monospace)', color: 'var(--color-text-secondary)' }}>
          {result.detail}
        </p>
      )}
    </div>
  )
}
