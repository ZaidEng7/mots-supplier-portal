// Flips the document's dir and lang whenever the active language changes, per RESPONSIVE-AND-RTL.md.
//
// One owner for that side effect: the toast reads the language directly rather than calling this, so nothing else writes those
// two attributes.

import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { RTL_LANGUAGES } from './config'

export function useDirection() {
  const { i18n } = useTranslation()
  const dir = RTL_LANGUAGES.has(i18n.language) ? 'rtl' : 'ltr'

  useEffect(() => {
    document.documentElement.dir = dir
    document.documentElement.lang = i18n.language
  }, [dir, i18n.language])

  return dir
}
