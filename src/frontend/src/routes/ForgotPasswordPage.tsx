import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { AuthHeading, Button, Field, Input } from '../components/ui'
import { forgotPassword } from '../api/auth'

const schema = z.object({ email: z.email() })
type FormValues = z.infer<typeof schema>

/**
 * T-084. The page had no failure path at all: `await forgotPassword(...)` then `setSent(true)`, so a
 * network failure or a 500 rejected the promise, the `sent` panel never appeared, and the form sat there
 * looking as though the click had not registered. Someone locked out of their account would click again.
 *
 * <p>The success message stays deliberately non-committal - "if that account exists" - because saying
 * whether an address is registered is user enumeration. The new error is about the REQUEST, not the
 * account, so it does not weaken that.</p>
 */
export function ForgotPasswordPage() {
  const { t } = useTranslation()
  const [sent, setSent] = useState(false)
  const [failed, setFailed] = useState(false)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  const onSubmit = async (values: FormValues) => {
    setFailed(false)
    try {
      await forgotPassword(values.email)
      setSent(true)
    } catch {
      // Any failure, one message. A 500 and a dead connection are the same thing to someone who wants
      // their password back, and distinguishing them here would tell an attacker which addresses make the
      // server work harder.
      setFailed(true)
    }
  }

  return (
    <main id="main" className="flex min-h-screen items-center justify-center px-4" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <div
        className="w-full max-w-sm rounded-[var(--radius-lg)] p-8"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}
      >
        <AuthHeading title={t('auth.forgotTitle')} />
        {sent ? (
          <p role="status" style={{ color: 'var(--color-text-secondary)' }}>
            {t('auth.forgotSent')}
          </p>
        ) : (
          <form className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)} noValidate>
            {failed ? (
              <p role="alert" style={{ color: 'var(--color-danger-fg)' }}>{t('auth.forgotFailed')}</p>
            ) : null}
            <Field label={t('auth.email')} error={errors.email?.message} required>
              {(inputProps) => <Input type="email" autoComplete="email" {...inputProps} {...register('email')} />}
            </Field>
            <Button type="submit" isLoading={isSubmitting} className="mt-2 w-full">
              {t('auth.forgotSubmit')}
            </Button>
          </form>
        )}
      </div>
    </main>
  )
}
