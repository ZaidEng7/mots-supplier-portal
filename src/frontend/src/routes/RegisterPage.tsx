import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { Link } from '@tanstack/react-router'
import { Button, Field, Input } from '../components/ui'
import { ApiError, registerSupplier } from '../api/auth'

const schema = z
  .object({
    displayNameAr: z.string().min(1),
    displayNameEn: z.string().min(1),
    registrationNumber: z.string().optional(),
    representativeName: z.string().min(1),
    representativePhone: z.string().min(1),
    email: z.string().email(),
    password: z.string().min(10),
    confirmPassword: z.string().min(1),
  })
  .refine((data) => data.password === data.confirmPassword, {
    path: ['confirmPassword'],
    message: 'passwords_must_match',
  })

type FormValues = z.infer<typeof schema>

export function RegisterPage() {
  const { t } = useTranslation()
  const [referenceCode, setReferenceCode] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  const onSubmit = async (values: FormValues) => {
    setFormError(null)
    try {
      const result = await registerSupplier({
        displayNameAr: values.displayNameAr,
        displayNameEn: values.displayNameEn,
        registrationNumber: values.registrationNumber || undefined,
        representativeName: values.representativeName,
        representativePhone: values.representativePhone,
        email: values.email,
        password: values.password,
      })
      setReferenceCode(result.referenceCode)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setFormError(t('register.duplicateEmail'))
      } else if (err instanceof ApiError && err.status === 400) {
        setFormError(t('register.weakPassword'))
      } else {
        setFormError(t('register.failed'))
      }
    }
  }

  if (referenceCode) {
    return (
      <div className="flex min-h-screen items-center justify-center px-4" style={{ backgroundColor: 'var(--color-bg-app)' }}>
        <div
          className="w-full max-w-sm rounded-[0.75rem] p-8 text-center shadow-sm"
          style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
        >
          <h1 className="mb-3 text-[length:var(--text-h3)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('register.successTitle')}
          </h1>
          <p className="mb-2" style={{ color: 'var(--color-text-secondary)' }}>
            {t('register.checkEmail')}
          </p>
          <p className="num mb-4 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-muted)' }}>
            {referenceCode}
          </p>
          <Link to="/login" style={{ color: 'var(--color-text-link)' }}>
            {t('auth.submit')}
          </Link>
        </div>
      </div>
    )
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4 py-8" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <div
        className="w-full max-w-lg rounded-[0.75rem] p-8 shadow-sm"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
      >
        <h1 className="mb-6 text-[length:var(--text-h3)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('register.title')}
        </h1>
        <form className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)} noValidate>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label={t('register.displayNameAr')} error={errors.displayNameAr?.message} required>
              {(p) => <Input {...p} {...register('displayNameAr')} />}
            </Field>
            <Field label={t('register.displayNameEn')} error={errors.displayNameEn?.message} required>
              {(p) => <Input {...p} {...register('displayNameEn')} />}
            </Field>
          </div>
          <Field label={t('register.registrationNumber')} hint={t('register.registrationNumberHint')}>
            {(p) => <Input {...p} {...register('registrationNumber')} />}
          </Field>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label={t('register.representativeName')} error={errors.representativeName?.message} required>
              {(p) => <Input {...p} {...register('representativeName')} />}
            </Field>
            <Field label={t('register.representativePhone')} error={errors.representativePhone?.message} required>
              {(p) => <Input type="tel" {...p} {...register('representativePhone')} />}
            </Field>
          </div>
          <Field label={t('auth.email')} error={errors.email?.message} required>
            {(p) => <Input type="email" autoComplete="email" {...p} {...register('email')} />}
          </Field>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label={t('auth.password')} error={errors.password?.message} required>
              {(p) => <Input type="password" autoComplete="new-password" {...p} {...register('password')} />}
            </Field>
            <Field label={t('register.confirmPassword')} error={errors.confirmPassword?.message} required>
              {(p) => <Input type="password" autoComplete="new-password" {...p} {...register('confirmPassword')} />}
            </Field>
          </div>
          {formError ? (
            <p role="alert" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--danger-500)' }}>
              {formError}
            </p>
          ) : null}
          <Button type="submit" isLoading={isSubmitting} className="mt-2 w-full">
            {t('register.submit')}
          </Button>
        </form>
        <Link to="/login" className="mt-4 block text-center text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-link)' }}>
          {t('register.haveAccount')}
        </Link>
      </div>
    </div>
  )
}
