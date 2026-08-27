import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Dialog, Field, Input, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../components/ui'
import { useToast } from '../components/ui'
import { listTeam, inviteTeamMember, disableTeamMember } from '../api/team'
import { SupplierApiError } from '../api/supplier'

const inviteSchema = z.object({
  fullName: z.string().min(1),
  email: z.string().email(),
})
type InviteFormValues = z.infer<typeof inviteSchema>

/** SCR-160-equivalent (FEAT-04.8/MSP-55): SCREEN-INVENTORY.md doesn't have a settings/team route
 * scaffolded yet, so this lands at /team under the supplier shell, matching the SCR-160 route the
 * inventory names ("/team", supplier_admin, invite/manage delegated supplier_users). */
export function TeamPage() {
  const { t } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const [inviteOpen, setInviteOpen] = useState(false)

  const teamQuery = useQuery({ queryKey: ['team'], queryFn: listTeam })

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<InviteFormValues>({ resolver: zodResolver(inviteSchema) })

  const inviteMutation = useMutation({
    mutationFn: (values: InviteFormValues) => inviteTeamMember(values),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['team'] })
      notify({ kind: 'success', title: t('team.inviteSent') })
      setInviteOpen(false)
      reset()
    },
    onError: (err) => {
      const message = err instanceof SupplierApiError && err.status === 409 ? t('team.duplicateEmail') : t('team.inviteFailed')
      notify({ kind: 'danger', title: message })
    },
  })

  const disableMutation = useMutation({
    mutationFn: (userId: string) => disableTeamMember(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['team'] })
      notify({ kind: 'success', title: t('team.disabled') })
    },
    onError: () => notify({ kind: 'danger', title: t('team.disableFailed') }),
  })

  if (teamQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }

  const members = teamQuery.data ?? []

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('team.title')}
          </h1>
          <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('team.subtitle')}
          </p>
        </div>
        <Button onClick={() => setInviteOpen(true)}>{t('team.invite')}</Button>
      </div>

      <Card title={t('team.membersTitle')}>
        {members.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('team.empty')}</p>
        ) : (
          <Table caption={t('team.membersTitle')}>
            <TableHead>
              <TableHeaderCell>{t('team.fields.fullName')}</TableHeaderCell>
              <TableHeaderCell>{t('team.fields.email')}</TableHeaderCell>
              <TableHeaderCell>{t('team.status')}</TableHeaderCell>
              <TableHeaderCell>{t('team.actions')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {members.map((m) => (
                <TableRow key={m.userId}>
                  <TableCell>{m.fullName}</TableCell>
                  <TableCell>{m.email}</TableCell>
                  <TableCell>
                    <Badge tone={m.isActive ? 'success' : 'neutral'}>{m.isActive ? t('team.active') : t('team.disabledStatus')}</Badge>
                  </TableCell>
                  <TableCell>
                    {m.isActive ? (
                      <Button variant="ghost" size="sm" isLoading={disableMutation.isPending} onClick={() => disableMutation.mutate(m.userId)}>
                        {t('team.disable')}
                      </Button>
                    ) : null}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>

      <Dialog open={inviteOpen} onOpenChange={(o) => { setInviteOpen(o); if (!o) reset() }} title={t('team.inviteTitle')} description={t('team.inviteDescription')}>
        <form className="flex flex-col gap-4" onSubmit={handleSubmit((values) => inviteMutation.mutate(values))} noValidate>
          <Field label={t('team.fields.fullName')} error={errors.fullName ? t('team.errors.fullNameRequired') : undefined} required>
            {(p) => <Input {...p} {...register('fullName')} />}
          </Field>
          <Field label={t('team.fields.email')} error={errors.email ? t('team.errors.emailInvalid') : undefined} required>
            {(p) => <Input type="email" {...p} {...register('email')} />}
          </Field>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" onClick={() => setInviteOpen(false)}>
              {t('team.cancel')}
            </Button>
            <Button type="submit" isLoading={inviteMutation.isPending}>
              {t('team.sendInvite')}
            </Button>
          </div>
        </form>
      </Dialog>
    </div>
  )
}
