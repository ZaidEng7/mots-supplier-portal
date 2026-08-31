import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation } from '@tanstack/react-query'
import { Button, Card, Dialog, Field, Input, Select, useToast } from '../../components/ui'
import { inviteStaff, type Staff } from '../../api/staff'
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

      <InviteStaffDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </div>
  )
}
