import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Dialog, Field, Input, Select, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../../components/ui'
import { OnboardingStepNav } from '../../components/OnboardingStepNav'
import { getOwnSupplier, SupplierApiError, type Address, type Branch, type SupplierProfile } from '../../api/supplier'
import { addAddress, updateAddress, removeAddress, addBranch, updateBranch, removeBranch } from '../../api/addresses'
import { fetchRegions } from '../../api/reference'

const ADDRESS_KINDS = ['HeadOffice', 'Billing', 'Branch'] as const

const addressSchema = z.object({
  kind: z.enum(ADDRESS_KINDS),
  line1: z.string().min(1),
  line2: z.string().optional(),
  city: z.string().min(1),
  regionCode: z.string().min(1),
  country: z.string().min(1),
  postalCode: z.string().optional(),
})
type AddressFormValues = z.infer<typeof addressSchema>

const branchSchema = z.object({
  nameAr: z.string().min(1),
  nameEn: z.string().min(1),
  addressId: z.string().optional(),
})
type BranchFormValues = z.infer<typeof branchSchema>

function isEditableState(state: string | undefined) {
  return state === 'EmailVerified' || state === 'ProfileInProgress' || state === 'InfoRequested'
}

function AddressDialog({
  open,
  onOpenChange,
  initial,
  regionOptions,
  onSubmit,
  isSaving,
  apiError,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  initial?: Address
  regionOptions: { value: string; label: string }[]
  onSubmit: (values: AddressFormValues) => void
  isSaving: boolean
  apiError?: string
}) {
  const { t } = useTranslation()
  const {
    register,
    handleSubmit,
    setValue,
    watch,
    reset,
    formState: { errors },
  } = useForm<AddressFormValues>({
    resolver: zodResolver(addressSchema),
    values: {
      kind: (initial?.kind as (typeof ADDRESS_KINDS)[number]) ?? 'HeadOffice',
      line1: initial?.line1 ?? '',
      line2: initial?.line2 ?? '',
      city: initial?.city ?? '',
      regionCode: initial?.regionCode ?? '',
      country: initial?.country ?? '',
      postalCode: initial?.postalCode ?? '',
    },
  })
  const kind = watch('kind')
  const regionCode = watch('regionCode')

  return (
    <Dialog open={open} onOpenChange={(o) => { onOpenChange(o); if (!o) reset() }} title={initial ? t('addresses.editAddress') : t('addresses.addAddress')}>
      <form className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)} noValidate>
        <Field label={t('addresses.fields.kind')} required>
          {(p) => (
            <Select
              id={p.id}
              value={kind}
              onValueChange={(v) => setValue('kind', v as (typeof ADDRESS_KINDS)[number])}
              options={ADDRESS_KINDS.map((k) => ({ value: k, label: t(`addresses.kinds.${k}`) }))}
            />
          )}
        </Field>
        <Field label={t('addresses.fields.line1')} error={errors.line1 ? t('addresses.errors.line1Required') : undefined} required>
          {(p) => <Input {...p} {...register('line1')} />}
        </Field>
        <Field label={t('addresses.fields.line2')}>{(p) => <Input {...p} {...register('line2')} />}</Field>
        <div className="grid grid-cols-2 gap-4">
          <Field label={t('addresses.fields.city')} error={errors.city ? t('addresses.errors.cityRequired') : undefined} required>
            {(p) => <Input {...p} {...register('city')} />}
          </Field>
          <Field label={t('addresses.fields.regionCode')} required>
            {(p) => (
              <Select id={p.id} value={regionCode || undefined} onValueChange={(v) => setValue('regionCode', v)} options={regionOptions} placeholder={t('addresses.fields.regionCode')} />
            )}
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <Field label={t('addresses.fields.country')} error={errors.country ? t('addresses.errors.countryRequired') : undefined} required>
            {(p) => <Input {...p} {...register('country')} />}
          </Field>
          <Field label={t('addresses.fields.postalCode')}>{(p) => <Input {...p} {...register('postalCode')} />}</Field>
        </div>
        {apiError ? (
          <p role="alert" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-danger-fg)' }}>
            {apiError}
          </p>
        ) : null}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            {t('addresses.cancel')}
          </Button>
          <Button type="submit" isLoading={isSaving}>
            {t('addresses.save')}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}

function BranchDialog({
  open,
  onOpenChange,
  initial,
  addressOptions,
  onSubmit,
  isSaving,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  initial?: Branch
  addressOptions: { value: string; label: string }[]
  onSubmit: (values: BranchFormValues) => void
  isSaving: boolean
}) {
  const { t } = useTranslation()
  const {
    register,
    handleSubmit,
    setValue,
    watch,
    reset,
    formState: { errors },
  } = useForm<BranchFormValues>({
    resolver: zodResolver(branchSchema),
    values: { nameAr: initial?.nameAr ?? '', nameEn: initial?.nameEn ?? '', addressId: initial?.addressId ?? undefined },
  })
  const addressId = watch('addressId')

  return (
    <Dialog open={open} onOpenChange={(o) => { onOpenChange(o); if (!o) reset() }} title={initial ? t('addresses.editBranch') : t('addresses.addBranch')}>
      <form className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)} noValidate>
        <Field label={t('addresses.fields.nameAr')} error={errors.nameAr ? t('addresses.errors.nameArRequired') : undefined} required>
          {(p) => <Input dir="rtl" {...p} {...register('nameAr')} />}
        </Field>
        <Field label={t('addresses.fields.nameEn')} error={errors.nameEn ? t('addresses.errors.nameEnRequired') : undefined} required>
          {(p) => <Input dir="ltr" {...p} {...register('nameEn')} />}
        </Field>
        {addressOptions.length > 0 ? (
          <Field label={t('addresses.fields.linkedAddress')} hint={t('addresses.linkedAddressHint')}>
            {(p) => (
              <Select id={p.id} value={addressId} onValueChange={(v) => setValue('addressId', v)} options={addressOptions} placeholder={t('addresses.fields.linkedAddress')} />
            )}
          </Field>
        ) : null}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            {t('addresses.cancel')}
          </Button>
          <Button type="submit" isLoading={isSaving}>
            {t('addresses.save')}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}

export function AddressesPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const queryClient = useQueryClient()
  const profileQuery = useQuery({ queryKey: ['own-supplier'], queryFn: getOwnSupplier })
  const regionsQuery = useQuery({ queryKey: ['regions'], queryFn: fetchRegions })
  const profile = profileQuery.data
  const editable = isEditableState(profile?.onboardingState)

  const [addrDialog, setAddrDialog] = useState<{ open: boolean; addr?: Address }>({ open: false })
  const [branchDialog, setBranchDialog] = useState<{ open: boolean; branch?: Branch }>({ open: false })
  const [addrRowError, setAddrRowError] = useState<string | null>(null)

  const onProfile = (data: SupplierProfile) => queryClient.setQueryData(['own-supplier'], data)

  const addrMutation = useMutation({
    mutationFn: (values: AddressFormValues) => {
      const payload = { ...values, line2: values.line2 || null, postalCode: values.postalCode || null }
      return addrDialog.addr ? updateAddress(addrDialog.addr.id, payload) : addAddress(payload)
    },
    onSuccess: (data) => { onProfile(data); setAddrDialog({ open: false }) },
  })

  const removeAddrMutation = useMutation({
    mutationFn: (id: string) => removeAddress(id),
    onSuccess: (data) => { onProfile(data); setAddrRowError(null) },
    onError: (err) => setAddrRowError(err instanceof SupplierApiError ? err.message : t('addresses.errors.removeFailed')),
  })

  const branchMutation = useMutation({
    mutationFn: (values: BranchFormValues) => {
      const payload = { nameAr: values.nameAr, nameEn: values.nameEn, addressId: values.addressId || null, isActive: branchDialog.branch?.isActive ?? true }
      return branchDialog.branch ? updateBranch(branchDialog.branch.id, payload) : addBranch(payload)
    },
    onSuccess: (data) => { onProfile(data); setBranchDialog({ open: false }) },
  })

  const removeBranchMutation = useMutation({
    mutationFn: (id: string) => removeBranch(id),
    onSuccess: onProfile,
  })

  if (profileQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }

  const addresses = profile?.addresses ?? []
  const branches = profile?.branches ?? []
  const hasHeadOffice = addresses.some((a) => a.kind === 'HeadOffice')
  const missingHeadOffice = (profile?.missingProfileFields ?? []).includes('address')
  const regionOptions = (regionsQuery.data ?? []).map((r) => ({ value: r.code, label: isArabic ? r.nameAr : r.nameEn }))
  const addressOptions = addresses.map((a) => ({ value: a.id, label: `${t(`addresses.kinds.${a.kind}`)} — ${a.city}` }))

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('addresses.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('addresses.subtitle')}
        </p>
      </div>

      <OnboardingStepNav />

      {missingHeadOffice ? (
        <p role="alert" className="rounded-[0.5rem] px-4 py-3 text-[length:var(--text-body-sm)]" style={{ backgroundColor: 'var(--warning-50)', color: 'var(--warning-600)' }}>
          {t('addresses.missingHeadOffice')}
        </p>
      ) : null}

      <Card
        title={t('addresses.addressesTitle')}
        action={editable ? <Button size="sm" onClick={() => setAddrDialog({ open: true })}>{t('addresses.addAddress')}</Button> : null}
      >
        {addrRowError ? (
          <p role="alert" className="mb-3 rounded-[0.375rem] px-3 py-2 text-[length:var(--text-body-sm)]" style={{ backgroundColor: 'var(--danger-50)', color: 'var(--danger-600)' }}>
            {addrRowError}
          </p>
        ) : null}
        {addresses.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('addresses.empty')}</p>
        ) : (
          <Table caption={t('addresses.addressesTitle')}>
            <TableHead>
              <TableHeaderCell>{t('addresses.fields.kind')}</TableHeaderCell>
              <TableHeaderCell>{t('addresses.fields.line1')}</TableHeaderCell>
              <TableHeaderCell>{t('addresses.fields.city')}</TableHeaderCell>
              <TableHeaderCell>{t('addresses.fields.country')}</TableHeaderCell>
              {editable ? <TableHeaderCell>{t('addresses.actions')}</TableHeaderCell> : null}
            </TableHead>
            <TableBody>
              {addresses.map((a) => (
                <TableRow key={a.id}>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      {a.kind === 'HeadOffice' ? <Badge tone="brand">{t('addresses.kinds.HeadOffice')}</Badge> : t(`addresses.kinds.${a.kind}`)}
                    </div>
                  </TableCell>
                  <TableCell>{a.line1}</TableCell>
                  <TableCell>{a.city}</TableCell>
                  <TableCell>{a.country}</TableCell>
                  {editable ? (
                    <TableCell>
                      <div className="flex flex-wrap gap-2">
                        <Button variant="ghost" size="sm" onClick={() => setAddrDialog({ open: true, addr: a })}>
                          {t('addresses.edit')}
                        </Button>
                        <Button variant="ghost" size="sm" isLoading={removeAddrMutation.isPending} onClick={() => removeAddrMutation.mutate(a.id)}>
                          {t('addresses.remove')}
                        </Button>
                      </div>
                    </TableCell>
                  ) : null}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        {!hasHeadOffice && addresses.length > 0 ? (
          <p className="mt-3 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('addresses.needHeadOfficeHint')}
          </p>
        ) : null}
      </Card>

      <Card
        title={t('addresses.branchesTitle')}
        action={editable ? <Button size="sm" onClick={() => setBranchDialog({ open: true })}>{t('addresses.addBranch')}</Button> : null}
      >
        {branches.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('addresses.emptyBranches')}</p>
        ) : (
          <Table caption={t('addresses.branchesTitle')}>
            <TableHead>
              <TableHeaderCell>{t('addresses.fields.nameAr')}</TableHeaderCell>
              <TableHeaderCell>{t('addresses.fields.nameEn')}</TableHeaderCell>
              <TableHeaderCell>{t('addresses.fields.linkedAddress')}</TableHeaderCell>
              {editable ? <TableHeaderCell>{t('addresses.actions')}</TableHeaderCell> : null}
            </TableHead>
            <TableBody>
              {branches.map((b) => (
                <TableRow key={b.id}>
                  <TableCell>
                    <bdi dir="rtl">{b.nameAr}</bdi>
                  </TableCell>
                  <TableCell>
                    <bdi dir="ltr">{b.nameEn}</bdi>
                  </TableCell>
                  <TableCell>{addressOptions.find((o) => o.value === b.addressId)?.label ?? '—'}</TableCell>
                  {editable ? (
                    <TableCell>
                      <div className="flex flex-wrap gap-2">
                        <Button variant="ghost" size="sm" onClick={() => setBranchDialog({ open: true, branch: b })}>
                          {t('addresses.edit')}
                        </Button>
                        <Button variant="ghost" size="sm" isLoading={removeBranchMutation.isPending} onClick={() => removeBranchMutation.mutate(b.id)}>
                          {t('addresses.remove')}
                        </Button>
                      </div>
                    </TableCell>
                  ) : null}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>

      <AddressDialog
        open={addrDialog.open}
        onOpenChange={(open) => setAddrDialog((s) => ({ ...s, open }))}
        initial={addrDialog.addr}
        regionOptions={regionOptions}
        onSubmit={(values) => addrMutation.mutate(values)}
        isSaving={addrMutation.isPending}
        apiError={addrMutation.error instanceof SupplierApiError ? addrMutation.error.message : undefined}
      />

      <BranchDialog
        open={branchDialog.open}
        onOpenChange={(open) => setBranchDialog((s) => ({ ...s, open }))}
        initial={branchDialog.branch}
        addressOptions={addressOptions}
        onSubmit={(values) => branchMutation.mutate(values)}
        isSaving={branchMutation.isPending}
      />
    </div>
  )
}
