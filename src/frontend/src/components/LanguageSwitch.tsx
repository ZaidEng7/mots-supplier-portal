import { useTranslation } from 'react-i18next'
import { RTL_LANGUAGES } from '../i18n/config'
import { updateAccount, getAccount } from '../api/auth'
import { useAuthStore } from '../lib/authStore'

export function LanguageSwitch() {
  const { i18n, t } = useTranslation()
  const isAuthenticated = useAuthStore((state) => state.status) === 'authenticated'

  const toggle = () => {
    const next = RTL_LANGUAGES.has(i18n.language) ? 'en' : 'ar'
    void i18n.changeLanguage(next)

    // SCR-902 gave the account a stored language, and a toggle that did not write to it would leave
    // two answers to one question: the header would say English and the account screen Arabic, and a
    // reload would pick whichever it happened to read. So the toggle persists for a signed-in user.
    //
    // Fire-and-forget, and deliberately: the language has ALREADY changed on screen. Blocking the
    // toggle on a round trip would make it feel broken on a slow connection, and a failed write costs
    // this session nothing - the user simply gets their old preference back on the next sign-in. The
    // name is read first because the endpoint takes both fields and inventing a name here would
    // overwrite one.
    if (isAuthenticated) {
      void getAccount()
        .then((account) => updateAccount(account.fullName, next))
        .catch(() => undefined)
    }
  }

  return (
    <button
      type="button"
      onClick={toggle}
      className="rounded-[var(--radius-md)] border px-3 py-1.5 text-[length:var(--text-body)] font-[var(--fw-medium)] transition-colors"
      style={{
        borderColor: 'var(--color-border)',
        color: 'var(--color-text-primary)',
        backgroundColor: 'var(--color-bg-surface)',
      }}
    >
      {t('language')}
    </button>
  )
}
