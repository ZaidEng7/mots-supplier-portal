import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Dialog, Field, Input, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../../components/ui'
import { OnboardingStepNav } from '../../components/OnboardingStepNav'
import { getOwnSupplier, SupplierApiError, type Representative, type Contact, type SupplierProfile } from '../../api/supplier'
import { addRepresentative, updateRepresentative, removeRepresentative, setPrimaryRepresentative } from '../../api/representatives'
import { addContact, updateContact, removeContact } from '../../api/contacts'

const personSchema = z.object({
  fullName: z.string().min(1),
  email: z.string().email(),
  phone: z.string().optional(),
})
type PersonFormValues = z.infer<typeof personSchema>

function isEditableState(state: string | undefined) {
  return state === 'EmailVerified' || state === 'ProfileInProgress' || state === 'InfoRequested'
}

function PersonDialog({
  open,
  onOpenChange,
  title,
  initial,
  extraField,
  onSubmit,
  isSaving,
  apiError,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  initial?: { fullName: string; email: string; phone?: string | null; extra?: string | null }
  extraField?: { label: string; key: 'position' | 'role' }
  onSubmit: (values: PersonFormValues & { extra?: string }) => void
  isSaving: boolean
  apiError?: string
}) {
  const { t } = useTranslation()
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<PersonFormValues & { extra?: string }>({
    resolver: zodResolver(personSchema.extend({ extra: z.string().optional() })),
    values: { fullName: initial?.fullName ?? '', email: initial?.email ?? '', phone: initial?.phone ?? '', extra: initial?.extra ?? '' },
  })

  return (
    <Dialog open={open} onOpenChange={(o) => { onOpenChange(o); if (!o) reset() }} title={title}>
      <form
        className="flex flex-col gap-4"
        onSubmit={handleSubmit((values) => onSubmit(values))}
        noValidate
      >
        <Field label={t('contacts.fields.fullName')} error={errors.fullName?.message ? t('contacts.errors.fullNameRequired') : undefined} required>
          {(p) => <Input {...p} {...register('fullName')} />}
        </Field>
        <Field label={t('contacts.fields.email')} error={errors.email?.message ? t('contacts.errors.emailInvalid') : undefined} required>
          {(p) => <Input type="email" {...p} {...register('email')} />}
        </Field>
        <Field label={t('contacts.fields.phone')}>{(p) => <Input {...p} {...register('phone')} />}</Field>
        {extraField ? <Field label={extraField.label}>{(p) => <Input {...p} {...register('extra')} />}</Field> : null}
        {apiError ? (
          <p role="alert" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-danger-fg)' }}>
            {apiError}
          </p>
        ) : null}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            {t('contacts.cancel')}
          </Button>
          <Button type="submit" isLoading={isSaving}>
            {t('contacts.save')}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}

export function ContactsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const profileQuery = useQuery({ queryKey: ['own-supplier'], queryFn: getOwnSupplier })
  const profile = profileQuery.data
  const editable = isEditableState(profile?.onboardingState)

  const [repDialog, setRepDialog] = useState<{ open: boolean; rep?: Representative }>({ open: false })
  const [contactDialog, setContactDialog] = useState<{ open: boolean; contact?: Contact }>({ open: false })
  const [repRowError, setRepRowError] = useState<string | null>(null)

  const onProfile = (data: SupplierProfile) => queryClient.setQueryData(['own-supplier'], data)

  const repMutation = useMutation({
    mutationFn: (values: PersonFormValues & { extra?: string }) => {
      const payload = { fullName: values.fullName, email: values.email, phone: values.phone || null, position: values.extra || null }
      return repDialog.rep ? updateRepresentative(repDialog.rep.id, payload) : addRepresentative(payload)
    },
    onSuccess: (data) => { onProfile(data); setRepDialog({ open: false }) },
  })

  const removeRepMutation = useMutation({
    mutationFn: (id: string) => removeRepresentative(id),
    onSuccess: (data) => { onProfile(data); setRepRowError(null) },
    onError: (err) => setRepRowError(err instanceof SupplierApiError ? err.message : t('contacts.errors.removeFailed')),
  })

  const setPrimaryMutation = useMutation({
    mutationFn: (id: string) => setPrimaryRepresentative(id),
    onSuccess: onProfile,
  })

  const contactMutation = useMutation({
    mutationFn: (values: PersonFormValues & { extra?: string }) => {
      const payload = { fullName: values.fullName, email: values.email, phone: values.phone || null, role: values.extra || null }
      return contactDialog.contact ? updateContact(contactDialog.contact.id, payload) : addContact(payload)
    },
    onSuccess: (data) => { onProfile(data); setContactDialog({ open: false }) },
  })

  const removeContactMutation = useMutation({
    mutationFn: (id: string) => removeContact(id),
    onSuccess: onProfile,
  })

  if (profileQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }

  const representatives = profile?.representatives ?? []
  const contacts = profile?.contacts ?? []

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('contacts.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('contacts.subtitle')}
        </p>
      </div>

      <OnboardingStepNav />

      <Card
        title={t('contacts.representativesTitle')}
        action={editable ? <Button size="sm" onClick={() => setRepDialog({ open: true })}>{t('contacts.addRepresentative')}</Button> : null}
      >
        {repRowError ? (
          <p role="alert" className="mb-3 rounded-[0.375rem] px-3 py-2 text-[length:var(--text-body-sm)]" style={{ backgroundColor: 'var(--danger-50)', color: 'var(--danger-600)' }}>
            {repRowError}
          </p>
        ) : null}
        {representatives.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('contacts.empty')}</p>
        ) : (
          <Table caption={t('contacts.representativesTitle')}>
            <TableHead>
              <TableHeaderCell>{t('contacts.fields.fullName')}</TableHeaderCell>
              <TableHeaderCell>{t('contacts.fields.email')}</TableHeaderCell>
              <TableHeaderCell>{t('contacts.fields.phone')}</TableHeaderCell>
              <TableHeaderCell>{t('contacts.fields.position')}</TableHeaderCell>
              <TableHeaderCell>{t('contacts.status')}</TableHeaderCell>
              {editable ? <TableHeaderCell>{t('contacts.actions')}</TableHeaderCell> : null}
            </TableHead>
            <TableBody>
              {representatives.map((rep) => (
                <TableRow key={rep.id}>
                  <TableCell>{rep.fullName}</TableCell>
                  <TableCell>{rep.email}</TableCell>
                  <TableCell>{rep.phone ? <bdi dir="ltr">{rep.phone}</bdi> : '—'}</TableCell>
                  <TableCell>{rep.position || '—'}</TableCell>
                  <TableCell>{rep.isPrimary ? <Badge tone="brand">{t('contacts.primary')}</Badge> : null}</TableCell>
                  {editable ? (
                    <TableCell>
                      <div className="flex flex-wrap gap-2">
                        {!rep.isPrimary ? (
                          <Button variant="ghost" size="sm" isLoading={setPrimaryMutation.isPending} onClick={() => setPrimaryMutation.mutate(rep.id)}>
                            {t('contacts.makePrimary')}
                          </Button>
                        ) : null}
                        <Button variant="ghost" size="sm" onClick={() => setRepDialog({ open: true, rep })}>
                          {t('contacts.edit')}
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          isLoading={removeRepMutation.isPending}
                          onClick={() => removeRepMutation.mutate(rep.id)}
                        >
                          {t('contacts.remove')}
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

      <Card
        title={t('contacts.contactsTitle')}
        action={editable ? <Button size="sm" onClick={() => setContactDialog({ open: true })}>{t('contacts.addContact')}</Button> : null}
      >
        {contacts.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('contacts.empty')}</p>
        ) : (
          <Table caption={t('contacts.contactsTitle')}>
            <TableHead>
              <TableHeaderCell>{t('contacts.fields.fullName')}</TableHeaderCell>
              <TableHeaderCell>{t('contacts.fields.email')}</TableHeaderCell>
              <TableHeaderCell>{t('contacts.fields.phone')}</TableHeaderCell>
              <TableHeaderCell>{t('contacts.fields.role')}</TableHeaderCell>
              {editable ? <TableHeaderCell>{t('contacts.actions')}</TableHeaderCell> : null}
            </TableHead>
            <TableBody>
              {contacts.map((c) => (
                <TableRow key={c.id}>
                  <TableCell>{c.fullName}</TableCell>
                  <TableCell>{c.email}</TableCell>
                  <TableCell>{c.phone ? <bdi dir="ltr">{c.phone}</bdi> : '—'}</TableCell>
                  <TableCell>{c.role || '—'}</TableCell>
                  {editable ? (
                    <TableCell>
                      <div className="flex flex-wrap gap-2">
                        <Button variant="ghost" size="sm" onClick={() => setContactDialog({ open: true, contact: c })}>
                          {t('contacts.edit')}
                        </Button>
                        <Button variant="ghost" size="sm" isLoading={removeContactMutation.isPending} onClick={() => removeContactMutation.mutate(c.id)}>
                          {t('contacts.remove')}
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

      <PersonDialog
        open={repDialog.open}
        onOpenChange={(open) => setRepDialog((s) => ({ ...s, open }))}
        title={repDialog.rep ? t('contacts.editRepresentative') : t('contacts.addRepresentative')}
        initial={repDialog.rep ? { fullName: repDialog.rep.fullName, email: repDialog.rep.email, phone: repDialog.rep.phone, extra: repDialog.rep.position } : undefined}
        extraField={{ label: t('contacts.fields.position'), key: 'position' }}
        onSubmit={(values) => repMutation.mutate(values)}
        isSaving={repMutation.isPending}
        apiError={repMutation.error instanceof SupplierApiError ? repMutation.error.message : undefined}
      />

      <PersonDialog
        open={contactDialog.open}
        onOpenChange={(open) => setContactDialog((s) => ({ ...s, open }))}
        title={contactDialog.contact ? t('contacts.editContact') : t('contacts.addContact')}
        initial={contactDialog.contact ? { fullName: contactDialog.contact.fullName, email: contactDialog.contact.email, phone: contactDialog.contact.phone, extra: contactDialog.contact.role } : undefined}
        extraField={{ label: t('contacts.fields.role'), key: 'role' }}
        onSubmit={(values) => contactMutation.mutate(values)}
        isSaving={contactMutation.isPending}
        apiError={contactMutation.error instanceof SupplierApiError ? contactMutation.error.message : undefined}
      />
    </div>
  )
}
