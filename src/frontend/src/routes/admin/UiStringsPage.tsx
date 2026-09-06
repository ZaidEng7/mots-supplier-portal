import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import i18n from '../../i18n/config'
import {
  Badge, Button, Card, Field, Input, Select, SkeletonTable,
  Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast,
} from '../../components/ui'
import { listUiStringOverrides, upsertUiStringOverride, deleteUiStringOverride } from '../../api/uiStrings'

/**
 * Every key the running bundle actually has, flattened to the dotted paths the API stores.
 *
 * <p><b>Read from the bundle rather than from a list on the server.</b> The server does not hold the
 * SPA's key set and never can — it lives in the compiled bundle — so any list it kept would be a second,
 * always-stale copy. The screen that IS the bundle is the only honest source, which is why key selection
 * happens here and the API accepts any key.</p>
 */
function flattenKeys(node: unknown, prefix = ''): string[] {
  if (typeof node === 'string') return [prefix]
  if (typeof node !== 'object' || node === null) return []
  return Object.entries(node as Record<string, unknown>)
    .flatMap(([segment, child]) => flattenKeys(child, prefix ? `${prefix}.${segment}` : segment))
}

/**
 * SCR-716, `/back-office/ui-strings`, `system_admin`, P2.
 *
 * <p>Correcting one word used to mean a code change and a deployment, because every string is compiled
 * into i18n/config.ts. ARABIC-REVIEW.md is a long list of corrections waiting on exactly that, and the
 * people who own the wording are not the people who own releases.</p>
 *
 * <p>The shipped string is shown beside the override so an administrator can see what they are replacing —
 * without it, the screen is a text box next to a key nobody can read.</p>
 */
export function UiStringsPage() {
  const { t } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()

  const [language, setLanguage] = useState('ar')
  const [search, setSearch] = useState('')
  const [selectedKey, setSelectedKey] = useState('')
  const [draft, setDraft] = useState('')

  const overridesQuery = useQuery({ queryKey: ['ui-strings'], queryFn: listUiStringOverrides })

  const allKeys = useMemo(() => {
    const bundle = i18n.getResourceBundle(language, 'translation') as unknown
    return flattenKeys(bundle).sort()
  }, [language])

  // Capped at fifty, because the bundle has well over a thousand keys and a datalist of all of them is a
  // browser hang rather than a search.
  const matches = useMemo(() => {
    if (search.trim().length < 2) return []
    const needle = search.trim().toLowerCase()
    return allKeys.filter((key) => key.toLowerCase().includes(needle)).slice(0, 50)
  }, [allKeys, search])

  const shippedValue = selectedKey
    ? (i18n.getFixedT(language, 'translation')(selectedKey) as string)
    : ''

  const saveMutation = useMutation({
    mutationFn: () => upsertUiStringOverride(language, selectedKey, draft),
    onSuccess: async () => {
      notify({ kind: 'success', title: t('uiStrings.saved') })
      setDraft('')
      setSelectedKey('')
      await queryClient.invalidateQueries({ queryKey: ['ui-strings'] })
    },
    onError: () => notify({ kind: 'danger', title: t('uiStrings.errors.saveFailed') }),
  })

  const deleteMutation = useMutation({
    mutationFn: (row: { language: string; key: string }) => deleteUiStringOverride(row.language, row.key),
    onSuccess: async () => {
      notify({ kind: 'success', title: t('uiStrings.restored') })
      await queryClient.invalidateQueries({ queryKey: ['ui-strings'] })
    },
    onError: () => notify({ kind: 'danger', title: t('uiStrings.errors.deleteFailed') }),
  })

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('uiStrings.title')}
        </h1>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('uiStrings.subtitle')}</p>
      </div>

      <Card title={t('uiStrings.editTitle')}>
        <div className="flex flex-col gap-4">
          <div className="max-w-[14rem]">
            <Field label={t('uiStrings.fields.language')}>
              {/* The Field's props are threaded through and a placeholder is given, and both matter: Select
                  names its Radix trigger from `placeholder`, so without one the trigger had no accessible name
                  at all - axe reported button-name at CRITICAL impact and the e2e a11y sweep failed on this
                  page. Discarding `p` also meant the visible label's htmlFor pointed at nothing. */}
              {(p) => (
                <Select
                  {...p}
                  value={language}
                  onValueChange={(next) => { setLanguage(next); setSelectedKey(''); setDraft('') }}
                  placeholder={t('uiStrings.fields.language')}
                  options={[{ value: 'ar', label: t('account.languages.ar') }, { value: 'en', label: t('account.languages.en') }]}
                />
              )}
            </Field>
          </div>

          <Field label={t('uiStrings.fields.search')} hint={t('uiStrings.searchHint')}>
            {(p) => <Input {...p} value={search} onChange={(e) => setSearch(e.target.value)} />}
          </Field>

          {matches.length > 0 ? (
            <ul className="flex flex-col gap-1">
              {matches.map((key) => (
                <li key={key}>
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setSelectedKey(key)
                      // Pre-filled with the string being replaced, not blank: an administrator fixing one
                      // word should not have to retype the sentence around it.
                      setDraft(i18n.getFixedT(language, 'translation')(key) as string)
                    }}
                  >
                    <span className="font-mono text-[length:var(--text-body-sm)]">{key}</span>
                  </Button>
                </li>
              ))}
            </ul>
          ) : null}

          {selectedKey ? (
            <>
              <div>
                <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                  {t('uiStrings.shipped')}
                </p>
                <p style={{ color: 'var(--color-text-primary)' }}>{shippedValue}</p>
              </div>
              <Field label={t('uiStrings.fields.value')} required>
                {(p) => <Input {...p} value={draft} onChange={(e) => setDraft(e.target.value)} />}
              </Field>
              <div className="flex gap-2">
                <Button isLoading={saveMutation.isPending} disabled={!draft.trim() || draft === shippedValue}
                  onClick={() => saveMutation.mutate()}>
                  {t('uiStrings.save')}
                </Button>
                <Button variant="ghost" onClick={() => { setSelectedKey(''); setDraft('') }}>
                  {t('uiStrings.cancel')}
                </Button>
              </div>
            </>
          ) : null}
        </div>

        <p className="mt-4 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('uiStrings.takesEffect')}
        </p>
      </Card>

      <Card title={t('uiStrings.listTitle')}>
        {overridesQuery.isLoading ? <SkeletonTable label={t('common.loading')} /> : null}
        {overridesQuery.isError ? (
          <div className="flex flex-col gap-2">
            <p>{t('uiStrings.errors.loadFailed')}</p>
            <Button variant="ghost" onClick={() => void overridesQuery.refetch()}>{t('uiStrings.retry')}</Button>
          </div>
        ) : null}

        {overridesQuery.data && overridesQuery.data.length === 0 ? (
          /* Not a defect: no overrides means the product reads exactly as it was built, which is the
             correct state for a fresh deployment. */
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('uiStrings.empty')}</p>
        ) : null}

        {overridesQuery.data && overridesQuery.data.length > 0 ? (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('uiStrings.fields.key')}</TableHeaderCell>
                <TableHeaderCell>{t('uiStrings.fields.language')}</TableHeaderCell>
                <TableHeaderCell>{t('uiStrings.fields.value')}</TableHeaderCell>
                <TableHeaderCell>{t('uiStrings.fields.actions')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {overridesQuery.data.map((row) => (
                <TableRow key={`${row.language}:${row.key}`}>
                  <TableCell><span className="font-mono text-[length:var(--text-body-sm)]">{row.key}</span></TableCell>
                  <TableCell><Badge tone="neutral">{row.language}</Badge></TableCell>
                  <TableCell>{row.value}</TableCell>
                  <TableCell>
                    {/* "Restore", not "delete": what the click does is bring back the shipped string, and
                        naming it after the storage would make an administrator hesitate over the one
                        action that is always safe. */}
                    <Button variant="ghost" disabled={deleteMutation.isPending}
                      onClick={() => deleteMutation.mutate({ language: row.language, key: row.key })}>
                      {t('uiStrings.restore')}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : null}
      </Card>
    </div>
  )
}
