import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearch } from '@tanstack/react-router'
import { AuthHeading, Button, Field, Input } from '../components/ui'
import { resendVerification } from '../api/auth'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

export function VerifyEmailPage() {
  const { t } = useTranslation()
  const search = useSearch({ strict: false }) as { token?: string }
  const [status, setStatus] = useState<'pending' | 'success' | 'failed'>('pending')
  const [resendEmail, setResendEmail] = useState('')
  const [resendStatus, setResendStatus] = useState<'idle' | 'sending' | 'sent'>('idle')

  useEffect(() => {
    if (!search.token) {
      setStatus('failed')
      return
    }
    fetch(`${API_BASE_URL}/api/v1/auth/verify-email`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token: search.token }),
    })
      .then((res) => setStatus(res.ok ? 'success' : 'failed'))
      .catch(() => setStatus('failed'))
  }, [search.token])

  const handleResend = async () => {
    if (!resendEmail) return
    setResendStatus('sending')
    try {
      await resendVerification(resendEmail)
    } finally {
      setResendStatus('sent')
    }
  }

  // Three outcomes of one request, rendered as three independent branches rather than a chain: each is
  // a different thing to say, not a refinement of the one before.
  const isVerified = status === 'success'

  return (
    <main id="main" className="flex min-h-screen items-center justify-center px-4" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <div
        className="w-full max-w-sm rounded-[var(--radius-lg)] p-8 text-center"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}
      >
        {/* The screen had no title in any of its three states, so a reader who followed a link from an
            email and hit the failure branch was shown an error with nothing saying what had failed.
            AuthHeading rather than PageHeading: this card IS the viewport. */}
        <AuthHeading title={t('auth.verifyEmailTitle')} />
        {status === 'pending' ? (
          <output className="block" style={{ color: 'var(--color-text-secondary)' }}>
            {t('auth.verifyingEmail')}
          </output>
        ) : null}
        {isVerified ? (
          <div className="flex flex-col gap-4">
            <output className="block" style={{ color: 'var(--color-success-fg)' }}>
              {t('auth.verifySuccess')}
            </output>
            <Link to="/login" style={{ color: 'var(--color-text-link)' }}>
              {t('auth.submit')}
            </Link>
          </div>
        ) : null}
        {status === 'failed' ? (
          <div className="flex flex-col gap-4 text-start">
            <p role="alert" className="text-center" style={{ color: 'var(--color-danger-fg)' }}>
              {t('auth.verifyFailed')}
            </p>
            {resendStatus === 'sent' ? (
              <output className="block" style={{ color: 'var(--color-success-fg)' }}>
                {t('auth.resendSent')}
              </output>
            ) : (
              <>
                <Field label={t('auth.email')}>
                  {(p) => <Input type="email" {...p} value={resendEmail} onChange={(e) => setResendEmail(e.target.value)} />}
                </Field>
                <Button isLoading={resendStatus === 'sending'} disabled={!resendEmail} onClick={handleResend}>
                  {t('auth.resendVerification')}
                </Button>
              </>
            )}
          </div>
        ) : null}
      </div>
    </main>
  )
}
