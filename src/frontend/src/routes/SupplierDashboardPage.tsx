import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { getSupplierDashboard, type ActionRequired } from '../api/supplierDashboard'
import { unreadNotificationCount } from '../api/notifications'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { StatusChip } from '../components/ui/StatusChip'
import { SkeletonGrid, SkeletonList } from '../components/ui/Skeleton'
import { formatDate, formatDeadline, formatNumber } from '../lib/datetime'
import { dismiss, isDismissed } from '../lib/dismissedChips'

/**
 * SCR-120 — the supplier dashboard. SCREEN-SPECIFICATIONS.md §1.
 *
 * <p>§1's regions in order: PageHeader with greeting and status badge, the conditional
 * action-required strip, the four-tile KPI row, and a two-column body that stacks on mobile —
 * invitations and proposals inline-start, profile health and notifications inline-end.</p>
 *
 * <p><b>Widget failures are isolated.</b> §1 is explicit that "per-widget ErrorPanel + retry
 * (isolated failures don't blank the page)", so the notification panel has its own query and its own
 * error branch: it failing must leave the KPI row, the invitations and the health card standing.
 * That is the requirement most easily lost by writing one page-level error state.</p>
 */
export function SupplierDashboardPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'

  const query = useQuery({ queryKey: ['supplier-dashboard'], queryFn: getSupplierDashboard })

  // A SEPARATE query, on purpose: §1's "Recent notifications" is its own widget, and one widget's
  // failure must not blank the page.
  const notifications = useQuery({
    queryKey: ['notifications', 'unread-count'],
    queryFn: unreadNotificationCount,
  })

  const [, forceRender] = useState(0)
  const data = query.data

  if (query.isPending) {
    return (
      <div className="flex flex-col gap-4">
        {/* §1: "header shows name immediately from session" - but the session store holds no
            company name, so the skeleton covers the header too. Reported as a gap. */}
        {/* Labelled with the SCREEN, not a widget: a skeleton whose accessible name is "Open
            invitations" is indistinguishable from the loaded tile of that name - to a screen reader
            and, as this cost me, to a test. */}
        <SkeletonGrid label={t('supplierDashboard.title')} items={4} columns={4} />
        <SkeletonList label={t('supplierDashboard.title')} rows={5} />
      </div>
    )
  }

  if (query.isError || !data) {
    return (
      <Card title={t('supplierDashboard.invitations')}>
        <p>{t('supplierDashboard.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => query.refetch()}>{t('supplierDashboard.retry')}</Button>
      </Card>
    )
  }

  // §1's "Not-yet-approved: dashboard replaced by onboarding progress banner linking to SCR-100".
  // Replaced, not emptied: a supplier who is not yet eligible for any invitation must not read
  // "Open invitations: 0" as a verdict on them.
  if (!data.isApproved) {
    return (
      <Card title={t('supplierDashboard.pendingTitle')}>
        <p>{t('supplierDashboard.pendingBody')}</p>
        <div className="mt-3 flex items-center gap-3">
          <StatusChip machine="onboarding" value={data.onboardingState} />
          <Link to="/onboarding">{t('supplierDashboard.pendingCta')}</Link>
        </div>
      </Card>
    )
  }

  const chips = ([
    // §1 links these to SCR-106/130. This app has no /documents route - document upload lives in
    // the onboarding wizard's documents step - so they land there. Reported as a route the
    // inventory names and the SPA does not have.
    ['expiringDocuments', data.actionRequired.expiringDocuments, '/onboarding'],
    ['rejectedDocuments', data.actionRequired.rejectedDocuments, '/onboarding'],
    ['invitationsClosingSoon', data.actionRequired.invitationsClosingSoon, '/rfqs'],
    ['clarificationsAnswered', data.actionRequired.clarificationsAnswered, '/rfqs'],
    ['awardOffers', data.actionRequired.awardOffers, '/rfqs'],
  ] as const).filter(([key, count]) => count > 0 && !isDismissed(key as keyof ActionRequired))

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-[length:var(--text-heading-lg)]">
            {t('supplierDashboard.greeting', { name: isArabic ? data.displayNameAr : data.displayNameEn })}
          </h1>
        </div>
        <StatusChip machine="onboarding" value={data.lifecycleState} />
      </header>

      {data.erpDegraded ? (
        <div role="status" className="rounded-[var(--radius-md)] p-3"
          style={{ background: 'var(--color-surface-raised)', border: '1px solid var(--color-border)' }}>
          {t('supplierDashboard.erpDegraded')}
        </div>
      ) : null}

      {chips.length > 0 ? (
        <ul className="flex flex-wrap gap-2">
          {chips.map(([key, count, href]) => (
            <li key={key} className="flex items-center gap-2 rounded-[var(--radius-md)] px-3 py-2"
              style={{ background: 'var(--color-surface-raised)', border: '1px solid var(--color-border)' }}>
              <Link to={href}>{t(`supplierDashboard.actionRequired.${key}`, { count: formatNumber(count, locale, 0) })}</Link>
              <button type="button" aria-label={t('supplierDashboard.actionRequired.dismiss')}
                onClick={() => { dismiss(key); forceRender((n) => n + 1) }}>
                ×
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      <ul className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {([
          ['openInvitations', data.kpis.openInvitations],
          ['draftProposals', data.kpis.draftProposals],
          ['submittedProposals', data.kpis.submittedProposals],
          ['documentsNeedingAttention', data.kpis.documentsNeedingAttention],
        ] as const).map(([key, value]) => (
          <li key={key} className="rounded-[var(--radius-md)] p-3" style={{ border: '1px solid var(--color-border)' }}>
            <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t(`supplierDashboard.kpis.${key}`)}
            </p>
            <p className="num text-[length:var(--text-heading-md)]">{formatNumber(value, locale, 0)}</p>
          </li>
        ))}
      </ul>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="flex flex-col gap-4 lg:col-span-2">
          <Card title={t('supplierDashboard.invitations')}>
            {data.invitations.length === 0 ? (
              <div className="py-6 text-center">
                <p className="font-[var(--fw-semibold)]">{t('supplierDashboard.emptyTitle')}</p>
                <p style={{ color: 'var(--color-text-secondary)' }}>{t('supplierDashboard.emptyBody')}</p>
              </div>
            ) : (
              <ul className="flex flex-col gap-2">
                {data.invitations.map((invitation) => (
                  <li key={invitation.rfqReferenceCode} className="flex flex-wrap items-center justify-between gap-2">
                    <Link to="/rfqs/$referenceCode" params={{ referenceCode: invitation.rfqReferenceCode }}>
                      {isArabic ? invitation.titleAr : invitation.titleEn}
                    </Link>
                    <span className="flex items-center gap-2">
                      {invitation.submissionClosesAt ? (
                        // §1's RTL note: countdowns keep their digits LTR-internal while the line
                        // flows RTL. <bdi> is what RTL §5.3 requires for exactly this.
                        <bdi className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                          {formatDeadline(invitation.submissionClosesAt, locale)}
                        </bdi>
                      ) : null}
                      <StatusChip machine="invitation" value={invitation.invitationStatus} />
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Card title={t('supplierDashboard.proposals')}>
            <ul className="flex flex-col gap-2">
              {data.proposals.map((proposal) => (
                <li key={proposal.proposalReferenceCode} className="flex flex-wrap items-center justify-between gap-2">
                  <Link to="/rfqs/$referenceCode/proposal" params={{ referenceCode: proposal.rfqReferenceCode }}>
                    {isArabic ? proposal.titleAr : proposal.titleEn}
                  </Link>
                  <span className="flex items-center gap-2">
                    <bdi className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                      {proposal.validityEnd
                        ? t('supplierDashboard.validUntil', { date: formatDate(proposal.validityEnd, locale) })
                        : t('supplierDashboard.noValidity')}
                    </bdi>
                    <StatusChip machine="proposal" value={proposal.state} />
                  </span>
                </li>
              ))}
            </ul>
          </Card>
        </div>

        <div className="flex flex-col gap-4">
          <Card title={t('supplierDashboard.profileHealth')}>
            {/* §1's RTL note: "Progress meter fills from inline-start". Logical properties, so it
                grows right-to-left under Arabic - a meter filling left-to-right on an Arabic page
                reads as emptying. */}
            <div role="progressbar"
              aria-valuenow={Math.round(data.profileHealth.completeness * 100)}
              aria-valuemin={0} aria-valuemax={100}
              aria-label={t('supplierDashboard.profileHealth')}
              className="h-2 w-full rounded-full"
              style={{ background: 'var(--color-border)' }}>
              <div className="h-2 rounded-full"
                style={{
                  inlineSize: `${Math.round(data.profileHealth.completeness * 100)}%`,
                  marginInlineStart: 0,
                  background: 'var(--color-primary)',
                }} />
            </div>
            <p className="mt-2">
              {t('supplierDashboard.completeness', {
                done: formatNumber(data.profileHealth.requiredDocumentsSupplied, locale, 0),
                total: formatNumber(data.profileHealth.requiredDocumentsTotal, locale, 0),
              })}
            </p>
            <p style={{ color: 'var(--color-text-secondary)' }}>
              {data.profileHealth.nextRequiredDocumentTypeCode
                ? t('supplierDashboard.nextDocument', { code: data.profileHealth.nextRequiredDocumentTypeCode })
                : t('supplierDashboard.allDocuments')}
            </p>
            <Link to="/onboarding">{t('supplierDashboard.profileHealth')}</Link>
          </Card>

          <Card title={t('supplierDashboard.notifications')}>
            {notifications.isError ? (
              // The isolated-failure requirement, made visible: this widget says it failed and
              // everything around it still renders.
              <div>
                <p>{t('supplierDashboard.loadFailed')}</p>
                <Button size="sm" variant="ghost" onClick={() => notifications.refetch()}>
                  {t('supplierDashboard.retry')}
                </Button>
              </div>
            ) : (
              <Link to="/notifications">{t('supplierDashboard.openNotifications')}</Link>
            )}
          </Card>
        </div>
      </div>
    </div>
  )
}
