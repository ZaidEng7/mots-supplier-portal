// The onboarding step where a supplier declares which categories they can serve.
//
// Editing is gated on the onboarding state: a supplier under review who can still change their categories is changing the
// basis on which they are being reviewed, and Approved is the same argument after the fact.
//
// D-66: THE CHECKBOX THAT WROTE, DID NOT RE-TICK, AND SAID NOTHING WHEN IT FAILED. Two changes, and the second is the one
// that mattered. The response body is still used - it is the server's own view of the profile - but the query is ALSO
// invalidated, so the tick comes from a re-read rather than from trusting that this particular response carried the
// collection. And there is now an error branch: a refused toggle told the supplier nothing at all, on the one screen whose
// completion gates their whole application.
//
// Stated plainly: the missing re-tick was reported twice from the walkthrough and could not be reproduced from the source -
// the handler does update the aggregate and the response does carry the categories. What is fixed is that neither path
// depends on that any more: success re-reads, failure speaks.

import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {Card, PageHeading, QueryError, SkeletonList, useToast} from '../../components/ui'
import { FormMeasure } from '../../components/ui/FormMeasure'
import { OnboardingStepNav } from '../../components/OnboardingStepNav'
import { getOwnSupplier, type SupplierProfile } from '../../api/supplier'
import { linkCategory, unlinkCategory, setPrimaryCategory } from '../../api/categoryLinks'
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

  const toggleMutation = useMutation({
    mutationFn: ({ code, linked }: { code: string; linked: boolean }) => (linked ? unlinkCategory(code) : linkCategory(code)),
    onSuccess: (data) => {
      onProfile(data)
      invalidateQuietly(queryClient, { queryKey: ['own-supplier'] })
    },
    onError: () => notify({ kind: 'danger', title: t('offerings.toggleFailed') }),
  })

  const primaryMutation = useMutation({
    mutationFn: (code: string) => setPrimaryCategory(code),
    onSuccess: (data) => {
      queryClient.setQueryData(['own-supplier'], data)
      void profileQuery.refetch()
    },
    onError: () => notify({ kind: 'danger', title: t('offerings.primaryFailed') }),
  })

  if (profileQuery.isError || categoriesQuery.isError) {
    return <QueryError error={profileQuery.error} onRetry={() => { void profileQuery.refetch(); void categoriesQuery.refetch() }} />
  }

  if (profileQuery.isLoading || categoriesQuery.isLoading) {
    return <SkeletonList label={t('common.loading')} />
  }

  const linkedCodes = new Set(profile?.categories ?? [])
  const primaryCode = profile?.primaryCategoryCode ?? null
  const categories = categoriesQuery.data ?? []
  const missingCategoryLink = (profile?.missingProfileFields ?? []).includes('categoryLink')

  return (
    <FormMeasure>
      <div>
        <PageHeading title={t('offerings.title')} subtitle={t('offerings.subtitle')} />
      </div>

      <OnboardingStepNav />

      {missingCategoryLink ? (
        <p role="alert" className="rounded-[var(--radius-md)] px-4 py-3 text-[length:var(--text-body-sm)]" style={{ backgroundColor: 'var(--color-warning-bg)', color: 'var(--color-warning-fg)' }}>
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
                    className="flex cursor-pointer items-center gap-3 rounded-[var(--radius-md)] p-3 text-[length:var(--text-body-sm)]"
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

                  {linked ? (
                    <label className="mt-1 flex cursor-pointer items-center gap-2 ps-3 text-[length:var(--text-caption)]">
                      <input
                        type="radio"
                        name="primaryCategory"
                        checked={primaryCode === c.code}
                        disabled={!editable || primaryMutation.isPending}
                        onChange={() => primaryMutation.mutate(c.code)}
                      />
                      <span style={{ color: 'var(--color-text-secondary)' }}>{t('offerings.primaryLabel')}</span>
                    </label>
                  ) : null}
                </li>
              )
            })}
          </ul>
        )}
      </Card>
    </FormMeasure>
  )
}
