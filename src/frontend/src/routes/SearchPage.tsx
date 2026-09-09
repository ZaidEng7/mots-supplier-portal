import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import {Badge, Button, Card, Field, Input, PageHeading, SkeletonList} from '../components/ui'
import { search, type SearchHit } from '../api/search'

/**
 * SCR-906, `/back-office/search`, back-office personas, P2.
 *
 * <p>Until now nothing searched across entities: the offering catalogue had a text box over two columns of
 * one table, and finding a tender meant knowing which list it was in. This is one query over RFQs,
 * suppliers and offerings, ranked together.</p>
 *
 * <p><b>What the caller gets back is scoped per kind on the server</b>, with each entity's own rule - a
 * search that returned a row the caller could not open would be a disclosure, and a quiet one, since the
 * title alone tells them the thing exists. Nothing on this screen re-filters; it renders what it is given.</p>
 *
 * <p><b>Submitted, not live.</b> A keystroke-per-request search over three tables is a load test aimed at
 * the database, and a person typing a reference code does not want results for its first three characters.</p>
 */
export function SearchPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')

  const [draft, setDraft] = useState('')
  const [submitted, setSubmitted] = useState('')

  const resultsQuery = useQuery({
    queryKey: ['search', submitted],
    queryFn: () => search(submitted),
    // Not fired for an empty box: the server answers an empty query with nothing, and asking it anyway
    // would mean a request on every page visit that can only return zero rows.
    enabled: submitted.trim().length > 0,
  })

  const linkFor = (hit: SearchHit): string | null => {
    if (hit.kind === 'rfq' && hit.referenceCode) return `/back-office/rfqs/${hit.referenceCode}`
    if (hit.kind === 'supplier' && hit.referenceCode) return `/back-office/review/${hit.referenceCode}`
    // An offering has no page of its own. Rather than inventing a route, the row stays unlinked and its
    // supplier code is shown - a dead link would be worse than an honest plain row.
    return null
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <PageHeading title={t('search.title')} subtitle={t('search.subtitle')} />
      </div>

      <Card title={t('search.formTitle')}>
        <form
          className="flex items-end gap-2"
          onSubmit={(event) => {
            event.preventDefault()
            setSubmitted(draft)
          }}
        >
          <div className="grow">
            <Field label={t('search.fields.query')} hint={t('search.hint')}>
              {(p) => <Input {...p} value={draft} onChange={(e) => setDraft(e.target.value)} />}
            </Field>
          </div>
          <Button type="submit" disabled={!draft.trim()}>{t('search.submit')}</Button>
        </form>
      </Card>

      {resultsQuery.isLoading ? <SkeletonList label={t('common.loading')} /> : null}

      {resultsQuery.isError ? (
        <Card title={t('search.resultsTitle')}>
          <p>{t('search.errors.failed')}</p>
          <Button variant="ghost" onClick={() => void resultsQuery.refetch()}>{t('search.retry')}</Button>
        </Card>
      ) : null}

      {resultsQuery.data ? (
        <Card title={t('search.resultsTitle')}>
          {/* The query is echoed, so "no results" cannot be confused with results for something else. */}
          <p className="mb-3 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('search.resultsFor', { query: resultsQuery.data.query, count: resultsQuery.data.hits.length })}
          </p>

          {resultsQuery.data.truncated ? (
            <output className="block mb-3 rounded-[var(--radius-md)] p-3"
              style={{ backgroundColor: 'var(--color-warning-bg)', color: 'var(--color-warning-fg)' }}>
              {t('search.truncated')}
            </output>
          ) : null}

          {resultsQuery.data.hits.length === 0 ? (
            <div>
              <p style={{ color: 'var(--color-text-secondary)' }}>{t('search.empty')}</p>
              {/* The unstemmed-search caveat, said where it matters rather than in a document nobody
                  reads: this matches whole words and prefixes, so a plural will not find a singular. */}
              <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {t('search.emptyHint')}
              </p>
            </div>
          ) : (
            <ul className="flex flex-col gap-2">
              {resultsQuery.data.hits.map((hit) => {
                const to = linkFor(hit)
                const title = isArabic ? hit.titleAr : hit.titleEn
                return (
                  <li
                    key={`${hit.kind}:${hit.referenceCode ?? title}`}
                    className="rounded-[var(--radius-md)] p-3"
                    style={{ border: '1px solid var(--color-border)' }}
                  >
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge tone={hit.kind === 'rfq' ? 'brand' : hit.kind === 'supplier' ? 'info' : 'neutral'}>
                        {t(`search.kinds.${hit.kind}`, { defaultValue: hit.kind })}
                      </Badge>
                      {to ? (
                        <Link to={to} style={{ color: 'var(--color-text-link)' }}>{title}</Link>
                      ) : (
                        <span style={{ color: 'var(--color-text-primary)' }}>{title}</span>
                      )}
                      {hit.referenceCode ? (
                        <span className="font-mono text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                          {hit.referenceCode}
                        </span>
                      ) : null}
                      {hit.context ? (
                        <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                          {hit.context}
                        </span>
                      ) : null}
                    </div>
                  </li>
                )
              })}
            </ul>
          )}
        </Card>
      ) : null}
    </div>
  )
}
