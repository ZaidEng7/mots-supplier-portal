import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearch } from '@tanstack/react-router'
import { Button, Field, Input } from '../components/ui'
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
    fetch(`${API_BASE_URL}/api/v1/registrations/verify`, {
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

  return (
    <div className="flex min-h-screen items-center justify-center px-4" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <div
        className="w-full max-w-sm rounded-[0.75rem] p-8 text-center shadow-sm"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
      >
        {status === 'pending' ? (
          <p role="status" style={{ color: 'var(--color-text-secondary)' }}>
            {t('auth.verifyingEmail')}
          </p>
        ) : status === 'success' ? (
          <div className="flex flex-col gap-4">
            <p role="status" style={{ color: 'var(--success-600)' }}>
              {t('auth.verifySuccess')}
            </p>
            <Link to="/login" style={{ color: 'var(--color-text-link)' }}>
              {t('auth.submit')}
            </Link>
          </div>
        ) : (
          <div className="flex flex-col gap-4 text-start">
            <p role="alert" className="text-center" style={{ color: 'var(--color-danger-fg)' }}>
              {t('auth.verifyFailed')}
            </p>
            {resendStatus === 'sent' ? (
              <p role="status" style={{ color: 'var(--success-600)' }}>
                {t('auth.resendSent')}
              </p>
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
        )}
      </div>
    </div>
  )
}
