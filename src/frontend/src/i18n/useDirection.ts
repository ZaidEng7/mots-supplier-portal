// Flips the document's dir and lang whenever the active language changes, per RESPONSIVE-AND-RTL.md, and names the
// browser tab in that language.
//
// One owner for that side effect: the toast reads the language directly rather than calling this, so nothing else writes those
// attributes or the title.
//
// THE TITLE FOLLOWS THE LANGUAGE for the same reason the direction does. index.html starts it as the Arabic name, the
// product's default, because the tab shows it before any script runs; after that the portal's name in the chosen
// language replaces it - and an administrator's rewording of that name reaches the tab too, since the overrides
// re-announce the language when they land.

import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { RTL_LANGUAGES } from './config'

export function useDirection() {
  const { t, i18n } = useTranslation()
  const dir = RTL_LANGUAGES.has(i18n.language) ? 'rtl' : 'ltr'

  useEffect(() => {
    document.documentElement.dir = dir
    document.documentElement.lang = i18n.language
    document.title = t('appName')
  }, [dir, i18n.language, t])

  return dir
}
