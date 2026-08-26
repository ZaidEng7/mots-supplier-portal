import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { RTL_LANGUAGES } from './config'

/** Flips document dir/lang whenever the active language changes (docs/ux/RESPONSIVE-AND-RTL.md). */
export function useDirection() {
  const { i18n } = useTranslation()
  const dir = RTL_LANGUAGES.has(i18n.language) ? 'rtl' : 'ltr'

  useEffect(() => {
    document.documentElement.dir = dir
    document.documentElement.lang = i18n.language
  }, [dir, i18n.language])

  return dir
}
