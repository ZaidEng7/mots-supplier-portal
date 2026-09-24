// The coordinate fields on a supplier's address.
//
// COORDINATES ARE NOW REQUIRED, and the test that used to assert a blank pair became null is gone with the
// behaviour it described. What replaced it asserts the refusal: an address cannot be saved without a position.
// The ministry lists both as Must fields, so a form that let them through would produce exactly the file we
// already sent them - two empty columns.
//
// THE MAP ITSELF IS NOT TESTED HERE. Leaflet needs a laid-out container and jsdom gives it none, so what these
// tests drive is the pair of inputs the map writes into. That is the right seam anyway: the inputs are the
// accessible path and the thing the form actually submits, and a test that mocked a map into rendering would
// assert that the mock worked.
//
// THE FIELDS EXISTED EVERYWHERE EXCEPT THE SCREEN. Address.Latitude and Address.Longitude have been on the table, in the
// API contract and in the registry export since the schema was written, and every one of them was null across every
// supplier - not because nobody wanted to fill them in, but because nothing ever asked. That is the failure this file
// guards: a column the product carries and no human can reach.
//
// SO THE FIRST TEST IS THAT THE VALUE REACHES THE REQUEST, as a number. The form works in strings, the API takes numbers,
// and a coordinate posted as "33.5138" is a string where a double belongs - which the server would refuse and the supplier
// would read as a broken form. Asserting the rendered input alone would pass against a field that is typed into and
// dropped on the way out, which is a more likely mistake than the input not existing.
//
// AN EMPTY COORDINATE IS null, NOT 0. Zero is a real place in the Gulf of Guinea, and a supplier who left the field blank
// has not claimed to be there. This is the same rule the export follows for the same reason, and it is the one an
// `|| Number(...)` would break silently.
//
// OUT OF RANGE IS REFUSED AND SAYS WHY. A latitude of 91 is not a latitude. The control beside it is that a legitimate
// negative value - the southern hemisphere, which the regex-minded version of this check gets wrong - still saves.
//
// THE DECIMAL COMMA is why the input is text rather than type="number". "33,5" is what an Arabic keyboard and a European
// locale both produce, and a number input hands back an empty string for it: the coordinate vanishes and the supplier is
// told nothing. As text it is caught and refused with a message naming the decimal point.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a', useRouterState: () => ({ location: { pathname: '/onboarding/addresses' } }) }
})

const { AddressesPage } = await import('./AddressesPage')

const REGIONS = [{ code: 'DIM', nameEn: 'Damascus', nameAr: 'دمشق' }]

function profile(overrides: Record<string, unknown> = {}) {
  return {
    id: 's-1', supplierCode: 'SUP-000001',
    displayNameEn: 'Gulf Catering Co.', displayNameAr: 'الخليج',
    onboardingState: 'ProfileInProgress',
    addresses: [], branches: [],
    missingProfileFields: [],
    ...overrides,
  }
}

const routes = { '/api/v1/suppliers/me': profile(), '/api/v1/reference/regions': REGIONS }

// Editing an EXISTING address rather than adding one, because the region is a Radix select and driving it adds a
// dependency on that component's internals to a test about coordinates. An edit starts from a complete, valid address,
// so the only thing under test is what the two coordinate fields contribute to the request.
const EXISTING = {
  id: 'a-1', kind: 'HeadOffice', line1: '1 Baghdad Street', line2: null,
  city: 'Damascus', regionCode: 'DIM', country: 'SY', postalCode: null,
  latitude: null, longitude: null, isPrimary: true,
}

async function openTheEditDialog(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: /^edit$/i }))
}

describe('coordinates on a supplier address', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('are offered on the form at all, which is the whole gap', async () => {
    restore = mockFetch(routes)
    const user = userEvent.setup()

    renderPage(<AddressesPage />)
    await user.click(await screen.findByRole('button', { name: /add address/i }))

    expect(await screen.findByLabelText(/latitude/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/longitude/i)).toBeInTheDocument()
  })

  it('refuses a latitude that is not a latitude, and still accepts a southern one', async () => {
    restore = mockFetch(routes)
    const user = userEvent.setup()

    renderPage(<AddressesPage />)
    await user.click(await screen.findByRole('button', { name: /add address/i }))

    const latitude = await screen.findByLabelText(/latitude/i)
    await user.type(latitude, '91')
    await user.click(screen.getByRole('button', { name: /^save$/i }))

    expect(await screen.findByText(/between -90 and 90/i)).toBeInTheDocument()

    await user.clear(latitude)
    await user.type(latitude, '-33.86')
    await user.click(screen.getByRole('button', { name: /^save$/i }))

    expect(screen.queryByText(/between -90 and 90/i)).toBeNull()
  })

  it('refuses a decimal comma instead of dropping the value', async () => {
    restore = mockFetch(routes)
    const user = userEvent.setup()

    renderPage(<AddressesPage />)
    await user.click(await screen.findByRole('button', { name: /add address/i }))

    await user.type(await screen.findByLabelText(/latitude/i), '33,5')
    await user.type(screen.getByLabelText(/longitude/i), '36.2765')
    await user.click(screen.getByRole('button', { name: /^save$/i }))

    expect(await screen.findByText(/between -90 and 90/i)).toBeInTheDocument()
    expect(screen.queryByText(/between -180 and 180/i)).toBeNull()
  })
})

describe('what the coordinate fields send', () => {
  let restore: () => void
  let recorded: RecordedRequest[]
  afterEach(() => restore?.())

  const withAddress = {
    '/api/v1/suppliers/me': profile({ addresses: [EXISTING] }),
    '/api/v1/reference/regions': REGIONS,
  }

  it('sends the coordinates as numbers, not as the strings the form holds', async () => {
    recorded = []
    restore = mockFetch(withAddress, recorded)
    const user = userEvent.setup()

    renderPage(<AddressesPage />)
    await openTheEditDialog(user)
    await user.type(screen.getByLabelText(/latitude/i), '33.5138')
    await user.type(screen.getByLabelText(/longitude/i), '36.2765')
    await user.click(screen.getByRole('button', { name: /^save$/i }))

    const posted = recorded.find((r) => r.url.includes('/addresses') && r.method !== 'GET')
    expect(posted).toBeDefined()
    expect(JSON.parse(posted!.body)).toMatchObject({ latitude: 33.5138, longitude: 36.2765 })
  })

  it('refuses to save an address with no position at all', async () => {
    recorded = []
    restore = mockFetch(withAddress, recorded)
    const user = userEvent.setup()

    renderPage(<AddressesPage />)
    await openTheEditDialog(user)
    await user.click(screen.getByRole('button', { name: /^save$/i }))

    expect(await screen.findByText(/between -90 and 90/i)).toBeInTheDocument()
    expect(recorded.find((r) => r.url.includes('/addresses') && r.method !== 'GET')).toBeUndefined()
  })
})
