import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Field, Input, Select } from '../components/ui'
import { useToast } from '../components/ui'
import { getOwnSupplier, updateProfile, submitApplication, SupplierApiError, type SupplierProfile } from '../api/supplier'
import { fetchCurrencies } from '../api/reference'

const schema = z.object({
  registrationNumber: z.string().optional(),
  taxId: z.string().optional(),
  addressLine: z.string().optional(),
  city: z.string().optional(),
  country: z.string().optional(),
  currencyCode: z.string().optional(),
  primaryContactPhone: z.string().optional(),
})

type FormValues = z.infer<typeof schema>

const REQUIRED_FIELDS = ['registrationNumber', 'taxId', 'addressLine', 'city', 'country', 'currencyCode', 'primaryContactPhone'] as const

export function OnboardingPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { notify } = useToast()

  const profileQuery = useQuery({ queryKey: ['own-supplier'], queryFn: getOwnSupplier })
  const currenciesQuery = useQuery({ queryKey: ['currencies'], queryFn: fetchCurrencies })

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    watch,
    formState: { isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })
  const currencyCode = watch('currencyCode')

  useEffect(() => {
    if (profileQuery.data) {
      reset({
        registrationNumber: profileQuery.data.registrationNumber ?? '',
        taxId: profileQuery.data.taxId ?? '',
        addressLine: profileQuery.data.addressLine ?? '',
        city: profileQuery.data.city ?? '',
        country: profileQuery.data.country ?? '',
        currencyCode: profileQuery.data.currencyCode ?? '',
        primaryContactPhone: profileQuery.data.primaryContactPhone ?? '',
      })
    }
  }, [profileQuery.data, reset])

  const saveMutation = useMutation({
    mutationFn: (values: FormValues) => updateProfile(values),
    onSuccess: (data) => {
      queryClient.setQueryData(['own-supplier'], data)
      notify({ kind: 'success', title: t('onboarding.saved') })
    },
    onError: () => notify({ kind: 'danger', title: t('onboarding.saveFailed') }),
  })

  const submitMutation = useMutation({
    mutationFn: submitApplication,
    onSuccess: (data) => {
      queryClient.setQueryData(['own-supplier'], data)
      notify({ kind: 'success', title: t('onboarding.submitted') })
    },
    onError: (err) => {
      if (err instanceof SupplierApiError && err.missingFields) {
        notify({ kind: 'danger', title: t('onboarding.incomplete'), description: err.missingFields.join(', ') })
      } else {
        notify({ kind: 'danger', title: t('onboarding.submitFailed') })
      }
    },
  })

  if (profileQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>...</p>
  }

  const profile = profileQuery.data as SupplierProfile | undefined
  const missing = new Set(profile?.missingProfileFields ?? [])
  const isReadOnly = profile ? profile.onboardingState !== 'EmailVerified' && profile.onboardingState !== 'ProfileInProgress' : false
  const currencyOptions = (currenciesQuery.data ?? []).map((c) => ({ value: c.code, label: `${c.code}` }))

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('onboarding.title')}
        </h1>
        {profile ? <Badge tone={profile.onboardingState === 'Submitted' ? 'success' : 'brand'}>{profile.onboardingState}</Badge> : null}
      </div>

      <div
        className="rounded-[0.75rem] p-6"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
      >
        <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('onboarding.checklist')}
        </h2>
        <ul className="flex flex-col gap-1.5">
          {REQUIRED_FIELDS.map((field) => (
            <li key={field} className="flex items-center gap-2 text-[length:var(--text-body-sm)]">
              <Badge tone={missing.has(field) ? 'warning' : 'success'}>
                {missing.has(field) ? t('onboarding.missing') : t('onboarding.complete')}
              </Badge>
              <span style={{ color: 'var(--color-text-secondary)' }}>{t(`onboarding.fields.${field}`)}</span>
            </li>
          ))}
        </ul>
      </div>

      <form
        className="flex flex-col gap-4 rounded-[0.75rem] p-6"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
        onSubmit={handleSubmit((values) => saveMutation.mutate(values))}
      >
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label={t('onboarding.fields.registrationNumber')}>
            {(p) => <Input disabled={isReadOnly} {...p} {...register('registrationNumber')} />}
          </Field>
          <Field label={t('onboarding.fields.taxId')}>{(p) => <Input disabled={isReadOnly} {...p} {...register('taxId')} />}</Field>
          <Field label={t('onboarding.fields.addressLine')}>
            {(p) => <Input disabled={isReadOnly} {...p} {...register('addressLine')} />}
          </Field>
          <Field label={t('onboarding.fields.city')}>{(p) => <Input disabled={isReadOnly} {...p} {...register('city')} />}</Field>
          <Field label={t('onboarding.fields.country')}>{(p) => <Input disabled={isReadOnly} {...p} {...register('country')} />}</Field>
          <Field label={t('onboarding.fields.currencyCode')}>
            {(p) => (
              <Select
                id={p.id}
                aria-describedby={p['aria-describedby']}
                aria-invalid={p['aria-invalid']}
                value={currencyCode || undefined}
                onValueChange={(v) => setValue('currencyCode', v)}
                options={currencyOptions}
                placeholder={t('onboarding.fields.currencyCode')}
              />
            )}
          </Field>
          <Field label={t('onboarding.fields.primaryContactPhone')}>
            {(p) => <Input disabled={isReadOnly} {...p} {...register('primaryContactPhone')} />}
          </Field>
        </div>
        {!isReadOnly ? (
          <div className="flex gap-3">
            <Button type="submit" variant="secondary" isLoading={isSubmitting || saveMutation.isPending}>
              {t('onboarding.save')}
            </Button>
            <Button
              type="button"
              isLoading={submitMutation.isPending}
              disabled={missing.size > 0}
              onClick={() => submitMutation.mutate()}
            >
              {t('onboarding.submit')}
            </Button>
          </div>
        ) : (
          <p role="status" style={{ color: 'var(--success-600)' }}>
            {t('onboarding.readOnlyNotice')}
          </p>
        )}
      </form>
    </div>
  )
}
