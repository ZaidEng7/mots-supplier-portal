import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import {Badge, Button, Card, Field, Input, PageHeading, SkeletonList, StatusChip, toneFor} from '../components/ui'
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
const KIND_TONES = { rfq: 'brand', supplier: 'info' } as const

export function SearchPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')

  /*
    The query lives in the URL rather than in this component.

    It was local state, which had three consequences a reader meets: the top bar could not submit into
    this screen (so it was a link to an empty page instead of a search box), a search could not be
    linked to or reloaded, and the back button took you out of the screen rather than back to the
    previous search. All three are the same fact - a search IS an address.
  */
  const { q } = useSearch({ from: '/back-office/search' })
  const navigate = useNavigate()
  const submitted = q ?? ''
  const [draft, setDraft] = useState(submitted)

  // Arriving with a query in the address - from the top bar, a bookmark, or the back button - fills the
  // box with it, so the reader can see and edit what was searched rather than facing an empty field
  // above their own results.
  //
  // Adjusted during render rather than in an effect: an effect would render once with the stale box,
  // then set state and render again, and React's own guidance for "reset state when an input changes"
  // is this comparison. The reader never sees the intermediate frame.
  const [lastSubmitted, setLastSubmitted] = useState(submitted)
  if (lastSubmitted !== submitted) {
    setLastSubmitted(submitted)
    setDraft(submitted)
  }

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
            const query = draft.trim()
            void navigate({ to: '/back-office/search', search: query === '' ? {} : { q: query } })
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

      {/*
        Before anything has been searched this screen was a page containing one empty box and nothing
        else, which reads as an unfinished screen rather than as a starting point. It now says what it
        searches and what it will not find, which is the same two sentences the results already carry
        when they come back empty - moved to where they answer the question a reader arrives with.
      */}
      {submitted.trim() === '' ? (
        <Card title={t('search.resultsTitle')}>
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('search.idle')}</p>
          <p className="mt-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('search.emptyHint')}
          </p>
        </Card>
      ) : null}

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
                      <Badge tone={toneFor(hit.kind, KIND_TONES)}>
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
                      {hit.context ? <HitContext kind={hit.kind} context={hit.context} /> : null}
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

/**
 * The third column of a result row, which is a different thing for each kind.
 *
 * <p><b>What this replaces.</b> `{hit.context}` printed raw. The server puts three unrelated values in
 * that one field - an RFQ's state, a supplier's lifecycle state, and for an offering the supplier's
 * reference code - so a search for "catering" answered with `Draft`, `InternalReview` and `Completed`
 * in English, on an Arabic page as readily as an English one. They are enum members, not words anybody
 * chose for a reader.</p>
 *
 * <p>Every one of those states already has an authored label in both languages, under `status.rfq.*`
 * and `status.onboarding.*`, and a chip that renders them. The search screen was simply not using it.
 * Interpreting by `kind` needs no change to the wire: the screen already decides what to link to the
 * same way.</p>
 *
 * <p>An offering's context is a supplier CODE - an identifier a person reads and copies, not a word -
 * so it stays as it is, in the same monospace treatment the reference code above it gets.</p>
 */
function HitContext({ kind, context }: Readonly<{ kind: SearchHit['kind']; context: string }>) {
  if (kind === 'rfq') return <StatusChip machine="rfq" value={context} />
  if (kind === 'supplier') return <StatusChip machine="onboarding" value={context} />

  return (
    <span className="font-mono text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
      {context}
    </span>
  )
}
