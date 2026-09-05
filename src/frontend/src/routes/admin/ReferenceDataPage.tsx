import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Badge, Button, Card, Field, Input, Select, SkeletonTable,
  Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast,
} from '../../components/ui'
import { SupplierApiError } from '../../api/supplier'
import {
  REFERENCE_TABLES, listReferenceItems, createReferenceItem, updateReferenceItem, setReferenceItemActive,
  type ReferenceItem, type ReferenceTable,
} from '../../api/referenceAdmin'

/**
 * SCR-710 / SCR-711 / SCR-712, `/back-office/reference`, `system_admin`, P1 (FR-ADM-004).
 *
 * <p>T-034/T-059 landed the whole admin write surface in batch 9 and no screen consumed it, so adding a
 * document type still meant a request by hand. Three inventory rows, one screen: the operations are
 * identical across the five tables and only DocumentType carries extra flags — five near-identical pages
 * would be five places for the next change to miss, which is the same argument the single handler behind
 * them already makes.</p>
 *
 * <p><b>No delete, and the code is not editable.</b> Both are D-28: every one of these tables is
 * referenced BY CODE from live rows with no cascade, so deleting a Category a published RFQ points at
 * would leave that RFQ describing nothing, and renaming a code would silently change what a historical
 * award was for. Deactivation hides a code from new selections and leaves every existing row readable.</p>
 */
export function ReferenceDataPage() {
  const { t } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()

  const [table, setTable] = useState<ReferenceTable>('categories')
  const [newCode, setNewCode] = useState('')
  const [newNameAr, setNewNameAr] = useState('')
  const [newNameEn, setNewNameEn] = useState('')
  const [error, setError] = useState<string | null>(null)

  const itemsQuery = useQuery({
    queryKey: ['reference-admin', table],
    queryFn: () => listReferenceItems(table),
  })

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['reference-admin', table] })

  // The server names the rule that was broken - a duplicate code, or one longer than the column allows
  // (Currency.Code is 3 by ISO, the others 50, and a too-long code used to answer 500 from Postgres).
  // Showing "invalid" instead would leave an administrator guessing which.
  //
  // Where it is shown depends on whether the failure has a field to point at: a rejected NEW code
  // belongs beside the code input, and a rejected rename or deactivation has no input of its own,
  // so it goes to the toast. Doing both put the same sentence on screen twice.
  const onError = (raised: unknown, fallback: string, surface: 'field' | 'toast') => {
    const code = raised instanceof SupplierApiError ? (raised.code ?? '') : ''
    const message =
      code === 'DUPLICATE_RESOURCE' ? t('referenceAdmin.errors.duplicateCode')
        : code === 'INVALID_REFERENCE_ITEM'
          ? (raised instanceof SupplierApiError ? raised.message : fallback)
          : fallback
    if (surface === 'field') setError(message)
    else notify({ kind: 'danger', title: message })
  }

  const createMutation = useMutation({
    mutationFn: () => createReferenceItem(table, newCode.trim(), { nameAr: newNameAr, nameEn: newNameEn }),
    onSuccess: () => {
      refresh()
      setNewCode(''); setNewNameAr(''); setNewNameEn(''); setError(null)
      notify({ kind: 'success', title: t('referenceAdmin.created') })
    },
    onError: (raised) => onError(raised, t('referenceAdmin.errors.createFailed'), 'field'),
  })

  const activeMutation = useMutation({
    mutationFn: ({ code, isActive }: { code: string; isActive: boolean }) => setReferenceItemActive(table, code, isActive),
    onSuccess: refresh,
    onError: (raised) => onError(raised, t('referenceAdmin.errors.updateFailed'), 'toast'),
  })

  const renameMutation = useMutation({
    mutationFn: ({ item, nameAr, nameEn }: { item: ReferenceItem; nameAr: string; nameEn: string }) =>
      updateReferenceItem(table, item.code, {
        nameAr, nameEn,
        // Omitted means unchanged, not false: an administrator fixing an Arabic typo must not silently
        // clear a document type's requiredness.
        isRequired: item.isRequired, expiryTracked: item.expiryTracked,
      }),
    onSuccess: () => { refresh(); notify({ kind: 'success', title: t('referenceAdmin.renamed') }) },
    onError: (raised) => onError(raised, t('referenceAdmin.errors.updateFailed'), 'toast'),
  })

  const [drafts, setDrafts] = useState<Record<string, { ar: string; en: string }>>({})
  const draftFor = (item: ReferenceItem) => drafts[item.code] ?? { ar: item.nameAr, en: item.nameEn }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('referenceAdmin.title')}
        </h1>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('referenceAdmin.subtitle')}</p>
      </div>

      <div className="max-w-xs">
        <Field label={t('referenceAdmin.tableLabel')}>
          {(p) => (
            <Select
              {...p}
              value={table}
              onValueChange={(value: string) => { setTable(value as ReferenceTable); setDrafts({}); setError(null) }}
              options={REFERENCE_TABLES.map((name) => ({ value: name, label: t(`adminOverview.tables.${name}`) }))}
            />
          )}
        </Field>
      </div>

      <Card title={t('referenceAdmin.addTitle')}>
        <div className="flex flex-wrap items-end gap-2">
          <Field label={t('referenceAdmin.code')} error={error ?? undefined}>
            {(p) => <Input {...p} value={newCode} onChange={(e) => setNewCode(e.target.value)} />}
          </Field>
          <Field label={t('referenceAdmin.nameEn')}>
            {(p) => <Input {...p} value={newNameEn} onChange={(e) => setNewNameEn(e.target.value)} />}
          </Field>
          <Field label={t('referenceAdmin.nameAr')}>
            {(p) => <Input {...p} value={newNameAr} onChange={(e) => setNewNameAr(e.target.value)} />}
          </Field>
          <Button
            size="sm"
            disabled={!newCode.trim() || !newNameAr || !newNameEn || createMutation.isPending}
            onClick={() => createMutation.mutate()}
          >
            {t('referenceAdmin.add')}
          </Button>
        </div>
        {/* Stated on the screen, because an administrator who cannot find a delete button deserves to know
            it is absent on purpose rather than missing. */}
        <p className="mt-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('referenceAdmin.noDeleteNotice')}
        </p>
      </Card>

      {itemsQuery.isLoading ? (
        <SkeletonTable label={t('common.loading')} />
      ) : itemsQuery.isError ? (
        <Card title={t('referenceAdmin.title')}>
          <p>{t('referenceAdmin.errors.loadFailed')}</p>
          <Button size="sm" variant="ghost" onClick={() => void itemsQuery.refetch()}>{t('referenceAdmin.retry')}</Button>
        </Card>
      ) : (
        <Card title={t(`adminOverview.tables.${table}`)}>
          {(itemsQuery.data ?? []).length === 0 ? (
            <p style={{ color: 'var(--color-text-secondary)' }}>{t('referenceAdmin.empty')}</p>
          ) : (
            <Table caption={t(`adminOverview.tables.${table}`)}>
              <TableHead>
                <TableHeaderCell>{t('referenceAdmin.code')}</TableHeaderCell>
                <TableHeaderCell>{t('referenceAdmin.name')}</TableHeaderCell>
                <TableHeaderCell>{t('referenceAdmin.status')}</TableHeaderCell>
                <TableHeaderCell>{t('referenceAdmin.actions')}</TableHeaderCell>
              </TableHead>
              <TableBody>
                {(itemsQuery.data ?? []).map((item) => {
                  const draft = draftFor(item)
                  const dirty = draft.ar !== item.nameAr || draft.en !== item.nameEn
                  return (
                    <TableRow key={item.code}>
                      {/* The code is text, not an input: it is the foreign key in every live row. */}
                      <TableCell><code>{item.code}</code></TableCell>
                      <TableCell>
                        <div className="flex flex-wrap items-end gap-2">
                          <Input
                            aria-label={`${t('referenceAdmin.nameEn')} — ${item.code}`}
                            value={draft.en}
                            onChange={(e) => setDrafts((prev) => ({ ...prev, [item.code]: { ...draft, en: e.target.value } }))}
                          />
                          <Input
                            aria-label={`${t('referenceAdmin.nameAr')} — ${item.code}`}
                            value={draft.ar}
                            onChange={(e) => setDrafts((prev) => ({ ...prev, [item.code]: { ...draft, ar: e.target.value } }))}
                          />
                        </div>
                      </TableCell>
                      <TableCell>
                        <Badge tone={item.isActive ? 'success' : 'neutral'}>
                          {item.isActive ? t('referenceAdmin.active') : t('referenceAdmin.inactive')}
                        </Badge>
                        {item.isRequired === true ? (
                          <span className="ms-2"><Badge tone="info">{t('referenceAdmin.required')}</Badge></span>
                        ) : null}
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-2">
                          <Button
                            size="sm"
                            disabled={!dirty || renameMutation.isPending}
                            onClick={() => renameMutation.mutate({ item, nameAr: draft.ar, nameEn: draft.en })}
                          >
                            {t('referenceAdmin.save')}
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            disabled={activeMutation.isPending}
                            onClick={() => activeMutation.mutate({ code: item.code, isActive: !item.isActive })}
                          >
                            {item.isActive ? t('referenceAdmin.deactivate') : t('referenceAdmin.reactivate')}
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          )}
          <p className="mt-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('referenceAdmin.inactiveNotice')}
          </p>
        </Card>
      )}
    </div>
  )
}
