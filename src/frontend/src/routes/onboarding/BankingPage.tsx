import { useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Dialog, Field, Input, Select, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../../components/ui'
import { useToast } from '../../components/ui'
import { OnboardingStepNav } from '../../components/OnboardingStepNav'
import { getOwnSupplier, SupplierApiError, type BankAccount, type SupplierProfile } from '../../api/supplier'
import { addBankAccount, updateBankAccount, removeBankAccount, setDefaultBankAccount, revealBankAccount } from '../../api/banking'
import { fetchCurrencies } from '../../api/reference'

const REVEAL_AUTO_HIDE_MS = 15_000

function isEditableState(state: string | undefined) {
  return state === 'EmailVerified' || state === 'ProfileInProgress' || state === 'InfoRequested'
}

const bankSchema = z.object({
  accountHolderName: z.string().min(1),
  bankName: z.string().min(1),
  branchName: z.string().optional(),
  accountNumber: z.string().optional(),
  swiftBic: z.string().optional(),
  currencyCode: z.string().min(1),
})
type BankFormValues = z.infer<typeof bankSchema>

// zod doesn't need branch-based conditional required-ness here: the account number is required
// only when adding (not editing), enforced manually below rather than via a dynamically-typed
// resolver (which confuses react-hook-form's inferred FieldValues type across the two branches).

function BankAccountDialog({
  open,
  onOpenChange,
  initial,
  currencyOptions,
  onSubmit,
  isSaving,
  apiError,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  initial?: BankAccount
  currencyOptions: { value: string; label: string }[]
  onSubmit: (values: BankFormValues) => void
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
    setError,
    formState: { errors },
  } = useForm<BankFormValues>({
    resolver: zodResolver(bankSchema),
    values: {
      accountHolderName: initial?.accountHolderName ?? '',
      bankName: initial?.bankName ?? '',
      branchName: initial?.branchName ?? '',
      accountNumber: '',
      swiftBic: initial?.swiftBic ?? '',
      currencyCode: initial?.currencyCode ?? '',
    },
  })
  const currencyCode = watch('currencyCode')

  const submit = handleSubmit((values) => {
    if (!initial && !values.accountNumber) {
      setError('accountNumber', { type: 'required' })
      return
    }
    onSubmit(values)
  })

  return (
    <Dialog open={open} onOpenChange={(o) => { onOpenChange(o); if (!o) reset() }} title={initial ? t('banking.editAccount') : t('banking.addAccount')}>
      <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
        <Field label={t('banking.fields.accountHolderName')} error={errors.accountHolderName ? t('banking.errors.accountHolderRequired') : undefined} required>
          {(p) => <Input {...p} {...register('accountHolderName')} />}
        </Field>
        <Field label={t('banking.fields.bankName')} error={errors.bankName ? t('banking.errors.bankNameRequired') : undefined} required>
          {(p) => <Input {...p} {...register('bankName')} />}
        </Field>
        <Field label={t('banking.fields.branchName')}>{(p) => <Input {...p} {...register('branchName')} />}</Field>
        <Field
          label={t('banking.fields.accountNumber')}
          hint={initial ? t('banking.accountNumberEditHint') : undefined}
          error={errors.accountNumber ? t('banking.errors.accountNumberRequired') : undefined}
          required={!initial}
        >
          {(p) => <Input autoComplete="off" {...p} {...register('accountNumber')} />}
        </Field>
        <div className="grid grid-cols-2 gap-4">
          <Field label={t('banking.fields.swiftBic')}>{(p) => <Input {...p} {...register('swiftBic')} />}</Field>
          <Field label={t('banking.fields.currencyCode')} required>
            {(p) => (
              <Select id={p.id} value={currencyCode || undefined} onValueChange={(v) => setValue('currencyCode', v)} options={currencyOptions} placeholder={t('banking.fields.currencyCode')} />
            )}
          </Field>
        </div>
        {apiError ? (
          <p role="alert" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-danger-fg)' }}>
            {apiError}
          </p>
        ) : null}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            {t('banking.cancel')}
          </Button>
          <Button type="submit" isLoading={isSaving}>
            {t('banking.save')}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}

function RevealCell({ account }: { account: BankAccount }) {
  const { t } = useTranslation()
  const { notify } = useToast()
  const [revealed, setRevealed] = useState<string | null>(null)
  const hideTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const revealMutation = useMutation({
    mutationFn: () => revealBankAccount(account.id),
    onSuccess: (accountNumber) => {
      setRevealed(accountNumber)
      if (hideTimer.current) clearTimeout(hideTimer.current)
      hideTimer.current = setTimeout(() => setRevealed(null), REVEAL_AUTO_HIDE_MS)
    },
    onError: () => notify({ kind: 'danger', title: t('banking.revealFailed') }),
  })

  useEffect(() => () => { if (hideTimer.current) clearTimeout(hideTimer.current) }, [])

  if (revealed) {
    return (
      <div className="flex items-center gap-2">
        <bdi dir="ltr" className="font-mono" style={{ color: 'var(--color-text-primary)' }}>
          {revealed}
        </bdi>
        <Button variant="ghost" size="sm" onClick={() => { setRevealed(null); if (hideTimer.current) clearTimeout(hideTimer.current) }}>
          {t('banking.hide')}
        </Button>
      </div>
    )
  }

  return (
    <div className="flex items-center gap-2">
      <bdi dir="ltr" className="font-mono" style={{ color: 'var(--color-text-primary)' }}>
        {account.maskedAccountNumber}
      </bdi>
      <Button variant="ghost" size="sm" isLoading={revealMutation.isPending} onClick={() => revealMutation.mutate()}>
        {t('banking.reveal')}
      </Button>
    </div>
  )
}

export function BankingPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const profileQuery = useQuery({ queryKey: ['own-supplier'], queryFn: getOwnSupplier })
  const currenciesQuery = useQuery({ queryKey: ['currencies'], queryFn: fetchCurrencies })
  const profile = profileQuery.data
  const editable = isEditableState(profile?.onboardingState)

  const [dialog, setDialog] = useState<{ open: boolean; account?: BankAccount }>({ open: false })
  const [rowError, setRowError] = useState<string | null>(null)

  const onProfile = (data: SupplierProfile) => queryClient.setQueryData(['own-supplier'], data)

  const saveMutation = useMutation({
    mutationFn: (values: BankFormValues) => {
      const payload = {
        accountHolderName: values.accountHolderName,
        bankName: values.bankName,
        branchName: values.branchName || null,
        accountNumber: values.accountNumber || null,
        swiftBic: values.swiftBic || null,
        currencyCode: values.currencyCode,
      }
      return dialog.account ? updateBankAccount(dialog.account.id, payload) : addBankAccount({ ...payload, accountNumber: payload.accountNumber ?? '' })
    },
    onSuccess: (data) => { onProfile(data); setDialog({ open: false }) },
  })

  const removeMutation = useMutation({
    mutationFn: (id: string) => removeBankAccount(id),
    onSuccess: (data) => { onProfile(data); setRowError(null) },
    onError: (err) => setRowError(err instanceof SupplierApiError ? err.message : t('banking.errors.removeFailed')),
  })

  const setDefaultMutation = useMutation({
    mutationFn: (id: string) => setDefaultBankAccount(id),
    onSuccess: onProfile,
  })

  if (profileQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }

  const accounts = profile?.bankAccounts ?? []
  const currencyOptions = (currenciesQuery.data ?? []).map((c) => ({ value: c.code, label: c.code }))

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('banking.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('banking.subtitle')}
        </p>
      </div>

      <OnboardingStepNav />

      <Card title={t('banking.accountsTitle')} action={editable ? <Button size="sm" onClick={() => setDialog({ open: true })}>{t('banking.addAccount')}</Button> : null}>
        {rowError ? (
          <p role="alert" className="mb-3 rounded-[0.375rem] px-3 py-2 text-[length:var(--text-body-sm)]" style={{ backgroundColor: 'var(--danger-50)', color: 'var(--danger-600)' }}>
            {rowError}
          </p>
        ) : null}
        {accounts.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('banking.empty')}</p>
        ) : (
          <Table caption={t('banking.accountsTitle')}>
            <TableHead>
              <TableHeaderCell>{t('banking.fields.bankName')}</TableHeaderCell>
              <TableHeaderCell>{t('banking.fields.accountHolderName')}</TableHeaderCell>
              <TableHeaderCell>{t('banking.fields.accountNumber')}</TableHeaderCell>
              <TableHeaderCell>{t('banking.fields.currencyCode')}</TableHeaderCell>
              <TableHeaderCell>{t('banking.default')}</TableHeaderCell>
              {editable ? <TableHeaderCell>{t('banking.actions')}</TableHeaderCell> : null}
            </TableHead>
            <TableBody>
              {accounts.map((a) => (
                <TableRow key={a.id}>
                  <TableCell>{a.bankName}</TableCell>
                  <TableCell>{a.accountHolderName}</TableCell>
                  <TableCell>
                    <RevealCell account={a} />
                  </TableCell>
                  <TableCell>{a.currencyCode}</TableCell>
                  <TableCell>
                    {a.isDefault ? (
                      <Badge tone="brand">{t('banking.isDefault')}</Badge>
                    ) : editable ? (
                      <Button variant="ghost" size="sm" isLoading={setDefaultMutation.isPending} onClick={() => setDefaultMutation.mutate(a.id)}>
                        {t('banking.makeDefault')}
                      </Button>
                    ) : null}
                  </TableCell>
                  {editable ? (
                    <TableCell>
                      <div className="flex flex-wrap gap-2">
                        <Button variant="ghost" size="sm" onClick={() => setDialog({ open: true, account: a })}>
                          {t('banking.edit')}
                        </Button>
                        <Button variant="ghost" size="sm" isLoading={removeMutation.isPending} onClick={() => removeMutation.mutate(a.id)}>
                          {t('banking.remove')}
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

      <BankAccountDialog
        open={dialog.open}
        onOpenChange={(open) => setDialog((s) => ({ ...s, open }))}
        initial={dialog.account}
        currencyOptions={currencyOptions}
        onSubmit={(values) => saveMutation.mutate(values)}
        isSaving={saveMutation.isPending}
        apiError={saveMutation.error instanceof SupplierApiError ? saveMutation.error.message : undefined}
      />
    </div>
  )
}
