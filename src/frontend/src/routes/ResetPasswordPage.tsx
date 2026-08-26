import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { Link, useSearch } from '@tanstack/react-router'
import { Button, Field, Input } from '../components/ui'
import { resetPassword } from '../api/auth'

const schema = z.object({
  newPassword: z.string().min(10),
})
type FormValues = z.infer<typeof schema>

export function ResetPasswordPage() {
  const { t } = useTranslation()
  const search = useSearch({ strict: false }) as { userId?: string; token?: string }
  const [status, setStatus] = useState<'idle' | 'success' | 'invalid'>('idle')

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  const onSubmit = async (values: FormValues) => {
    if (!search.userId || !search.token) {
      setStatus('invalid')
      return
    }
    try {
      await resetPassword(search.userId, search.token, values.newPassword)
      setStatus('success')
    } catch {
      setStatus('invalid')
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <div
        className="w-full max-w-sm rounded-[0.75rem] p-8 shadow-sm"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
      >
        <h1 className="mb-6 text-[length:var(--text-h3)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('auth.resetTitle')}
        </h1>
        {status === 'success' ? (
          <div className="flex flex-col gap-4">
            <p role="status" style={{ color: 'var(--success-600)' }}>
              {t('auth.resetSuccess')}
            </p>
            <Link to="/login" style={{ color: 'var(--color-text-link)' }}>
              {t('auth.submit')}
            </Link>
          </div>
        ) : (
          <form className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)} noValidate>
            <Field label={t('auth.newPassword')} error={errors.newPassword?.message} required>
              {(inputProps) => <Input type="password" autoComplete="new-password" {...inputProps} {...register('newPassword')} />}
            </Field>
            {status === 'invalid' ? (
              <p role="alert" style={{ color: 'var(--danger-500)' }}>
                {t('auth.resetInvalid')}
              </p>
            ) : null}
            <Button type="submit" isLoading={isSubmitting} className="mt-2 w-full">
              {t('auth.resetSubmit')}
            </Button>
          </form>
        )}
      </div>
    </div>
  )
}
