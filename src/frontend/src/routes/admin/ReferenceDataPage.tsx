// SCR-710, SCR-711 and SCR-712 at /back-office/reference, for system_admin, P1 (FR-ADM-004).
//
// T-034 and T-059 landed the whole admin write surface in batch 9 and no screen consumed it, so adding a document type still
// meant a request by hand. Three inventory rows, one screen: the operations are identical across the five tables and only
// DocumentType carries extra flags, so five near-identical pages would be five places for the next change to miss - which
// is the same argument the single handler behind them already makes.
//
// NO DELETE, AND THE CODE IS NOT EDITABLE. Both are D-28: every one of these tables is referenced BY CODE from live rows
// with no cascade, so deleting a Category a published RFQ points at would leave that RFQ describing nothing, and renaming a
// code would silently change what a historical award was for. Deactivation hides a code from new selections and leaves every
// existing row readable. That is stated on the screen, because an administrator who cannot find a delete button deserves to
// know it is absent on purpose - and the code itself renders as text rather than an input, being the foreign key in every
// live row.
//
// THE REFUSALS. A duplicate code is one this screen can word itself; an invalid reference item is one only the server can
// explain, so its own message wins; everything else falls back to the caller's wording. They are written as statements rather
// than a chain, because the middle case defers to the SERVER's wording. The server names the rule that was broken - a
// duplicate code, or one longer than the column allows, since Currency.Code is 3 by ISO and the others 50, and a too-long
// code used to answer 500 from Postgres - and showing "invalid" instead would leave an administrator guessing which.
//
// WHERE a refusal is shown depends on whether it has a field to point at: a rejected NEW code belongs beside the code input,
// and a rejected rename or deactivation has no input of its own, so it goes to the toast. Doing both put the same sentence on
// screen twice.
//
// AN OMITTED FLAG MEANS UNCHANGED, not false: an administrator fixing an Arabic typo must not silently clear a document
// type's requiredness. BRULE-023's flag write sends the names unchanged alongside it, because the update contract takes the
// whole row and an omitted name would be read as a rename to empty. It is offered only where the flag exists, since a null
// means "this table has no such flag".
//
// BRULE-016's LINK SETS are fetched only on the document-types table: these links exist for no other table, and a request
// that could only ever return the same rows on four of five tabs is a request not worth making. The line beside them says
// what a link DOES rather than that it does nothing, because BRULE-016 has been live since D-59.
//
// BRULE-023's consequence is stated once and near the control rather than in a tooltip.
//
// The card is OUTSIDE the state rather than inside it, because loading used to render a bare skeleton with no card around it.

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {Badge, Button, Card, Field, Input, ListCard, PageHeading, Select, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast} from '../../components/ui'
import { SupplierApiError } from '../../api/supplier'
import {
  REFERENCE_TABLES, listReferenceItems, createReferenceItem, updateReferenceItem, setReferenceItemActive,
  getDocumentTypeCategories, setDocumentTypeCategories,
  type ReferenceItem, type ReferenceTable,
} from '../../api/referenceAdmin'

function messageFor(code: string | undefined, raised: unknown, fallback: string, duplicateText: string): string {
  if (code === 'DUPLICATE_RESOURCE') return duplicateText
  if (code === 'INVALID_REFERENCE_ITEM' && raised instanceof SupplierApiError) return raised.message
  return fallback
}

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

  const onError = (raised: unknown, fallback: string, surface: 'field' | 'toast') => {
    const code = raised instanceof SupplierApiError ? (raised.code ?? '') : ''
    const message = messageFor(code, raised, fallback, t('referenceAdmin.errors.duplicateCode'))
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
        isRequired: item.isRequired, expiryTracked: item.expiryTracked,
        isAwardCritical: item.isAwardCritical,
      }),
    onSuccess: () => { refresh(); notify({ kind: 'success', title: t('referenceAdmin.renamed') }) },
    onError: (raised) => onError(raised, t('referenceAdmin.errors.updateFailed'), 'toast'),
  })

  const linksQuery = useQuery({
    queryKey: ['document-type-categories'],
    queryFn: getDocumentTypeCategories,
    enabled: table === 'document-types',
  })

  const categoriesQuery = useQuery({
    queryKey: ['reference-admin', 'categories'],
    queryFn: () => listReferenceItems('categories', false),
    enabled: table === 'document-types',
  })

  const linksMutation = useMutation({
    mutationFn: ({ code, categoryCodes }: { code: string; categoryCodes: string[] }) =>
      setDocumentTypeCategories(code, categoryCodes),
    onSuccess: async () => {
      notify({ kind: 'success', title: t('referenceAdmin.linksSaved') })
      await queryClient.invalidateQueries({ queryKey: ['document-type-categories'] })
    },
    onError: (raised) => onError(raised, t('referenceAdmin.errors.updateFailed'), 'toast'),
  })

  const awardCriticalMutation = useMutation({
    mutationFn: ({ item, next }: { item: ReferenceItem; next: boolean }) =>
      updateReferenceItem(table, item.code, {
        nameAr: item.nameAr, nameEn: item.nameEn,
        isRequired: item.isRequired, expiryTracked: item.expiryTracked,
        isAwardCritical: next,
      }),
    onSuccess: () => { refresh(); notify({ kind: 'success', title: t('referenceAdmin.awardCriticalSaved') }) },
    onError: (raised) => onError(raised, t('referenceAdmin.errors.updateFailed'), 'toast'),
  })

  const [drafts, setDrafts] = useState<Record<string, { ar: string; en: string }>>({})
  const draftFor = (item: ReferenceItem) => drafts[item.code] ?? { ar: item.nameAr, en: item.nameEn }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <PageHeading title={t('referenceAdmin.title')} subtitle={t('referenceAdmin.subtitle')} />
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
        <p className="mt-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('referenceAdmin.noDeleteNotice')}
        </p>
      </Card>

      <ListCard
        title={t(`adminOverview.tables.${table}`)}
        query={{ ...itemsQuery, isPending: itemsQuery.isLoading }}
        isEmpty={(itemsQuery.data ?? []).length === 0}
        labels={{ loading: t('common.loading'), error: t('referenceAdmin.errors.loadFailed'), empty: t('referenceAdmin.empty') }}
        footer={
          <p className="mt-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('referenceAdmin.inactiveNotice')}
          </p>
        }
      >
          <Table flush caption={t(`adminOverview.tables.${table}`)}>
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
                        {table === 'document-types' ? (
                          <div className="mt-2 flex flex-wrap gap-1">
                            {(categoriesQuery.data ?? []).map((category) => {
                              const linked = (linksQuery.data ?? [])
                                .find((l) => l.documentTypeCode === item.code)?.categoryCodes ?? []
                              const on = linked.includes(category.code)
                              return (
                                <Button
                                  key={category.code}
                                  size="sm"
                                  variant={on ? undefined : 'ghost'}
                                  disabled={linksMutation.isPending}
                                  onClick={() => linksMutation.mutate({
                                    code: item.code,
                                    categoryCodes: on
                                      ? linked.filter((c) => c !== category.code)
                                      : [...linked, category.code],
                                  })}
                                >
                                  {category.nameEn}
                                </Button>
                              )
                            })}
                          </div>
                        ) : null}
                        {item.isAwardCritical === true ? (
                          <span className="ms-2"><Badge tone="danger">{t('referenceAdmin.awardCritical')}</Badge></span>
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
                          {item.isAwardCritical !== null ? (
                            <Button
                              size="sm"
                              variant="ghost"
                              disabled={awardCriticalMutation.isPending}
                              onClick={() => awardCriticalMutation.mutate({ item, next: !item.isAwardCritical })}
                            >
                              {item.isAwardCritical
                                ? t('referenceAdmin.clearAwardCritical')
                                : t('referenceAdmin.setAwardCritical')}
                            </Button>
                          ) : null}
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
      </ListCard>
      {table === 'document-types' ? (
        <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('referenceAdmin.awardCriticalExplained')}
        </p>
      ) : null}

      {table === 'document-types' ? (
        <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('referenceAdmin.categoryLinksExplained')}
        </p>
      ) : null}
    </div>
  )
}
