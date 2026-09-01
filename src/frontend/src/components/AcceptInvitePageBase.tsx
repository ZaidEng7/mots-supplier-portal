import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Link, useSearch } from '@tanstack/react-router'
import { Button, Field, Input } from './ui'

const schema = z.object({ password: z.string().min(12) })
type FormValues = z.infer<typeof schema>

/** Shared by AcceptStaffInvitePage, AcceptTeamInvitePage, and ResetPasswordPage - all three are
 * the same screen (single new-password field, gated by an opaque token read from the URL, with
 * success/invalid states), differing only in copy and which endpoint consumes the token. */
export function AcceptInvitePageBase({
  onSubmitToken,
  title,
  hint,
  successMessage,
  invalidMessage,
  submitLabel,
  passwordFieldLabel,
  mapPasswordError,
  loginLinkLabel,
}: {
  onSubmitToken: (token: string, password: string) => Promise<unknown>
  title: string
  hint?: string
  successMessage: string
  invalidMessage: string
  submitLabel: string
  passwordFieldLabel: string
  mapPasswordError: (rawMessage: string | undefined) => string | undefined
  loginLinkLabel: string
}) {
  const search = useSearch({ strict: false }) as { token?: string }
  const [status, setStatus] = useState<'idle' | 'success' | 'invalid'>('idle')

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  const onSubmit = async (values: FormValues) => {
    if (!search.token) {
      setStatus('invalid')
      return
    }
    try {
      await onSubmitToken(search.token, values.password)
      setStatus('success')
    } catch {
      setStatus('invalid')
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <div className="w-full max-w-sm rounded-[0.75rem] p-8 shadow-sm" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
        <h1 className="mb-6 text-[length:var(--text-h3)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {title}
        </h1>
        {status === 'success' ? (
          <div className="flex flex-col gap-4">
            <p role="status" style={{ color: 'var(--success-600)' }}>
              {successMessage}
            </p>
            <Link to="/login" style={{ color: 'var(--color-text-link)' }}>
              {loginLinkLabel}
            </Link>
          </div>
        ) : (
          <form className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)} noValidate>
            {hint ? (
              <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {hint}
              </p>
            ) : null}
            <Field label={passwordFieldLabel} error={mapPasswordError(errors.password?.message)} required>
              {(p) => <Input type="password" autoComplete="new-password" {...p} {...register('password')} />}
            </Field>
            {status === 'invalid' ? (
              <p role="alert" style={{ color: 'var(--color-danger-fg)' }}>
                {invalidMessage}
              </p>
            ) : null}
            <Button type="submit" isLoading={isSubmitting} className="mt-2 w-full">
              {submitLabel}
            </Button>
          </form>
        )}
      </div>
    </div>
  )
}
