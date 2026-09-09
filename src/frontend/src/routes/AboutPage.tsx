import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { getMeta } from '../api/meta'
import { PageHeading } from '../components/ui/ListScreen'

/**
 * SCR-908 — what this build is, and what to quote when reporting a problem.
 *
 * <p>Public, and not only for tidiness: the moment a user most needs to say which version they are on
 * is when they cannot sign in. The route sits outside every authenticated layout for that reason.</p>
 *
 * <p>The correlation guidance is the useful half. Every error response carries a `correlationId`, and
 * until now nothing told a user that the string in a failure message is the thing support needs.</p>
 */
export function AboutPage() {
  const { t } = useTranslation()
  const metaQuery = useQuery({ queryKey: ['meta'], queryFn: getMeta })

  return (
    <main className="mx-auto flex max-w-[42rem] flex-col gap-6 p-6">
      <PageHeading title={t('about.title')} />

      <section className="rounded-[var(--radius-lg)] p-6" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
        <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('about.buildTitle')}
        </h2>
        <dl className="flex flex-col gap-2">
          <div>
            <dt className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>{t('about.version')}</dt>
            {/* Failure is reported, not hidden behind a dash: "we could not reach the server" and
                "this build has no version" are different facts and support needs to know which. */}
            <dd style={{ color: 'var(--color-text-primary)' }}>
              {metaQuery.isError ? t('about.unavailable') : (metaQuery.data?.version ?? t('about.loading'))}
            </dd>
          </div>
          {metaQuery.data?.commit ? (
            <div>
              <dt className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>{t('about.commit')}</dt>
              <dd className="font-mono text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-primary)' }}>{metaQuery.data.commit}</dd>
            </div>
          ) : null}
        </dl>
      </section>

      <section className="rounded-[var(--radius-lg)] p-6" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
        <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('about.supportTitle')}
        </h2>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('about.correlationHelp')}</p>
      </section>

      {/*
        Legal is a HEADING and no text. SCREEN-INVENTORY asks for "legal links", and terms of use and a
        privacy notice are documents the ministry writes, not text an implementation may compose. A
        plausible-looking policy nobody approved is worse than an acknowledged gap, so the section says
        what is missing and who owes it.
      */}
      <section className="rounded-[var(--radius-lg)] p-6" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
        <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('about.legalTitle')}
        </h2>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('about.legalPending')}</p>
      </section>
    </main>
  )
}
