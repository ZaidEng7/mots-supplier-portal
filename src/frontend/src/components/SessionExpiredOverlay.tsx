import { useState } from 'react'
import * as RadixDialog from '@radix-ui/react-dialog'
import { useTranslation } from 'react-i18next'
import { login } from '../api/auth'
import { useAuthStore } from '../lib/authStore'
import { Button, Field, Input } from './ui'

/**
 * SCR-040 — re-authenticate in place when a session expires under the user.
 *
 * <p>The destination-preserving redirect already worked: navigating to an authenticated route without
 * a session sends you to `/login?redirect=...` and back afterwards. What did not work is the case that
 * actually happens — the refresh cookie lapsing while someone is part way through a form. Every
 * request started failing, and the next navigation threw them at the login screen along with whatever
 * they had typed.</p>
 *
 * <p>So this signs them back in WITHOUT navigating: the route stays mounted, its component state
 * survives, and the work is still there when the overlay closes. It is deliberately not a route.</p>
 *
 * <p>Shown only for an expiry, never for a user who has not signed in - see authStore's `expired`.
 * The password is never held anywhere but this component's own state.</p>
 */
export function SessionExpiredOverlay() {
  const { t } = useTranslation()
  const expired = useAuthStore((state) => state.expired)
  const lastEmail = useAuthStore((state) => state.lastEmail)
  const setSession = useAuthStore((state) => state.setSession)
  const clearSession = useAuthStore((state) => state.clearSession)

  const [password, setPassword] = useState('')
  const [totp, setTotp] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (!expired) return null

  const submit = async () => {
    if (!lastEmail) return
    setBusy(true)
    setError(null)
    try {
      const tokens = await login(lastEmail, password, totp || undefined)
      setPassword('')
      setTotp('')
      // setSession clears `expired`, which unmounts this overlay and leaves the page underneath
      // exactly as it was.
      setSession(tokens.accessToken)
    } catch {
      // One message for every failure, and no distinction between a wrong password and a missing
      // second factor: this dialog sits over an authenticated session, and telling an attacker at an
      // unattended screen which half they got wrong is help they have not earned.
      setError(t('sessionExpired.failed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    // Radix, not a hand-rolled overlay: this declared role="dialog" aria-modal="true" and trapped
    // nothing, so Tab walked straight out into the page behind it - a page the reader can no longer
    // save. `modal` also makes that background inert to assistive technology.
    <RadixDialog.Root open modal>
      <RadixDialog.Portal>
        <RadixDialog.Overlay
          className="fixed inset-0"
          // The topmost layer this product defines. Nothing else uses --z-tooltip (there are no
          // tooltips), and an expired session outranks everything a person could be in the middle of -
          // a dialog, a select popover, a toast.
          style={{ zIndex: 'var(--z-tooltip)', backgroundColor: 'var(--color-bg-overlay)' }}
        />
        <RadixDialog.Content
          aria-labelledby="session-expired-title"
          className="fixed left-1/2 top-1/2 w-full max-w-[26rem] -translate-x-1/2 -translate-y-1/2 rounded-[var(--radius-lg)] p-6"
          style={{
            zIndex: 'var(--z-tooltip)',
            backgroundColor: 'var(--color-bg-surface)',
            border: '1px solid var(--color-border)',
            boxShadow: 'var(--shadow-lg)',
          }}
          // There is no way out of this one except re-authenticating: Escape, an outside click and the
          // close affordance are all refused deliberately.
          onEscapeKeyDown={(event) => event.preventDefault()}
          onPointerDownOutside={(event) => event.preventDefault()}
          onInteractOutside={(event) => event.preventDefault()}
        >
        <RadixDialog.Title
          id="session-expired-title"
          className="mb-2 text-[length:var(--text-h4)] font-[var(--fw-semibold)]"
          style={{ color: 'var(--color-text-primary)' }}
        >
          {t('sessionExpired.title')}
        </RadixDialog.Title>
        <p className="mb-4 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('sessionExpired.body')}
        </p>

        {lastEmail ? (
          <p className="mb-3" style={{ color: 'var(--color-text-primary)' }}>{lastEmail}</p>
        ) : null}

        <div className="flex flex-col gap-3">
          <Field label={t('sessionExpired.password')} error={error ?? undefined} required>
            {(p) => (
              <Input
                {...p}
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            )}
          </Field>

          {/* Optional, and present for the same reason the login form has one: a system_admin has MFA
              enrolled, and an overlay that could not take a code would be an overlay they cannot use. */}
          <Field label={t('sessionExpired.totp')}>
            {(p) => (
              <Input {...p} inputMode="numeric" autoComplete="one-time-code" value={totp}
                onChange={(e) => setTotp(e.target.value)} />
            )}
          </Field>

          <div className="flex gap-2">
            <Button isLoading={busy} disabled={!password} onClick={() => void submit()}>
              {t('sessionExpired.signIn')}
            </Button>
            {/* The way out. Someone who cannot remember their password must not be trapped behind a
                modal with no exit - this discards the session properly, and the next navigation takes
                them to the login screen through the redirect that already works. */}
            <Button variant="ghost" onClick={clearSession}>{t('sessionExpired.signOut')}</Button>
          </div>
        </div>
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  )
}
