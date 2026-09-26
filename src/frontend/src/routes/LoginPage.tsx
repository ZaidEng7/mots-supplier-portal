// The front door.
//
// THE FAILURE CODE is §7's machine-stable one. This read body.error and could never match:
// ProblemDetailsMiddleware conforms every error response and turns the handler's `error` token into `code` in
// SCREAMING_SNAKE, so the login challenge arrives as { status: 401, code: "MFA_REQUIRED" } with no `error` key at all.
// mfa_required was therefore never detected and an MFA account was shown "Invalid email or password" instead of the code
// step - which locked every system_admin, all of which require MFA, out of the SPA. mfa_invalid and email_not_verified
// failed the same way. Found by writing this page's first component test (B-1). It is normalised to the lower-case token
// the rest of this file already branches on, and the raw `error` key is still read first for any endpoint that has not
// been through the middleware.
//
// THE MFA STEP's pending credentials are set only when the API answers 401 mfa_required, per AuthEndpoints.cs's /login.
// They hold the already-verified password so the TOTP step can re-submit the same credentials plus the code, which is
// what LoginHandler expects on the second call.
//
// WHERE A PERSONA LANDS, written out rather than nested, because the three cases are three different shells and a reader
// should not have to unpick precedence to see which one a persona lands in.
//
// No supplierId means a staff or back-office user - the same signal backOfficeLayoutRoute's own guard uses in router.tsx -
// so they are routed into the back-office shell rather than the supplier dashboard, which has no staff guard of its own
// to catch this otherwise.
//
// An evaluator gets THEIR dashboard, not the shared placeholder. Found by signing in as evaluator@mots.local: they landed
// on /back-office/dashboard, which lists their permissions and says "a summary will appear here later", and nothing in
// the nav linked to /evaluation. The evaluation dashboard and their assignment list both existed, reachable only by
// typing the address, and an evaluator whose whole job is on one screen must not have to be told where it is. It is keyed
// on the PERMISSION rather than the role name, because the token carries permissions and a second source for "is this an
// evaluator" would disagree the day a role's grants change.
//
// That question first read permissions.includes('evaluation.score'), and a system_admin holds all 104 permissions
// including that one - so the administrator was sent to the evaluator's dashboard on every sign-in. The claim set carries
// no role, so the question has to be asked of the permissions: an evaluator scores and nothing else. rfq.read is the
// discriminator, because every back-office persona that is more than an evaluator holds it and the evaluator does not -
// their whole grant is evaluation.score, evaluation.submit and rfq.clarify.
//
// WHAT A FAILURE SAYS is read through the error code for the same reason as the challenge: ApiError.message falls back to
// "Request failed: 400" when the body carries `code` rather than `error`, so matching on the message never fired.
//
// 429 is NOT a credential failure, and calling it one is worse than unhelpful. NFR-SEC-009 limits auth attempts and the
// limiter answers before Identity is ever consulted, so the password was never checked, the account's failure count does
// not move, and the user is told the one thing that is definitely untrue. What they do next is reset a password that was
// always correct, on a reset endpoint that is rate limited too. Reproduced by hitting /login ten times: nine 401s, then
// 429s, all of them displayed as "Invalid email or password".
//
// Anything else is the service rather than the person - a 500 shown as a rejected password sends someone to change a
// credential in response to an outage - and a request that never reached the server at all has no response to have an
// opinion about the credentials.

import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { AuthHeading, Button, Field, Input } from '../components/ui'
import { ApiError, login } from '../api/auth'
import { loginDestination } from './loginDestination'
import { useAuthStore } from '../lib/authStore'

const schema = z.object({
  email: z.email(),
  password: z.string().min(1),
})

type FormValues = z.infer<typeof schema>

function errorCode(err: unknown): string | undefined {
  if (!(err instanceof ApiError)) return undefined
  const body = err.body as { error?: string; code?: string } | null
  return body?.error ?? body?.code?.toLowerCase()
}

export function LoginPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as { redirect?: string }
  const setSession = useAuthStore((s) => s.setSession)
  const [formError, setFormError] = useState<string | null>(null)
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
    const claims = useAuthStore.getState().claims

    await navigate({ to: loginDestination(claims, search.redirect) })
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
        else if (err.status === 400 && errorCode(err) === 'email_not_verified') setFormError(t('auth.emailNotVerified'))
        else if (err.status === 429) setFormError(t('auth.tooManyAttempts'))
        else if (err.status >= 500) setFormError(t('auth.serviceUnavailable'))
        else setFormError(t('auth.loginFailed'))
      } else {
        setFormError(t('auth.serviceUnavailable'))
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
    <main id="main" className="flex min-h-screen items-center justify-center px-4" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <div
        className="w-full max-w-sm rounded-[var(--radius-lg)] p-8"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}
      >
        {pendingCreds ? (
          <>
            <AuthHeading title={t('auth.mfaTitle')} />
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
            <AuthHeading title={t('auth.loginTitle')} />
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
    </main>
  )
}
