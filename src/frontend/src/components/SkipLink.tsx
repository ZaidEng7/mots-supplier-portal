import { useTranslation } from 'react-i18next'

/**
 * WCAG 2.4.1 Bypass Blocks, which `docs/ux/ACCESSIBILITY.md:52` has required since it was written and
 * nothing implemented.
 *
 * <p><b>What it costs a keyboard user not to have this.</b> The first Tab on an authenticated screen
 * lands on "Dashboard", and there are fourteen chrome stops before the first control on the page —
 * repeated on every navigation, all day. `axe` cannot see the absence: its `skip-link` rule is tagged
 * `best-practice`, and this suite runs the `wcag2a`/`wcag2aa`/`wcag22aa` tags only.</p>
 *
 * <p>Hidden until focused, then a real, visible control at the start of the page — a skip link nobody can
 * see when they land on it is the same as no skip link. It uses `position: fixed` rather than absolute so
 * it cannot be clipped by an ancestor's `overflow`.</p>
 */
export function SkipLink() {
  const { t } = useTranslation()

  return (
    <a
      href="#main"
      className="sr-only focus:not-sr-only focus:fixed focus:inset-block-start-0 focus:inset-inline-start-0 focus:m-2 focus:rounded-[var(--radius-md)] focus:px-4 focus:py-2 focus:text-[length:var(--text-body)] focus:font-[var(--fw-medium)]"
      style={{
        zIndex: 'var(--z-tooltip)',
        backgroundColor: 'var(--color-brand-solid)',
        color: 'var(--color-text-inverse)',
        boxShadow: 'var(--focus-ring)',
      }}
    >
      {t('nav.skipToContent')}
    </a>
  )
}
