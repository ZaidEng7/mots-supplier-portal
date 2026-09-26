// The map picker's tile loading, which had no test and did not work.
//
// WHAT HAPPENED. The tile address was written as a path, /api/v1/map/tiles/..., on the assumption that the page
// and the API share an origin. In development they do not: the SPA has its own dev server on another port, and
// every other call in this product is made against API_BASE_URL. So the browser asked the dev server for its
// tiles, received the dev server's 404, and drew a broken image in every square. A supplier opening the address
// form saw a grey grid and could place no pin at all.
//
// WHY NOTHING CAUGHT IT. Leaflet has no laid-out container under jsdom, so it never asks for a tile, and the
// accessibility sweep mocks every /api/v1 call, so tiles never load there either. The component's own loading
// path was tested nowhere, and the first person to see the failure was the person trying to use it.
//
// SO THIS TESTS THE ADDRESS RATHER THAN THE PICTURE. Whether a tile renders is a question about a real browser
// with a real layout; whether the address points at the API is a question about a string, and it is the part
// that was wrong. The subclass is asked to build a URL the way Leaflet does, and the answer must be absolute
// and carry the API's origin - a path beginning /api is the exact bug this file exists for.
//
// THE TOKEN GOES IN A HEADER, which is the reason the tiles are fetched rather than handed to an <img src>: our
// route requires a signed-in caller and an image tag cannot carry an Authorization header. A version of this
// that quietly stopped sending it would load nothing and look identical to a network fault.

import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { API_BASE_URL } from '../../api/auth'
import { useAuthStore } from '../../lib/authStore'
import { createTileLayer } from './MapPicker'

// Leaflet takes the zoom for a URL from the layer's own state rather than from the coordinates handed to it,
// and that state is set when the layer joins a map. There is no map here - jsdom gives Leaflet nothing to lay
// out - so the zoom is set directly. Without it every URL reads .../NaN/38/26.png, which is a fact about this
// test rather than about the component.
function layerAtZoom(zoom: number) {
  const layer = createTileLayer()
  ;(layer as unknown as { _tileZoom: number })._tileZoom = zoom
  return layer
}

describe('the map picker tile layer', () => {
  const originalFetch = globalThis.fetch

  beforeEach(() => {
    useAuthStore.setState({ accessToken: 'test-token' } as never)
  })

  afterEach(() => {
    globalThis.fetch = originalFetch
    vi.restoreAllMocks()
  })

  it('asks the API for tiles, not the page it is served from', () => {
    const layer = layerAtZoom(6)

    const url = layer.getTileUrl({ x: 38, y: 26, z: 6 })

    expect(url.startsWith(API_BASE_URL)).toBe(true)
    expect(url).toBe(`${API_BASE_URL}/api/v1/map/tiles/6/38/26.png`)
  })

  it('sends the session token, because an image tag cannot carry one', async () => {
    const calls: { url: string; headers: Record<string, string> }[] = []

    globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      calls.push({
        url: String(input),
        headers: (init?.headers ?? {}) as Record<string, string>,
      })
      return new Response(new Blob(), { status: 200 })
    }) as typeof fetch

    const layer = layerAtZoom(6)
    layer.createTile({ x: 38, y: 26, z: 6 } as never, () => {})

    await vi.waitFor(() => expect(calls).toHaveLength(1))

    expect(calls[0].url).toBe(`${API_BASE_URL}/api/v1/map/tiles/6/38/26.png`)
    expect(calls[0].headers.Authorization).toBe('Bearer test-token')
  })
})
