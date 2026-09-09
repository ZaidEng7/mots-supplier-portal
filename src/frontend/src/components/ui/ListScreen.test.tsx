import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ListCard, QueryError } from './ListScreen'
import { RfqApiError } from '../../api/rfqs'

/**
 * The audit's §C5: five of six read failures are interchangeable ("Could not load X"), so a reader
 * learns THAT it failed and never WHY.
 *
 * <p><b>The why was already in the building.</b> Every API module wraps a failure in a typed error
 * whose `message` came from `problemMessage()`, which prefers the server's RFC 9457 `detail` - its
 * "human-readable explanation of this occurrence". Write paths render it: an upload tells you which
 * rule it broke. Read paths threw it away and rendered a static fallback, so the fallback was being
 * used as the whole message.</p>
 *
 * <p>The assertions name translation KEYS, not English: i18n loads no resources under vitest, so t()
 * returns the key. That is the right level here anyway - this test is about which message is CHOSEN,
 * and the wording is asserted in i18n's own tests.</p>
 *
 * <p>The second case is the one that decides the design. A dropped connection throws a plain
 * `TypeError` reading "Failed to fetch", and a component bug throws whatever it throws. Neither is
 * prose to show a supplier, so only errors this app built from a problem document are rendered.</p>
 */
describe('QueryError shows the server explanation when there is one', () => {
  it('renders the server detail instead of the generic fallback', () => {
    render(<QueryError error={new RfqApiError(409, { detail: 'The clarification window has closed.' })} />)

    expect(screen.getByText('The clarification window has closed.')).toBeInTheDocument()
    expect(screen.queryByText('common.loadFailed')).not.toBeInTheDocument()
  })

  it('falls back when the throw is not one of ours', () => {
    // What a dropped connection actually throws. "Failed to fetch" must never reach a reader.
    render(<QueryError error={new TypeError('Failed to fetch')} />)

    expect(screen.getByText('common.loadFailed')).toBeInTheDocument()
    expect(screen.queryByText('Failed to fetch')).not.toBeInTheDocument()
  })

  it('falls back when the server sent a problem document with no prose in it', () => {
    // A bare 503 with no `detail` and no `title`: problemMessage() then returns "Request failed: 503",
    // which is developer text and is caught by the same guard.
    render(<QueryError error={new RfqApiError(503, null)} />)

    expect(screen.getByText('common.loadFailed')).toBeInTheDocument()
    expect(screen.queryByText(/Request failed/)).not.toBeInTheDocument()
  })

  it('falls back when given no error at all', () => {
    render(<QueryError />)

    expect(screen.getByText('common.loadFailed')).toBeInTheDocument()
  })
})

/**
 * What `ListCard` must carry before a screen can stop keeping its own `Card`.
 *
 * <p>Adoption stalled at four screens, and the reason was the component: a screen that offered a retry,
 * chose a skeleton shape, put a filter beside the title, or printed a standing note under the table had
 * to keep its own `Card` to keep any of those. Each is asserted here because each is why some screen was
 * not adopting.</p>
 */
describe('ListCard carries what the screens kept their own Card for', () => {
  const labels = { loading: 'loading', error: 'failed', empty: 'nothing here' }
  const failed = { isPending: false, isError: true, error: new RfqApiError(500, {}) }

  it('offers the way out of a failure when the query can refetch', async () => {
    const refetch = vi.fn()
    render(
      <ListCard title="Tenders" query={{ ...failed, refetch }} isEmpty={false} labels={labels}>
        <p>rows</p>
      </ListCard>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'common.retry' }))
    expect(refetch).toHaveBeenCalledTimes(1)
  })

  /**
   * The denominator. A card that always drew a retry button would pass the test above and lie on every
   * screen holding a query it cannot re-run.
   */
  it('offers no retry when the query cannot be refetched', () => {
    render(
      <ListCard title="Tenders" query={failed} isEmpty={false} labels={labels}>
        <p>rows</p>
      </ListCard>,
    )

    expect(screen.queryByRole('button', { name: 'common.retry' })).toBeNull()
  })

  it('shows a control beside the title and a note beneath the list', () => {
    render(
      <ListCard
        title="Tenders"
        action={<button type="button">Mine only</button>}
        footer={<p>Inactive rows are hidden.</p>}
        query={{ isPending: false, isError: false }}
        isEmpty={false}
        labels={labels}
      >
        <p>rows</p>
      </ListCard>,
    )

    expect(screen.getByRole('button', { name: 'Mine only' })).toBeInTheDocument()
    expect(screen.getByText('Inactive rows are hidden.')).toBeInTheDocument()
  })

  /**
   * The note is about the table, not about the rows, so it outlives them - which is the whole reason it
   * is a prop rather than the last child.
   */
  it('keeps the note when there are no rows to put it under', () => {
    render(
      <ListCard
        title="Tenders"
        footer={<p>Inactive rows are hidden.</p>}
        query={{ isPending: false, isError: false }}
        isEmpty
        labels={labels}
      >
        <p>rows</p>
      </ListCard>,
    )

    expect(screen.getByText('nothing here')).toBeInTheDocument()
    expect(screen.queryByText('rows')).toBeNull()
    expect(screen.getByText('Inactive rows are hidden.')).toBeInTheDocument()
  })
})
