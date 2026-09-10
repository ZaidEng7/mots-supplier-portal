import { useTranslation } from 'react-i18next'
import { Link, useRouterState } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { getOwnSupplier } from '../api/supplier'

/**
 * Which required fields each step is the place to fix.
 *
 * <p>The values are `SupplierDto.missingProfileFields`' own strings (Domain/Suppliers/Supplier.cs,
 * `GetMissingProfileFields`), not display keys - the same list `OnboardingPage`'s gate reads, so a step
 * card and the gate can never disagree about what is outstanding.</p>
 *
 * <p>A step with an empty list is genuinely optional: nothing on it blocks submission. That is a fact
 * about the server's list, not a judgement made here, which is why the list is transcribed rather than
 * inferred.</p>
 */
const STEPS = [
  { path: '/onboarding', key: 'company', fields: ['legalInfo', 'currencyCode', 'primaryContactPhone', 'termsAccepted'] },
  { path: '/onboarding/contacts', key: 'contacts', fields: [] },
  { path: '/onboarding/addresses', key: 'addresses', fields: ['address'] },
  { path: '/onboarding/banking', key: 'banking', fields: [] },
  { path: '/onboarding/offerings', key: 'offerings', fields: ['categoryLink'] },
] as const

type StepStatus = { tone: 'warning' | 'success' | 'muted'; label: string }

/**
 * The five steps of an application, each carrying how it stands.
 *
 * <p><b>What this replaces.</b> A row of five pills naming the steps and nothing else. A supplier could
 * see where they were and not what was left, so finding the one step still blocking them meant opening
 * all five. The comp gives each step a card with its own answer.</p>
 *
 * <p><b>It reads the profile itself.</b> Five screens render this, and threading the supplier through
 * all five would have put the same prop in five signatures for one component's benefit. The query key is
 * the one those screens already use, so this is the cached answer rather than a sixth request.</p>
 */
export function OnboardingStepNav() {
  const { t } = useTranslation()
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const profileQuery = useQuery({ queryKey: ['own-supplier'], queryFn: getOwnSupplier })

  const profile = profileQuery.data
  const missing = new Set(profile?.missingProfileFields ?? [])
  /**
   * Status is about work the supplier can still do. Once an application is with a reviewer, "1 thing
   * left" describes a form they can no longer edit, which is worse than saying nothing.
   */
  const isEditable = ['EmailVerified', 'ProfileInProgress', 'InfoRequested'].includes(profile?.onboardingState ?? '')

  const statusFor = (fields: readonly string[]): StepStatus | null => {
    if (!profile || !isEditable) return null
    if (fields.length === 0) return { tone: 'muted', label: t('onboarding.stepStatus.optional') }
    const left = fields.filter((field) => missing.has(field)).length
    if (left === 0) return { tone: 'success', label: t('onboarding.stepStatus.complete') }
    return { tone: 'warning', label: t('onboarding.stepStatus.left', { count: left }) }
  }

  return (
    <nav aria-label={t('onboarding.stepNavLabel')}>
      <ol className="m-0 grid list-none grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3 p-0">
        {STEPS.map((step, index) => {
          const active = pathname === step.path
          const status = statusFor(step.fields)
          return (
            <li key={step.path}>
              <Link
                to={step.path}
                aria-current={active ? 'step' : undefined}
                className="flex h-full flex-col gap-1 rounded-[var(--radius-lg)] p-3"
                style={{
                  backgroundColor: 'var(--color-bg-surface)',
                  border: `1px solid ${active ? 'var(--color-brand-solid)' : 'var(--color-border)'}`,
                  boxShadow: active ? '0 0 0 3px var(--color-accent-wash)' : 'var(--shadow-sm)',
                  textDecoration: 'none',
                }}
              >
                <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
                  {t('onboarding.stepNumber', { number: index + 1 })}
                </span>
                <span
                  className="text-[length:var(--text-body)] font-[var(--fw-semibold)]"
                  style={{ color: 'var(--color-text-primary)' }}
                >
                  {t(`onboarding.steps.${step.key}`)}
                </span>
                {status ? (
                  <span className="text-[length:var(--text-body-sm)]" style={{ color: statusColour(status.tone) }}>
                    {status.label}
                  </span>
                ) : null}
              </Link>
            </li>
          )
        })}
      </ol>
    </nav>
  )
}

function statusColour(tone: StepStatus['tone']): string {
  if (tone === 'warning') return 'var(--color-warning-fg)'
  if (tone === 'success') return 'var(--color-success-fg)'
  return 'var(--color-text-secondary)'
}
