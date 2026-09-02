import { useState } from 'react'
import { nextPageParam } from '../api/listEnvelope'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { invalidateQuietly } from '../lib/queryClient'
import { Badge, Button, Card, Dialog, Field, Input, SkeletonList, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../components/ui'
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

  // MSP-84: real pagination needs a real walking consumer - loading page one and stopping would
  // silently hide the rest of the team with no error, no empty state, nothing visibly wrong.
  const teamQuery = useInfiniteQuery({
    queryKey: ['team'],
    queryFn: ({ pageParam }) => listTeam(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: nextPageParam,
  })

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<InviteFormValues>({ resolver: zodResolver(inviteSchema) })

  const inviteMutation = useMutation({
    mutationFn: (values: InviteFormValues) => inviteTeamMember(values),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['team'] })
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
      invalidateQuietly(queryClient, { queryKey: ['team'] })
      notify({ kind: 'success', title: t('team.disabled') })
    },
    onError: () => notify({ kind: 'danger', title: t('team.disableFailed') }),
  })

  if (teamQuery.isLoading) {
    return <SkeletonList label={t('common.loading')} />
  }

  const members = teamQuery.data?.pages.flatMap((p) => p.data) ?? []

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
        {teamQuery.hasNextPage ? (
          <div className="mt-3">
            <Button variant="secondary" isLoading={teamQuery.isFetchingNextPage} onClick={() => teamQuery.fetchNextPage()}>
              {t('team.loadMore')}
            </Button>
          </div>
        ) : null}
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
