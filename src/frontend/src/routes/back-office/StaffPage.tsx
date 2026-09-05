import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Badge, Button, Card, Dialog, Field, Input, Select, SkeletonTable,
  Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast,
} from '../../components/ui'
import {
  inviteStaff, listStaff, setStaffActive, changeStaffRole, resetStaffMfa,
  type Staff, type StaffAccount,
} from '../../api/staff'
import { SupplierApiError } from '../../api/supplier'

// Task #28: deliberately excludes supplier_admin/supplier_user - those accounts come from
// supplier registration or the supplier-side team invite, not this staff-only flow. Mirrors
// InviteStaffHandler's own InvitableRoles set exactly.
const STAFF_ROLES = ['onboarding_reviewer', 'procurement_officer', 'procurement_manager', 'evaluator', 'ministry_viewer', 'system_admin'] as const

const inviteSchema = z.object({
  email: z.string().email(),
  fullName: z.string().min(1),
  role: z.enum(STAFF_ROLES),
})
type InviteFormValues = z.infer<typeof inviteSchema>

function InviteStaffDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { t } = useTranslation()
  const { notify } = useToast()
  const {
    register,
    handleSubmit,
    setValue,
    watch,
    reset,
    formState: { errors },
  } = useForm<InviteFormValues>({ resolver: zodResolver(inviteSchema), defaultValues: { role: 'onboarding_reviewer' } })
  const role = watch('role')

  const inviteMutation = useMutation({
    mutationFn: (values: InviteFormValues) => inviteStaff(values),
    onSuccess: (staff: Staff) => {
      notify({ kind: 'success', title: t('staff.invited', { email: staff.email }) })
      reset()
      onOpenChange(false)
    },
    onError: (err) => notify({ kind: 'danger', title: err instanceof SupplierApiError ? err.message : t('staff.errors.inviteFailed') }),
  })

  return (
    <Dialog open={open} onOpenChange={(o) => { onOpenChange(o); if (!o) reset() }} title={t('staff.inviteTitle')}>
      <form className="flex flex-col gap-4" onSubmit={handleSubmit((v) => inviteMutation.mutate(v))} noValidate>
        <Field label={t('staff.fields.fullName')} error={errors.fullName ? t('staff.errors.fullNameRequired') : undefined} required>
          {(p) => <Input {...p} {...register('fullName')} />}
        </Field>
        <Field label={t('staff.fields.email')} error={errors.email ? t('staff.errors.emailInvalid') : undefined} required>
          {(p) => <Input type="email" {...p} {...register('email')} />}
        </Field>
        <Field label={t('staff.fields.role')} required>
          {(p) => (
            <Select
              id={p.id}
              value={role}
              onValueChange={(v) => setValue('role', v as (typeof STAFF_ROLES)[number])}
              options={STAFF_ROLES.map((r) => ({ value: r, label: t(`staff.roles.${r}`) }))}
            />
          )}
        </Field>
        {inviteMutation.error instanceof SupplierApiError ? (
          <p role="alert" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-danger-fg)' }}>
            {inviteMutation.error.message}
          </p>
        ) : null}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            {t('staff.cancel')}
          </Button>
          <Button type="submit" isLoading={inviteMutation.isPending}>
            {t('staff.invite')}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}

export function StaffPage() {
  const { t } = useTranslation()
  const [dialogOpen, setDialogOpen] = useState(false)

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('staff.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('staff.subtitle')}
        </p>
      </div>

      <Card title={t('staff.inviteTitle')} action={<Button size="sm" onClick={() => setDialogOpen(true)}>{t('staff.invite')}</Button>}>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('staff.hint')}</p>
      </Card>

      {/*
        T-077/SCR-701 and SCR-702, both P0 and both previously absent - along with the endpoints behind
        them. An administrator could invite an account and then never see it again, so an account created
        in error could not be removed at all.

        One table rather than a list plus a detail page: everything SCR-702 lists as its content - role,
        activation, MFA reset - is a single action on a single row, and a second screen to reach three
        buttons would be a second screen to keep in step.
      */}
      <StaffAccounts />

      <InviteStaffDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </div>
  )
}

function StaffAccounts() {
  const { t } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const staffQuery = useQuery({ queryKey: ['staff'], queryFn: () => listStaff() })

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['staff'] })
  const onError = (error: unknown, fallback: string) => {
    // Branched on §7's machine-stable CODE, not on the human message - which is what §7 tells clients to
    // do, and a match on wording would break the day the wording changed.
    //
    // The two refusals worth naming: acting on your own account, and the last administrator. Both are
    // things an administrator has to understand rather than retry.
    const code = error instanceof SupplierApiError ? (error.code ?? '') : ''
    const message =
      code === 'CANNOT_ACT_ON_OWN_ACCOUNT' ? t('staff.errors.cannotActOnSelf')
        : code === 'WOULD_LOCK_OUT_ADMINISTRATION' ? t('staff.errors.wouldLockOutAdministration')
          : fallback
    notify({ kind: 'danger', title: message })
  }

  const activeMutation = useMutation({
    mutationFn: ({ userId, isActive }: { userId: string; isActive: boolean }) => setStaffActive(userId, isActive),
    onSuccess: refresh,
    onError: (error) => onError(error, t('staff.errors.updateFailed')),
  })

  const roleMutation = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: string }) => changeStaffRole(userId, role),
    onSuccess: () => { refresh(); notify({ kind: 'success', title: t('staff.roleChanged') }) },
    onError: (error) => onError(error, t('staff.errors.updateFailed')),
  })

  const mfaMutation = useMutation({
    mutationFn: (userId: string) => resetStaffMfa(userId),
    onSuccess: () => { refresh(); notify({ kind: 'success', title: t('staff.mfaReset') }) },
    onError: (error) => onError(error, t('staff.errors.updateFailed')),
  })

  if (staffQuery.isLoading) return <SkeletonTable label={t('common.loading')} />
  if (staffQuery.isError) {
    return (
      <Card title={t('staff.accountsTitle')}>
        <p>{t('staff.errors.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => void staffQuery.refetch()}>{t('staff.retry')}</Button>
      </Card>
    )
  }

  const accounts: StaffAccount[] = staffQuery.data?.data ?? []

  return (
    <Card title={t('staff.accountsTitle')}>
      {accounts.length === 0 ? (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('staff.noAccounts')}</p>
      ) : (
        <Table caption={t('staff.accountsTitle')}>
          <TableHead>
            <TableHeaderCell>{t('staff.fields.fullName')}</TableHeaderCell>
            <TableHeaderCell>{t('staff.fields.email')}</TableHeaderCell>
            <TableHeaderCell>{t('staff.fields.role')}</TableHeaderCell>
            <TableHeaderCell>{t('staff.status')}</TableHeaderCell>
            <TableHeaderCell>{t('staff.actions')}</TableHeaderCell>
          </TableHead>
          <TableBody>
            {accounts.map((account) => (
              <TableRow key={account.userId}>
                <TableCell>{account.fullName}</TableCell>
                <TableCell>{account.email}</TableCell>
                <TableCell>
                  <Select
                    value={account.role ?? undefined}
                    onValueChange={(role: string) => roleMutation.mutate({ userId: account.userId, role })}
                    options={STAFF_ROLES.map((r) => ({ value: r, label: t(`staff.roles.${r}`) }))}
                    placeholder={t('staff.fields.role')}
                  />
                </TableCell>
                <TableCell>
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge tone={account.isActive ? 'success' : 'neutral'}>
                      {account.isActive ? t('staff.active') : t('staff.inactive')}
                    </Badge>
                    {/* Both facts that make a row actionable: whether the second factor is enrolled, and
                        how many sessions are live - a deactivation that left sessions alive would only
                        stop the next sign-in. */}
                    {account.mfaEnabled ? <Badge tone="info">{t('staff.mfaOn')}</Badge> : null}
                    {account.activeSessionCount > 0 ? (
                      <Badge tone="neutral">{t('staff.sessions', { count: account.activeSessionCount })}</Badge>
                    ) : null}
                  </div>
                </TableCell>
                <TableCell>
                  <div className="flex flex-wrap gap-2">
                    <Button
                      size="sm"
                      variant="ghost"
                      disabled={activeMutation.isPending}
                      onClick={() => activeMutation.mutate({ userId: account.userId, isActive: !account.isActive })}
                    >
                      {account.isActive ? t('staff.deactivate') : t('staff.reactivate')}
                    </Button>
                    <Button
                      size="sm"
                      variant="ghost"
                      disabled={mfaMutation.isPending}
                      onClick={() => mfaMutation.mutate(account.userId)}
                    >
                      {t('staff.resetMfa')}
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </Card>
  )
}
