import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chooseLanguage, getAccount } from '../api/auth'
import { useAuthStore } from '../lib/authStore'
import { Button } from './ui'

/**
 * SCR-010 — the one-time language choice.
 *
 * <p>An overlay, not a route. `/onboarding/locale` as a destination would mean intercepting every
 * post-login redirect and then handing the user back to wherever they were going, which is a second
 * routing rule competing with the one SCR-040 and `ensureAuthenticated` already implement. This asks
 * its question over whatever the user landed on and gets out of the way.</p>
 *
 * <p><b>What it does not ask.</b> The inventory row lists numerals and a currency display preference
 * alongside the language. Numerals are derived from the locale by decision (lib/datetime.ts), so
 * offering a control here would create a second source of truth for the same formatting. A display
 * currency is worse than undecided: FEAT-12.3's own rate source is marked [ASSUMPTION], so honouring
 * such a preference would require converting money at a rate nobody has specified. Both are stated on
 * the screen; neither is invented.</p>
 */
export function FirstRunLocale() {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const isAuthenticated = useAuthStore((state) => state.status) === 'authenticated'

  // Only asked of a signed-in user, and only once: `languageChosen` is the server's answer, so the
  // question does not come back on another device.
  const accountQuery = useQuery({ queryKey: ['account'], queryFn: getAccount, enabled: isAuthenticated })

  const chooseMutation = useMutation({
    mutationFn: (language: string) => chooseLanguage(language),
    onSuccess: async (account) => {
      if (i18n.language !== account.language) await i18n.changeLanguage(account.language)
      // Awaited, not fired: the overlay unmounts on this data, and invalidating without waiting leaves
      // it on screen for a frame after the choice has been made.
      await queryClient.invalidateQueries({ queryKey: ['account'] })
    },
  })

  if (!isAuthenticated || !accountQuery.data || accountQuery.data.languageChosen) return null

  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center p-4" style={{ backgroundColor: 'rgba(0,0,0,0.55)' }}>
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="first-run-locale-title"
        className="w-full max-w-[26rem] rounded-[0.75rem] p-6"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
      >
        {/* Both languages at once, and this is the one screen in the app where that is right: the
            reader has not told us which one they read yet, so showing the question in only one of them
            is a coin toss. */}
        <h2 id="first-run-locale-title" className="mb-1 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          اختر لغة الواجهة
        </h2>
        <h2 className="mb-4 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          Choose your language
        </h2>

        <div className="flex gap-2">
          <Button isLoading={chooseMutation.isPending} onClick={() => chooseMutation.mutate('ar')}>العربية</Button>
          <Button isLoading={chooseMutation.isPending} onClick={() => chooseMutation.mutate('en')}>English</Button>
        </div>

        <p className="mt-4 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('firstRunLocale.derived')}
        </p>
        {chooseMutation.isError ? (
          <p role="alert" className="mt-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-danger-fg)' }}>
            {t('firstRunLocale.failed')}
          </p>
        ) : null}
      </div>
    </div>
  )
}
