import { useTranslation } from 'react-i18next'
import { Link, useRouterState } from '@tanstack/react-router'

const STEPS = [
  { path: '/onboarding', key: 'company' },
  { path: '/onboarding/contacts', key: 'contacts' },
  { path: '/onboarding/addresses', key: 'addresses' },
  { path: '/onboarding/banking', key: 'banking' },
  { path: '/onboarding/offerings', key: 'offerings' },
] as const

/** Lightweight cross-navigation between the onboarding sub-screens - SCR-101 through SCR-105 each
 * have their own route, but nothing else linked between them, so without this they'd be
 * effectively unreachable from each other. */
export function OnboardingStepNav() {
  const { t } = useTranslation()
  const pathname = useRouterState({ select: (s) => s.location.pathname })

  return (
    <nav
      aria-label={t('onboarding.stepNavLabel')}
      className="flex flex-wrap gap-1.5 overflow-x-auto rounded-[0.75rem] p-1.5"
      style={{ backgroundColor: 'var(--color-bg-sunken)' }}
    >
      {STEPS.map((step) => {
        const active = pathname === step.path
        return (
          <Link
            key={step.path}
            to={step.path}
            className="whitespace-nowrap rounded-[0.5rem] px-3 py-1.5 text-[length:var(--text-body-sm)] font-[var(--fw-medium)] transition-colors"
            style={{
              backgroundColor: active ? 'var(--color-bg-surface)' : 'transparent',
              color: active ? 'var(--color-text-brand)' : 'var(--color-text-secondary)',
              boxShadow: active ? 'var(--shadow-sm, 0 1px 2px rgba(0,0,0,0.06))' : 'none',
            }}
          >
            {t(`onboarding.steps.${step.key}`)}
          </Link>
        )
      })}
    </nav>
  )
}
