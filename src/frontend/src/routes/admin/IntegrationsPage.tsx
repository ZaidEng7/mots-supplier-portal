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
// THE BADGE SAYS WHETHER THE IMPORT CAN RUN, not whether the ERP answered. The test reads what the import reads, so
// an ERP that answers and refuses addresses fails it - and a "Could not reach it" badge above "Reached it, but it
// refused to read addresses" contradicted its own detail and pointed at the network.
//
// A FAILED LOAD SAYS SO RATHER THAN SHOWING AN EMPTY PAGE. Without that, a screen whose request failed reads as
// "this product has no integrations", which is the same picture as a working screen on a fresh deployment - and
// the reader has no way to tell them apart. A sweep in this repository catches exactly that omission, and caught
// this one.
//
// THREE OUTCOMES, EACH ITS OWN COLOUR. A run that finished but left something for a person - suspensions held back
// over a list that looked broken, a possible rename, a supplier that failed - is amber. A run that could not finish
// is red. A green badge over the first would reassure the one person who needs to look, and one shared amber made an
// outright failure look no more urgent than a rename waiting to be checked.
//
// THE LAST IMPORT IS SHOWN ON THE CARD, because the hourly run happens when nobody is looking and a failure that is
// never shown looks exactly like a run with nothing to do. This is where the person who would fix it already
// comes, so this is where it goes.
//
// THE SOURCE IS SHOWN ON EVERY ROW. A deployment can still be running from its own settings, and an administrator
// who saves an address and sees nothing change needs to be told that rather than left to guess.
//
// CREATING APPROVED SUPPLIERS IN THE ERP IS ON THE ERP'S CARD, as a group and a switch, and only there. The group comes
// first and the switch cannot be turned on without one, because the ERP refuses a supplier without a group and every
// create would fail on it. The groups are read from the ERP itself, so a name typed by hand that the ERP does not have
// cannot be chosen; the group already saved stays in the list even when the ERP cannot answer, so a failed read never
// blanks the choice. A failed read shows the server's own words and a retry, as the page does for its own load. The
// groups sit under the page's own query key, so every save and test reads them again: a save can change the address
// and credential they are read through.
//
// TURNING THE SWITCH ON ASKS FIRST AND SAVES AT ONCE. The question says how many approved suppliers are waiting,
// because turning it on sends every one of them to the ERP within minutes and nothing here can take one back, and a
// person agreeing to that should know whether it is two suppliers or two hundred. The count is read again as the
// question opens, since the list may have been loaded an hour earlier. Agreeing saves the card, so there is no state
// in which somebody agreed and nothing was sent. Turning it off, and changing the group, are ordinary edits saved with
// Save, like the connection's own switch: the question guards the one direction that writes to somebody else's system.
//
// SAVE SENDS THE SWITCH AND THE GROUP ONLY WHEN THE PERSON CHANGED THEM, and never sends the switch on. The server
// reads a switch or a group that is sent as a request to set it, and one that is left out as "leave it as it is", and
// the save carries no version. A card left open for an hour, while another administrator turned the writes off for a
// pause the ERP team asked for, would otherwise put its old On back with an unrelated change such as a new secret -
// without the question, under the name of the person who only changed the secret. So Save sends the switch when it
// turns off a switch the server has on, and the group when it differs from the server's; the question stays the only
// way to turn the writes on.
//
// THE CARD FOLLOWS THE SERVER'S SWITCH AND GROUP. Whenever the list is read again and either has moved, the card shows
// the new value, adjusted during render as SearchPage does rather than in an effect, and an unsaved change to it is
// dropped, because it was a change to a value that no longer holds. A save puts the server's answer into the list at
// once, so what Save compares against is the latest the server said rather than what the page loaded with.
//
// A REFUSED SAVE SHOWS THE SERVER'S SENTENCE, such as the switch turned on without a group through some other route,
// because a bare "could not save" tells nobody which rule they broke.

import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Badge, Button, Card, Dialog, Field, Input, PageHeading, QueryError, Select, SkeletonList, useToast,
} from '../../components/ui'
import { formatDateTime } from '../../lib/datetime'
import { errorDetail } from '../../api/problem'
import {
  getErpSupplierGroups,
  getIntegrations,
  testIntegration,
  updateIntegration,
  type Integration,
  type IntegrationTestResult,
  type IntegrationUpdate,
} from '../../api/integrations'

const ERP_KEY = 'erp'

type SupplierCreationChange = Pick<IntegrationUpdate, 'createSuppliersInErp' | 'defaultSupplierGroup'>

const OUTCOME_TONE = { Succeeded: 'success', NeedsAttention: 'warning', Failed: 'danger' } as const

const OUTCOME_LABEL = {
  Succeeded: 'integrations.importSucceeded',
  NeedsAttention: 'integrations.importNeedsAttention',
  Failed: 'integrations.importFailed',
} as const

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
  const isErp = integration.key === ERP_KEY
  const serverGroup = integration.defaultSupplierGroup ?? ''
  const [createSuppliersInErp, setCreateSuppliersInErp] = useState(integration.createSuppliersInErp)
  const [supplierGroup, setSupplierGroup] = useState(serverGroup)
  const [confirmingSupplierCreation, setConfirmingSupplierCreation] = useState(false)

  const [lastRead, setLastRead] = useState({ createSuppliersInErp: integration.createSuppliersInErp, group: serverGroup })
  if (lastRead.createSuppliersInErp !== integration.createSuppliersInErp || lastRead.group !== serverGroup) {
    setLastRead({ createSuppliersInErp: integration.createSuppliersInErp, group: serverGroup })
    setCreateSuppliersInErp(integration.createSuppliersInErp)
    setSupplierGroup(serverGroup)
  }

  const save = useMutation({
    mutationFn: (supplierCreation: SupplierCreationChange) =>
      updateIntegration(integration.key, {
        baseUrl: baseUrl.trim(),
        apiKey: apiKey.trim(),
        apiSecret: apiSecret.trim() === '' ? null : apiSecret.trim(),
        isEnabled,
        ...supplierCreation,
      }),
    onSuccess: (saved) => {
      setApiSecret('')
      setTested(null)
      setConfirmingSupplierCreation(false)
      queryClient.setQueryData<Integration[]>(['integrations'], (list) =>
        list?.map((item) => (item.key === saved.key ? saved : item)),
      )
      void queryClient.invalidateQueries({ queryKey: ['integrations'] })
      notify({ kind: 'success', title: t('integrations.saved') })
    },
    onError: (error) => {
      setConfirmingSupplierCreation(false)
      notify({ kind: 'danger', title: t('integrations.saveFailed'), description: errorDetail(error) ?? undefined })
    },
  })

  const supplierCreationEdits = (): SupplierCreationChange => {
    if (!isErp) return {}

    return {
      ...(integration.createSuppliersInErp && !createSuppliersInErp ? { createSuppliersInErp: false } : {}),
      ...(supplierGroup === serverGroup ? {} : { defaultSupplierGroup: supplierGroup }),
    }
  }

  const askToTurnOnSupplierCreation = () => {
    void queryClient.invalidateQueries({ queryKey: ['integrations'], exact: true })
    setConfirmingSupplierCreation(true)
  }

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

        {isErp && (
          <SupplierCreation
            integrationKey={integration.key}
            createSuppliersInErp={createSuppliersInErp}
            supplierGroup={supplierGroup}
            onGroupChange={setSupplierGroup}
            onTurnOn={askToTurnOnSupplierCreation}
            onTurnOff={() => setCreateSuppliersInErp(false)}
          />
        )}

        <div className="flex flex-wrap gap-2">
          <Button onClick={() => save.mutate(supplierCreationEdits())} disabled={save.isPending}>
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
              {integration.lastSyncOutcome !== null && (
                <Badge tone={OUTCOME_TONE[integration.lastSyncOutcome]}>
                  {t(OUTCOME_LABEL[integration.lastSyncOutcome])}
                </Badge>
              )}
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

      <Dialog
        open={confirmingSupplierCreation}
        onOpenChange={setConfirmingSupplierCreation}
        title={t('integrations.confirmSupplierCreationTitle')}
        description={
          integration.suppliersWaitingForErp === 0
            ? t('integrations.confirmSupplierCreationNoneWaiting', { group: supplierGroup })
            : t('integrations.confirmSupplierCreationWaiting', {
                count: integration.suppliersWaitingForErp,
                group: supplierGroup,
              })
        }
      >
        <div className="flex flex-col gap-4">
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('integrations.confirmSupplierCreationWarning')}</p>
          <div className="flex flex-wrap gap-2">
            <Button
              onClick={() => save.mutate({ createSuppliersInErp: true, defaultSupplierGroup: supplierGroup })}
              disabled={save.isPending}
            >
              {t('integrations.confirmSupplierCreation')}
            </Button>
            <Button variant="secondary" onClick={() => setConfirmingSupplierCreation(false)}>
              {t('integrations.cancel')}
            </Button>
          </div>
        </div>
      </Dialog>
    </Card>
  )
}

// The ERP's supplier group and the switch that creates approved suppliers there. The state is the card's, because one
// Save carries it with the address; this draws it and reads the groups.
function SupplierCreation({
  integrationKey,
  createSuppliersInErp,
  supplierGroup,
  onGroupChange,
  onTurnOn,
  onTurnOff,
}: Readonly<{
  integrationKey: string
  createSuppliersInErp: boolean
  supplierGroup: string
  onGroupChange: (group: string) => void
  onTurnOn: () => void
  onTurnOff: () => void
}>) {
  const { t } = useTranslation()
  const hintId = useId()
  const groups = useQuery({
    queryKey: ['integrations', integrationKey, 'supplier-groups'],
    queryFn: () => getErpSupplierGroups(integrationKey),
  })

  const names = [...new Set([...(groups.data ?? []), ...(supplierGroup === '' ? [] : [supplierGroup])])]
  const needsGroup = !createSuppliersInErp && supplierGroup === ''

  return (
    <div className="flex flex-col gap-3">
      <h3 className="text-[length:var(--text-body)] font-[var(--fw-medium)]" style={{ color: 'var(--color-text-primary)' }}>
        {t('integrations.supplierCreationTitle')}
      </h3>

      <Field
        label={t('integrations.supplierGroup')}
        hint={groups.isPending ? t('integrations.supplierGroupsLoading') : t('integrations.supplierGroupHint')}
      >
        {(inputProps) => (
          <Select
            id={inputProps.id}
            aria-describedby={inputProps['aria-describedby']}
            value={supplierGroup}
            onValueChange={onGroupChange}
            options={names.map((name) => ({ value: name, label: name }))}
            disabled={groups.isPending}
          />
        )}
      </Field>

      {groups.isError && (
        <QueryError
          error={groups.error}
          errorText={t('integrations.supplierGroupsFailed')}
          onRetry={() => void groups.refetch()}
        />
      )}

      <label className="flex items-center gap-2">
        <input
          type="checkbox"
          role="switch"
          aria-describedby={hintId}
          checked={createSuppliersInErp}
          disabled={needsGroup}
          onChange={(event) => (event.target.checked ? onTurnOn() : onTurnOff())}
        />
        <span>{t('integrations.supplierCreation')}</span>
      </label>
      <p id={hintId} style={{ color: 'var(--color-text-secondary)' }}>
        {t(needsGroup ? 'integrations.supplierCreationNeedsGroup' : 'integrations.supplierCreationHint')}
      </p>
    </div>
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
          {t(result.succeeded ? 'integrations.ready' : 'integrations.notReady')}
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
