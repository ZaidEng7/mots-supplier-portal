import { useTranslation } from 'react-i18next'
import { RTL_LANGUAGES } from '../i18n/config'

export function LanguageSwitch() {
  const { i18n, t } = useTranslation()

  const toggle = () => {
    const next = RTL_LANGUAGES.has(i18n.language) ? 'en' : 'ar'
    void i18n.changeLanguage(next)
  }

  return (
    <button
      type="button"
      onClick={toggle}
      className="rounded-md border px-3 py-1.5 text-sm font-medium transition-colors"
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
