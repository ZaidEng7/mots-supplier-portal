import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Card, SkeletonList, useToast } from '../../components/ui'
import { OnboardingStepNav } from '../../components/OnboardingStepNav'
import { getOwnSupplier, type SupplierProfile } from '../../api/supplier'
import { linkCategory, unlinkCategory } from '../../api/categoryLinks'
import { fetchCategories } from '../../api/reference'
import { invalidateQuietly } from '../../lib/queryClient'

function isEditableState(state: string | undefined) {
  return state === 'EmailVerified' || state === 'ProfileInProgress' || state === 'InfoRequested'
}

export function OfferingsPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const queryClient = useQueryClient()
  const { notify } = useToast()
  const profileQuery = useQuery({ queryKey: ['own-supplier'], queryFn: getOwnSupplier })
  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: fetchCategories })
  const profile = profileQuery.data
  const editable = isEditableState(profile?.onboardingState)

  const onProfile = (data: SupplierProfile) => queryClient.setQueryData(['own-supplier'], data)

  /**
   * D-66: the checkbox that wrote and did not re-tick, and said nothing when it failed.
   *
   * <p>Two changes, and the second is the one that mattered. The response body is still used - it is the
   * server's own view of the profile - but the query is ALSO invalidated, so the tick comes from a re-read
   * rather than from trusting that this particular response carried the collection. And there is now an
   * error branch: a refused toggle told the supplier nothing at all, on the one screen whose completion
   * gates their whole application.</p>
   *
   * <p><b>Stated plainly:</b> the missing re-tick was reported twice from the walkthrough and could not be
   * reproduced from the source - the handler does update the aggregate and the response does carry the
   * categories. What is fixed here is that neither path depends on that any more: success re-reads, failure
   * speaks.</p>
   */
  const toggleMutation = useMutation({
    mutationFn: ({ code, linked }: { code: string; linked: boolean }) => (linked ? unlinkCategory(code) : linkCategory(code)),
    onSuccess: (data) => {
      onProfile(data)
      invalidateQuietly(queryClient, ['own-supplier'])
    },
    onError: () => notify({ kind: 'danger', title: t('offerings.toggleFailed') }),
  })

  if (profileQuery.isLoading || categoriesQuery.isLoading) {
    return <SkeletonList label={t('common.loading')} />
  }

  const linkedCodes = new Set(profile?.categories ?? [])
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
