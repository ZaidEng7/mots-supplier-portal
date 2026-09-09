import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { Link } from '@tanstack/react-router'
import { Button, Field, Input, PhoneInput } from '../components/ui'
import { ApiError, registerSupplier } from '../api/auth'
import { useQuery } from '@tanstack/react-query'
import { getPublicSettings } from '../api/systemSettings'

/**
 * Messages are i18n KEYS, translated where they are rendered.
 *
 * Every rule here carried Zod's default before batch 12, so the first screen a supplier ever sees told
 * them "Too small: expected string to have >=1 characters" - library internals, in English, on an
 * Arabic-first product. The mismatch rule was worse: it already used a key, `passwords_must_match`,
 * and nothing translated it, so the literal token was printed under the field.
 *
 * The schema is module-scope and `t` is a hook, so the key travels in `message` and the translation
 * happens at the field. Keeping the schema out of the component also keeps it out of every render.
 */
const schema = z
  .object({
    displayNameAr: z.string().min(1, 'register.errors.required'),
    displayNameEn: z.string().min(1, 'register.errors.required'),
    registrationNumber: z.string().optional(),
    representativeName: z.string().min(1, 'register.errors.required'),
    representativePhone: z.string().min(1, 'register.errors.required'),
    email: z.email('register.errors.email'),
    password: z.string().min(12, 'register.errors.passwordLength'),
    confirmPassword: z.string().min(1, 'register.errors.required'),
  })
  .refine((data) => data.password === data.confirmPassword, {
    path: ['confirmPassword'],
    message: 'register.errors.passwordsMatch',
  })

type FormValues = z.infer<typeof schema>

export function RegisterPage() {
  const { t } = useTranslation()
  // MSP-73: submission success and referenceCode are tracked separately on purpose. A duplicate
  // email/registration number now returns the same 200 with referenceCode: null (see
  // api/auth.ts) - the confirmation screen must still show for that case exactly as it does for
  // a genuine new registration, or the response shape change would silently break the one thing
  // it exists to protect: a legitimate user re-registering by mistake gets no different an
  // experience than a first-time one.
  const [submitted, setSubmitted] = useState(false)
  const [referenceCode, setReferenceCode] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  // FR-REG-002/T-060. A closed portal must not render a form that cannot be submitted. The server
  // refuses it either way - this is the message, not the control.
  //
  // A FAILED read renders the form: the setting defaults to open, and a settings endpoint that is
  // briefly unavailable must not look like a closed ministry.
  const settingsQuery = useQuery({ queryKey: ['public-settings'], queryFn: getPublicSettings })
  const registrationClosed = settingsQuery.data?.['registration.mode'] === 'closed'

  const {
    register,
    handleSubmit,
    watch,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { representativePhone: '' } })
  const representativePhone = watch('representativePhone')

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
      setReferenceCode(result.supplierCode)
      setSubmitted(true)
    } catch (err) {
      if (err instanceof ApiError && err.status === 400) {
        setFormError(t('register.weakPassword'))
      } else if (err instanceof ApiError && err.status === 403) {
        // Closed between loading this page and submitting it. Says so, rather than reporting a
        // generic failure the applicant would retry.
        setFormError(t('register.closedBody'))
      } else {
        setFormError(t('register.failed'))
      }
    }
  }

  if (registrationClosed) {
    return (
      <main id="main" className="flex min-h-screen items-center justify-center px-4" style={{ backgroundColor: 'var(--color-bg-app)' }}>
        <div
          className="w-full max-w-sm rounded-[var(--radius-lg)] p-8 text-center"
          style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}
        >
          <h1 className="mb-3 text-[length:var(--text-h3)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('register.closedTitle')}
          </h1>
          <p className="mb-4" style={{ color: 'var(--color-text-secondary)' }}>
            {t('register.closedBody')}
          </p>
          <Link to="/login" style={{ color: 'var(--color-text-link)' }}>
            {t('auth.submit')}
          </Link>
        </div>
      </main>
    )
  }

  if (submitted) {
    return (
      <main id="main" className="flex min-h-screen items-center justify-center px-4" style={{ backgroundColor: 'var(--color-bg-app)' }}>
        <div
          className="w-full max-w-sm rounded-[var(--radius-lg)] p-8 text-center"
          style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}
        >
          <h1 className="mb-3 text-[length:var(--text-h3)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('register.successTitle')}
          </h1>
          <p className="mb-2" style={{ color: 'var(--color-text-secondary)' }}>
            {t('register.checkEmail')}
          </p>
          {referenceCode ? (
            <p className="num mb-4 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-muted)' }}>
              {referenceCode}
            </p>
          ) : null}
          <Link to="/login" style={{ color: 'var(--color-text-link)' }}>
            {t('auth.submit')}
          </Link>
        </div>
      </main>
    )
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4 py-8" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <div
        className="w-full max-w-lg rounded-[var(--radius-lg)] p-8"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}
      >
        <h1 className="mb-6 text-[length:var(--text-h3)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('register.title')}
        </h1>
        <form className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)} noValidate>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label={t('register.displayNameAr')} error={errors.displayNameAr?.message ? t(errors.displayNameAr.message) : undefined} required>
              {(p) => <Input {...p} {...register('displayNameAr')} />}
            </Field>
            <Field label={t('register.displayNameEn')} error={errors.displayNameEn?.message ? t(errors.displayNameEn.message) : undefined} required>
              {(p) => <Input {...p} {...register('displayNameEn')} />}
            </Field>
          </div>
          <Field label={t('register.registrationNumber')} hint={t('register.registrationNumberHint')}>
            {(p) => <Input {...p} {...register('registrationNumber')} />}
          </Field>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label={t('register.representativeName')} error={errors.representativeName?.message ? t(errors.representativeName.message) : undefined} required>
              {(p) => <Input {...p} {...register('representativeName')} />}
            </Field>
            <Field label={t('register.representativePhone')} error={errors.representativePhone?.message ? t(errors.representativePhone.message) : undefined} required>
              {(p) => (
                <PhoneInput
                  {...p}
                  value={representativePhone ?? ''}
                  onChange={(v) => setValue('representativePhone', v, { shouldValidate: true })}
                />
              )}
            </Field>
          </div>
          <Field label={t('auth.email')} error={errors.email?.message ? t(errors.email.message) : undefined} required>
            {(p) => <Input type="email" autoComplete="email" {...p} {...register('email')} />}
          </Field>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label={t('auth.password')} error={errors.password?.message ? t(errors.password.message) : undefined} required>
              {(p) => <Input type="password" autoComplete="new-password" {...p} {...register('password')} />}
            </Field>
            <Field label={t('register.confirmPassword')} error={errors.confirmPassword?.message ? t(errors.confirmPassword.message) : undefined} required>
              {(p) => <Input type="password" autoComplete="new-password" {...p} {...register('confirmPassword')} />}
            </Field>
          </div>
          {formError ? (
            <p role="alert" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-danger-fg)' }}>
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
