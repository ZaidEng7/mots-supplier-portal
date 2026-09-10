import { useEffect, useState, type ReactNode } from 'react'
import i18n from '../../i18n/config'

/**
 * Renders a story in Arabic, right to left, the way the product actually runs it.
 *
 * <p><b>Why this exists.</b> Every chart in this product flips something for Arabic - the numeric
 * axis, the axis side, the margins, the label position, the corner the bar is rounded at - and none of
 * those flips had ever been looked at, in any instrument. `dir="rtl"` alone is not the test: the
 * components decide from `i18n.language`, so a story that only set the attribute would render an LTR
 * chart inside an RTL document and prove nothing about either. This sets the language and lets
 * everything follow from it.</p>
 *
 * <p>It restores the language on unmount so one story cannot change what the next one renders.</p>
 */
export function ArabicStory({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(i18n.language.startsWith('ar'))

  useEffect(() => {
    const previous = i18n.language
    void i18n.changeLanguage('ar').then(() => setReady(true))
    document.documentElement.setAttribute('dir', 'rtl')
    document.documentElement.setAttribute('lang', 'ar')
    return () => {
      void i18n.changeLanguage(previous)
      document.documentElement.setAttribute('dir', 'ltr')
      document.documentElement.setAttribute('lang', previous)
    }
  }, [])

  // Held back until the catalogue has actually switched: recharts measures on first render and does
  // not re-measure on a language change, so a chart drawn in English and relabelled in Arabic would
  // keep English geometry and hide the very defect this decorator exists to show.
  if (!ready) return null
  return <div dir="rtl" lang="ar">{children}</div>
}
