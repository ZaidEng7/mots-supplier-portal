import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Card } from '../../components/ui'
import { OnboardingStepNav } from '../../components/OnboardingStepNav'
import { getOwnSupplier, type SupplierProfile } from '../../api/supplier'
import { linkCategory, unlinkCategory } from '../../api/categoryLinks'
import { fetchCategories } from '../../api/reference'

function isEditableState(state: string | undefined) {
  return state === 'EmailVerified' || state === 'ProfileInProgress' || state === 'InfoRequested'
}

export function OfferingsPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const queryClient = useQueryClient()
  const profileQuery = useQuery({ queryKey: ['own-supplier'], queryFn: getOwnSupplier })
  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: fetchCategories })
  const profile = profileQuery.data
  const editable = isEditableState(profile?.onboardingState)

  const onProfile = (data: SupplierProfile) => queryClient.setQueryData(['own-supplier'], data)

  const toggleMutation = useMutation({
    mutationFn: ({ code, linked }: { code: string; linked: boolean }) => (linked ? unlinkCategory(code) : linkCategory(code)),
    onSuccess: onProfile,
  })

  if (profileQuery.isLoading || categoriesQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }

  const linkedCodes = new Set(profile?.categoryCodes ?? [])
  const categories = categoriesQuery.data ?? []
  const missingCategoryLink = (profile?.missingProfileFields ?? []).includes('categoryLink')

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('offerings.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('offerings.subtitle')}
        </p>
      </div>

      <OnboardingStepNav />

      {missingCategoryLink ? (
        <p role="alert" className="rounded-[0.5rem] px-4 py-3 text-[length:var(--text-body-sm)]" style={{ backgroundColor: 'var(--warning-50)', color: 'var(--warning-600)' }}>
          {t('offerings.missingCategory')}
        </p>
      ) : null}

      <Card title={t('offerings.categoriesTitle')}>
        {categories.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('offerings.empty')}</p>
        ) : (
          <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            {categories.map((c) => {
              const linked = linkedCodes.has(c.code)
              return (
                <li key={c.code}>
                  <label
                    className="flex cursor-pointer items-center gap-3 rounded-[0.5rem] p-3 text-[length:var(--text-body-sm)]"
                    style={{ border: `1px solid ${linked ? 'var(--color-brand-solid)' : 'var(--color-border)'}`, backgroundColor: linked ? 'var(--color-brand-subtle)' : 'transparent' }}
                  >
                    <input
                      type="checkbox"
                      checked={linked}
                      disabled={!editable || toggleMutation.isPending}
                      onChange={() => toggleMutation.mutate({ code: c.code, linked })}
                    />
                    <span style={{ color: 'var(--color-text-primary)' }}>{isArabic ? c.nameAr : c.nameEn}</span>
                  </label>
                </li>
              )
            })}
          </ul>
        )}
      </Card>
    </div>
  )
}
