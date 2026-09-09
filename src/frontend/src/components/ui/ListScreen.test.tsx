import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryError } from './ListScreen'
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
