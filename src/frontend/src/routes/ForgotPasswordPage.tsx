import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { Button, Field, Input } from '../components/ui'
import { forgotPassword } from '../api/auth'

const schema = z.object({ email: z.string().email() })
type FormValues = z.infer<typeof schema>

export function ForgotPasswordPage() {
  const { t } = useTranslation()
  const [sent, setSent] = useState(false)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  const onSubmit = async (values: FormValues) => {
    await forgotPassword(values.email)
    setSent(true)
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <div
        className="w-full max-w-sm rounded-[0.75rem] p-8 shadow-sm"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
      >
        <h1 className="mb-6 text-[length:var(--text-h3)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('auth.forgotTitle')}
        </h1>
        {sent ? (
          <p role="status" style={{ color: 'var(--color-text-secondary)' }}>
            {t('auth.forgotSent')}
          </p>
        ) : (
          <form className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)} noValidate>
            <Field label={t('auth.email')} error={errors.email?.message} required>
              {(inputProps) => <Input type="email" autoComplete="email" {...inputProps} {...register('email')} />}
            </Field>
            <Button type="submit" isLoading={isSubmitting} className="mt-2 w-full">
              {t('auth.forgotSubmit')}
            </Button>
          </form>
        )}
      </div>
    </div>
  )
}
