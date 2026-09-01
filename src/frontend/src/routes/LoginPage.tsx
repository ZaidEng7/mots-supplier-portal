import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { Button, Field, Input } from '../components/ui'
import { ApiError, login } from '../api/auth'
import { useAuthStore } from '../lib/authStore'

const schema = z.object({
  email: z.string().email(),
  password: z.string().min(1),
})

type FormValues = z.infer<typeof schema>

function errorCode(err: unknown): string | undefined {
  if (!(err instanceof ApiError)) return undefined
  const body = err.body as { error?: string } | null
  return body?.error
}

export function LoginPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as { redirect?: string }
  const setSession = useAuthStore((s) => s.setSession)
  const [formError, setFormError] = useState<string | null>(null)
  // Set only when the API answers 401 { error: 'mfa_required' } (Api/Endpoints/AuthEndpoints.cs
  // `/login`) - holds the already-verified password so the TOTP step can re-submit the same
  // credentials plus the code, matching what LoginHandler expects on the second call.
  const [pendingCreds, setPendingCreds] = useState<FormValues | null>(null)
  const [totpCode, setTotpCode] = useState('')
  const [mfaSubmitting, setMfaSubmitting] = useState(false)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  const completeLogin = async (tokens: { accessToken: string }) => {
    setSession(tokens.accessToken)
    // No supplierId means a staff/back-office user (same signal backOfficeLayoutRoute's own
    // guard uses in router.tsx) - route them into the back-office shell instead of the
    // supplier dashboard, which has no staff guard of its own to catch this otherwise.
    const claims = useAuthStore.getState().claims
    const defaultRoute = claims?.supplierId ? '/dashboard' : '/back-office/dashboard'
    await navigate({ to: search.redirect ?? defaultRoute })
  }

  const onSubmit = async (values: FormValues) => {
    setFormError(null)
    try {
      const tokens = await login(values.email, values.password)
      await completeLogin(tokens)
    } catch (err) {
      if (errorCode(err) === 'mfa_required') {
        setPendingCreds(values)
        return
      }
      if (err instanceof ApiError) {
        if (err.status === 423) setFormError(t('auth.lockedOut'))
        else if (err.status === 400 && err.message === 'email_not_verified') setFormError(t('auth.emailNotVerified'))
        else setFormError(t('auth.loginFailed'))
      } else {
        setFormError(t('auth.loginFailed'))
      }
    }
  }

  const onSubmitTotp = async () => {
    if (!pendingCreds) return
    setFormError(null)
    setMfaSubmitting(true)
    try {
      const tokens = await login(pendingCreds.email, pendingCreds.password, totpCode)
      await completeLogin(tokens)
    } catch (err) {
      setFormError(errorCode(err) === 'mfa_invalid' ? t('auth.mfaInvalid') : t('auth.loginFailed'))
    } finally {
      setMfaSubmitting(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <div
        className="w-full max-w-sm rounded-[0.75rem] p-8 shadow-sm"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
      >
        {pendingCreds ? (
          <>
            <h1 className="mb-6 text-[length:var(--text-h3)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
              {t('auth.mfaTitle')}
            </h1>
            <form
              className="flex flex-col gap-4"
              onSubmit={(e) => {
                e.preventDefault()
                void onSubmitTotp()
              }}
              noValidate
            >
              <Field label={t('auth.mfaCodeLabel')} required>
                {(inputProps) => (
                  <Input
                    {...inputProps}
                    type="text"
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    autoFocus
                    value={totpCode}
                    onChange={(e) => setTotpCode(e.target.value)}
                  />
                )}
              </Field>
              {formError ? (
                <p role="alert" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-danger-fg)' }}>
                  {formError}
                </p>
              ) : null}
              <Button type="submit" isLoading={mfaSubmitting} disabled={totpCode.trim().length === 0} className="mt-2 w-full">
                {t('auth.mfaSubmit')}
              </Button>
              <Button
                type="button"
                variant="ghost"
                className="w-full"
                onClick={() => {
                  setPendingCreds(null)
                  setTotpCode('')
                  setFormError(null)
                }}
              >
                {t('auth.mfaBack')}
              </Button>
            </form>
          </>
        ) : (
          <>
            <h1 className="mb-6 text-[length:var(--text-h3)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
              {t('auth.loginTitle')}
            </h1>
            <form className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)} noValidate>
              <Field label={t('auth.email')} error={errors.email?.message} required>
                {(inputProps) => <Input type="email" autoComplete="email" {...inputProps} {...register('email')} />}
              </Field>
              <Field label={t('auth.password')} error={errors.password?.message} required>
                {(inputProps) => <Input type="password" autoComplete="current-password" {...inputProps} {...register('password')} />}
              </Field>
              {formError ? (
                <p role="alert" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-danger-fg)' }}>
                  {formError}
                </p>
              ) : null}
              <Button type="submit" isLoading={isSubmitting} className="mt-2 w-full">
                {t('auth.submit')}
              </Button>
            </form>
            <Link
              to="/forgot-password"
              className="mt-4 block text-center text-[length:var(--text-body-sm)]"
              style={{ color: 'var(--color-text-link)' }}
            >
              {t('auth.forgotPassword')}
            </Link>
            <Link
              to="/register"
              className="mt-2 block text-center text-[length:var(--text-body-sm)]"
              style={{ color: 'var(--color-text-link)' }}
            >
              {t('register.createAccount')}
            </Link>
          </>
        )}
      </div>
    </div>
  )
}
