// A map for placing a supplier's address on the ground, and the pair of number fields that are its
// keyboard-accessible half.
//
// THE NUMBERS ARE NOT A FALLBACK, they are the control. A dragged pin cannot be operated with a keyboard or
// described to a screen reader, and this product runs axe over every screen. So latitude and longitude stay
// ordinary inputs that anyone can type into, the map writes into them, and they write back to the map. Whoever
// cannot use the map has lost a convenience rather than the field.
//
// It also means the form still works when the map does not. Tiles come over the network; a supplier on a bad
// connection, or one whose network blocks the proxy, sees an empty grey square and two inputs that work.
//
// TILES ARE FETCHED WITH THE SESSION'S TOKEN, which is why this subclasses Leaflet's TileLayer instead of
// handing it a URL. Our tile route requires a signed-in user, the token lives in an Authorization header, and
// an <img src> cannot carry one - Leaflet's own loading would have sent an unauthenticated request and rendered
// 401 bodies as broken images. Each tile is fetched, turned into a blob URL, and revoked when Leaflet discards
// the tile, so a long panning session does not leak object URLs.
//
// THE MAP OPENS OVER SYRIA when there is nothing to show yet, at a zoom where the whole country is visible.
// Opening on a pin at the equator, which is what an unset 0,0 produces, would put every supplier in the Gulf of
// Guinea and invite them to drag from there.
//
// CLICKING PLACES THE PIN, and dragging it moves it. Both write six decimal places, which is about a tenth of a
// metre - far past what a pin dropped by hand is worth, but the ministry's field is decimal(9,6) and rounding
// further would be us deciding their precision for them.
//
// THE ATTRIBUTION IS NOT DECORATION. OpenStreetMap's data is under ODbL and the licence requires the credit be
// shown wherever the map is. It stays whether the tiles load or not.

import { useEffect, useId, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useAuthStore } from '../../lib/authStore'
import { Field } from './Field'
import { Input } from './Input'

const SYRIA_CENTRE: L.LatLngTuple = [34.8, 38.0]
const SYRIA_ZOOM = 6
const PLACED_ZOOM = 14

const AuthenticatedTileLayer = L.TileLayer.extend({
  createTile(coords: L.Coords, done: L.DoneCallback) {
    const tile = document.createElement('img')
    tile.alt = ''

    const url = this.getTileUrl(coords)
    const token = useAuthStore.getState().accessToken

    fetch(url, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
      .then((res) => (res.ok ? res.blob() : Promise.reject(new Error(String(res.status)))))
      .then((blob) => {
        const objectUrl = URL.createObjectURL(blob)
        tile.dataset.objectUrl = objectUrl
        tile.src = objectUrl
        done(undefined, tile)
      })
      .catch((error: Error) => done(error, tile))

    return tile
  },

  _removeTile(this: L.TileLayer, key: string) {
    const layer = this as unknown as {
      _tiles: Record<string, { el: HTMLImageElement } | undefined>
    }
    const objectUrl = layer._tiles[key]?.el?.dataset?.objectUrl
    if (objectUrl) URL.revokeObjectURL(objectUrl)
    const base = L.TileLayer.prototype as unknown as { _removeTile: (key: string) => void }
    base._removeTile.call(this, key)
  },
})

export function MapPicker({
  latitude,
  longitude,
  onChange,
  disabled,
  latitudeError,
  longitudeError,
}: Readonly<{
  latitude: string
  longitude: string
  onChange: (latitude: string, longitude: string) => void
  disabled?: boolean
  latitudeError?: string
  longitudeError?: string
}>) {
  const { t } = useTranslation()
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<L.Map | null>(null)
  const markerRef = useRef<L.Marker | null>(null)
  const onChangeRef = useRef(onChange)
  const mapId = useId()

  onChangeRef.current = onChange

  useEffect(() => {
    const container = containerRef.current
    if (!container || mapRef.current) return

    const map = L.map(container, { attributionControl: true }).setView(SYRIA_CENTRE, SYRIA_ZOOM)
    mapRef.current = map

    const layer = new (AuthenticatedTileLayer as unknown as new (url: string, options: L.TileLayerOptions) => L.TileLayer)(
      '/api/v1/map/tiles/{z}/{x}/{y}.png',
      { maxZoom: 19, minZoom: 3, attribution: '&copy; OpenStreetMap contributors' },
    )
    layer.addTo(map)

    map.on('click', (event: L.LeafletMouseEvent) => {
      onChangeRef.current(event.latlng.lat.toFixed(6), event.latlng.lng.toFixed(6))
    })

    return () => {
      map.remove()
      mapRef.current = null
      markerRef.current = null
    }
  }, [])

  useEffect(() => {
    const map = mapRef.current
    if (!map) return

    const lat = Number(latitude)
    const lng = Number(longitude)
    const placed = latitude.trim() !== '' && longitude.trim() !== ''
      && Number.isFinite(lat) && Number.isFinite(lng)
      && Math.abs(lat) <= 90 && Math.abs(lng) <= 180

    if (!placed) {
      markerRef.current?.remove()
      markerRef.current = null
      return
    }

    if (markerRef.current) {
      markerRef.current.setLatLng([lat, lng])
      return
    }

    const marker = L.marker([lat, lng], { draggable: !disabled }).addTo(map)
    marker.on('dragend', () => {
      const position = marker.getLatLng()
      onChangeRef.current(position.lat.toFixed(6), position.lng.toFixed(6))
    })
    markerRef.current = marker
    map.setView([lat, lng], Math.max(map.getZoom(), PLACED_ZOOM))
  }, [latitude, longitude, disabled])

  return (
    <div className="flex flex-col gap-3">
      <p className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
        {t('addresses.mapHint')}
      </p>

      <div
        id={mapId}
        ref={containerRef}
        role="application"
        aria-label={t('addresses.mapLabel')}
        style={{ height: '260px', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)' }}
      />

      <div className="grid grid-cols-2 gap-4">
        <Field
          label={t('addresses.fields.latitude')}
          error={latitudeError}
          required
        >
          {(p) => (
            <Input
              dir="ltr"
              inputMode="decimal"
              placeholder="33.513100"
              disabled={disabled}
              value={latitude}
              onChange={(e) => onChange(e.target.value, longitude)}
              {...p}
            />
          )}
        </Field>
        <Field label={t('addresses.fields.longitude')} error={longitudeError} required>
          {(p) => (
            <Input
              dir="ltr"
              inputMode="decimal"
              placeholder="36.292500"
              disabled={disabled}
              value={longitude}
              onChange={(e) => onChange(latitude, e.target.value)}
              {...p}
            />
          )}
        </Field>
      </div>
    </div>
  )
}
