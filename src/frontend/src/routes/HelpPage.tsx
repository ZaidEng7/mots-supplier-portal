import { useTranslation } from 'react-i18next'
import { Link } from '@tanstack/react-router'
import { PageHeading } from '../components/ui/ListScreen'

/**
 * SCR-907 — help, written from what the software actually does.
 *
 * <p>The inventory calls for "FAQ, contextual guidance, contact support", and the guidance here is
 * deliberately narrow: every answer below describes a behaviour that exists in this codebase and can
 * be checked against it. Nothing explains a policy, because a help page that invents a rule is worse
 * than no help page - a user would act on it.</p>
 *
 * <p><b>Contact support says the channel is not configured.</b> No address, phone number or hours
 * appear anywhere in this repository, and those belong to the ministry rather than to an
 * implementation. An invented address would send real users nowhere.</p>
 */
export function HelpPage() {
  const { t } = useTranslation()

  const topics = [
    { key: 'submitProposal', to: '/rfqs' },
    { key: 'afterSubmitting', to: '/proposals' },
    { key: 'clarification', to: '/proposals' },
    { key: 'documentsExpiring', to: '/documents' },
    { key: 'language', to: '/settings' },
    { key: 'password', to: '/settings' },
  ] as const

  return (
    <div className="flex flex-col gap-6">
      <div>
        <PageHeading title={t('help.title')} subtitle={t('help.subtitle')} />
      </div>

      <section className="flex flex-col gap-3">
        {topics.map((topic) => (
          <div
            key={topic.key}
            className="rounded-[var(--radius-lg)] p-5"
            style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
          >
            <h2 className="mb-1 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
              {t(`help.topics.${topic.key}.question`)}
            </h2>
            <p className="mb-2" style={{ color: 'var(--color-text-secondary)' }}>
              {t(`help.topics.${topic.key}.answer`)}
            </p>
            {/* Every answer ends somewhere the user can act, because guidance that stops at an
                explanation makes the reader hunt for the screen it just described. */}
            <Link to={topic.to} className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-link)' }}>
              {t(`help.topics.${topic.key}.action`)}
            </Link>
          </div>
        ))}
      </section>

      <section className="rounded-[var(--radius-lg)] p-5" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
        <h2 className="mb-2 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('help.contactTitle')}
        </h2>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('help.contactPending')}</p>
      </section>
    </div>
  )
}
