import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Dialog, Field, Input, PhoneInput, Select, SkeletonList, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../../components/ui'
import { invalidateQuietly } from '../../lib/queryClient'
import {
  addOrgUnit,
  createOrganization,
  createSupplierOrgLink,
  listOrganizations,
  listSupplierOrgLinks,
  removeOrgUnit,
  removeSupplierOrgLink,
  OrganizationApiError,
  type Organization,
  type OrganizationType,
} from '../../api/organizations'

const ORG_TYPES: OrganizationType[] = ['Hotel', 'MotBody', 'Ministry']

const createOrgSchema = z.object({
  legalNameAr: z.string().min(1),
  legalNameEn: z.string().min(1),
  organizationType: z.enum(['Hotel', 'MotBody', 'Ministry']),
  contactEmail: z.string().optional(),
  contactPhone: z.string().optional(),
})
type CreateOrgFormValues = z.infer<typeof createOrgSchema>

function CreateOrganizationDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { notify } = useToast()
  const {
    register,
    handleSubmit,
    setValue,
    watch,
    reset,
    formState: { errors },
  } = useForm<CreateOrgFormValues>({ resolver: zodResolver(createOrgSchema), defaultValues: { organizationType: 'Hotel' } })
  const organizationType = watch('organizationType')
  const contactPhone = watch('contactPhone')

  const createMutation = useMutation({
    mutationFn: (values: CreateOrgFormValues) =>
      createOrganization({
        legalNameAr: values.legalNameAr,
        legalNameEn: values.legalNameEn,
        organizationType: values.organizationType,
        contactEmail: values.contactEmail || null,
        contactPhone: values.contactPhone || null,
      }),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['organizations'] })
      notify({ kind: 'success', title: t('organizations.created') })
      onOpenChange(false)
    },
    onError: (err) => notify({ kind: 'danger', title: err instanceof OrganizationApiError ? err.message : t('organizations.errors.createFailed') }),
  })

  return (
    <Dialog open={open} onOpenChange={(o) => { onOpenChange(o); if (!o) reset() }} title={t('organizations.createTitle')}>
      <form className="flex flex-col gap-4" onSubmit={handleSubmit((v) => createMutation.mutate(v))} noValidate>
        <Field label={t('organizations.fields.legalNameAr')} error={errors.legalNameAr ? t('organizations.errors.nameRequired') : undefined} required>
          {(p) => <Input dir="rtl" {...p} {...register('legalNameAr')} />}
        </Field>
        <Field label={t('organizations.fields.legalNameEn')} error={errors.legalNameEn ? t('organizations.errors.nameRequired') : undefined} required>
          {(p) => <Input dir="ltr" {...p} {...register('legalNameEn')} />}
        </Field>
        <Field label={t('organizations.fields.organizationType')} required>
          {(p) => (
            <Select
              id={p.id}
              value={organizationType}
              onValueChange={(v) => setValue('organizationType', v as OrganizationType)}
              options={ORG_TYPES.map((v) => ({ value: v, label: t(`organizations.types.${v}`) }))}
            />
          )}
        </Field>
        <Field label={t('organizations.fields.contactEmail')}>{(p) => <Input type="email" {...p} {...register('contactEmail')} />}</Field>
        <Field label={t('organizations.fields.contactPhone')}>
          {(p) => <PhoneInput {...p} value={contactPhone ?? ''} onChange={(v) => setValue('contactPhone', v, { shouldValidate: true })} />}
        </Field>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            {t('organizations.cancel')}
          </Button>
          <Button type="submit" isLoading={createMutation.isPending}>
            {t('organizations.save')}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}

function OrgUnitsDialog({ org, open, onOpenChange }: { org: Organization | null; open: boolean; onOpenChange: (open: boolean) => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { notify } = useToast()
  const [name, setName] = useState('')

  const addMutation = useMutation({
    mutationFn: () => addOrgUnit(org!.id, name),
    onSuccess: () => {
      setName('')
      invalidateQuietly(queryClient, { queryKey: ['organizations'] })
    },
    onError: (err) => notify({ kind: 'danger', title: err instanceof OrganizationApiError ? err.message : t('organizations.errors.orgUnitFailed') }),
  })

  const removeMutation = useMutation({
    mutationFn: (orgUnitId: string) => removeOrgUnit(org!.id, orgUnitId),
    onSuccess: () => invalidateQuietly(queryClient, { queryKey: ['organizations'] }),
    onError: (err) => notify({ kind: 'danger', title: err instanceof OrganizationApiError ? err.message : t('organizations.errors.orgUnitFailed') }),
  })

  if (!org) return null

  return (
    <Dialog open={open} onOpenChange={onOpenChange} title={t('organizations.orgUnitsTitle', { name: org.legalNameEn })}>
      <div className="flex flex-col gap-4">
        {org.orgUnits.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('organizations.noOrgUnits')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {org.orgUnits.map((u) => (
              <li key={u.id} className="flex items-center justify-between rounded-[0.375rem] px-3 py-2" style={{ border: '1px solid var(--color-border)' }}>
                <span style={{ color: 'var(--color-text-primary)' }}>{u.name}</span>
                <Button variant="ghost" size="sm" isLoading={removeMutation.isPending} onClick={() => removeMutation.mutate(u.id)}>
                  {t('organizations.remove')}
                </Button>
              </li>
            ))}
          </ul>
        )}
        <div className="flex items-end gap-2">
          <Field label={t('organizations.fields.orgUnitName')}>{(p) => <Input {...p} value={name} onChange={(e) => setName(e.target.value)} />}</Field>
          <Button isLoading={addMutation.isPending} disabled={!name.trim()} onClick={() => addMutation.mutate()}>
            {t('organizations.add')}
          </Button>
        </div>
      </div>
    </Dialog>
  )
}

function SupplierLinksSection() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { notify } = useToast()
  const [referenceCode, setReferenceCode] = useState('')
  const [lookupCode, setLookupCode] = useState<string | null>(null)
  const [selectedOrgId, setSelectedOrgId] = useState<string | undefined>(undefined)

  const organizationsQuery = useQuery({ queryKey: ['organizations'], queryFn: listOrganizations })
  const linksQuery = useQuery({
    queryKey: ['supplier-org-links', lookupCode],
    queryFn: () => listSupplierOrgLinks(lookupCode!),
    enabled: lookupCode !== null,
  })

  const createLinkMutation = useMutation({
    mutationFn: () => createSupplierOrgLink(lookupCode!, selectedOrgId!),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['supplier-org-links', lookupCode] })
      notify({ kind: 'success', title: t('organizations.linkCreated') })
    },
    onError: (err) => notify({ kind: 'danger', title: err instanceof OrganizationApiError ? err.message : t('organizations.errors.linkFailed') }),
  })

  const removeLinkMutation = useMutation({
    mutationFn: (linkId: string) => removeSupplierOrgLink(linkId),
    onSuccess: () => invalidateQuietly(queryClient, { queryKey: ['supplier-org-links', lookupCode] }),
    onError: (err) => notify({ kind: 'danger', title: err instanceof OrganizationApiError ? err.message : t('organizations.errors.linkFailed') }),
  })

  const orgById = new Map((organizationsQuery.data ?? []).map((o) => [o.id, o]))

  return (
    <Card title={t('organizations.linksTitle')}>
      <div className="flex flex-col gap-4">
        <div className="flex items-end gap-2">
          <Field label={t('organizations.fields.supplierReferenceCode')}>
            {(p) => <Input {...p} value={referenceCode} onChange={(e) => setReferenceCode(e.target.value)} placeholder="SUP-2026-000001" />}
          </Field>
          <Button variant="secondary" disabled={!referenceCode.trim()} onClick={() => setLookupCode(referenceCode.trim())}>
            {t('organizations.lookup')}
          </Button>
        </div>

        {lookupCode ? (
          <div className="flex flex-col gap-3">
            {linksQuery.isLoading ? (
              <SkeletonList label={t('common.loading')} />
            ) : linksQuery.data && linksQuery.data.length > 0 ? (
              <ul className="flex flex-col gap-2">
                {linksQuery.data.map((link) => (
                  <li key={link.id} className="flex items-center justify-between rounded-[0.375rem] px-3 py-2" style={{ border: '1px solid var(--color-border)' }}>
                    <span style={{ color: 'var(--color-text-primary)' }}>{orgById.get(link.organizationId)?.legalNameEn ?? link.organizationId}</span>
                    <Button variant="ghost" size="sm" isLoading={removeLinkMutation.isPending} onClick={() => removeLinkMutation.mutate(link.id)}>
                      {t('organizations.remove')}
                    </Button>
                  </li>
                ))}
              </ul>
            ) : (
              <p style={{ color: 'var(--color-text-secondary)' }}>{t('organizations.noLinks')}</p>
            )}

            <div className="flex items-end gap-2">
              <Field label={t('organizations.fields.organization')}>
                {(p) => (
                  <Select
                    id={p.id}
                    value={selectedOrgId}
                    onValueChange={setSelectedOrgId}
                    options={(organizationsQuery.data ?? []).map((o) => ({ value: o.id, label: o.legalNameEn }))}
                    placeholder={t('organizations.fields.organization')}
                  />
                )}
              </Field>
              <Button isLoading={createLinkMutation.isPending} disabled={!selectedOrgId} onClick={() => createLinkMutation.mutate()}>
                {t('organizations.linkAdd')}
              </Button>
            </div>
          </div>
        ) : null}
      </div>
    </Card>
  )
}

export function OrganizationsPage() {
  const { t } = useTranslation()
  const [createOpen, setCreateOpen] = useState(false)
  const [orgUnitsDialogOrg, setOrgUnitsDialogOrg] = useState<Organization | null>(null)

  const organizationsQuery = useQuery({ queryKey: ['organizations'], queryFn: listOrganizations })
  const organizations = organizationsQuery.data ?? []

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('organizations.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('organizations.subtitle')}
        </p>
      </div>

      <Card title={t('organizations.listTitle')} action={<Button size="sm" onClick={() => setCreateOpen(true)}>{t('organizations.createTitle')}</Button>}>
        {organizations.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('organizations.empty')}</p>
        ) : (
          <Table caption={t('organizations.listTitle')}>
            <TableHead>
              <TableHeaderCell>{t('organizations.fields.legalNameEn')}</TableHeaderCell>
              <TableHeaderCell>{t('organizations.fields.organizationType')}</TableHeaderCell>
              <TableHeaderCell>{t('organizations.orgUnitsCount')}</TableHeaderCell>
              <TableHeaderCell>{t('organizations.actions')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {organizations.map((org) => (
                <TableRow key={org.id}>
                  <TableCell>{org.legalNameEn}</TableCell>
                  <TableCell>
                    <Badge tone={org.organizationType === 'Ministry' ? 'brand' : 'neutral'}>{t(`organizations.types.${org.organizationType}`)}</Badge>
                  </TableCell>
                  <TableCell>{org.orgUnits.length}</TableCell>
                  <TableCell>
                    <Button variant="ghost" size="sm" onClick={() => setOrgUnitsDialogOrg(org)}>
                      {t('organizations.manageOrgUnits')}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>

      <SupplierLinksSection />

      <CreateOrganizationDialog open={createOpen} onOpenChange={setCreateOpen} />
      <OrgUnitsDialog org={orgUnitsDialogOrg ? organizations.find((o) => o.id === orgUnitsDialogOrg.id) ?? orgUnitsDialogOrg : null} open={orgUnitsDialogOrg !== null} onOpenChange={(o) => { if (!o) setOrgUnitsDialogOrg(null) }} />
    </div>
  )
}
