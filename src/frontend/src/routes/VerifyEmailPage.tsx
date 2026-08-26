import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearch } from '@tanstack/react-router'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

export function VerifyEmailPage() {
  const { t } = useTranslation()
  const search = useSearch({ strict: false }) as { userId?: string; token?: string }
  const [status, setStatus] = useState<'pending' | 'success' | 'failed'>('pending')

  useEffect(() => {
    if (!search.userId || !search.token) {
      setStatus('failed')
      return
    }
    fetch(`${API_BASE_URL}/api/v1/registrations/verify`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userId: search.userId, token: search.token }),
    })
      .then((res) => setStatus(res.ok ? 'success' : 'failed'))
      .catch(() => setStatus('failed'))
  }, [search.userId, search.token])

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
          <p role="alert" style={{ color: 'var(--danger-500)' }}>
            {t('auth.verifyFailed')}
          </p>
        )}
      </div>
    </div>
  )
}
