import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'

/**
 * A card is a component, not a set of classes copied between screens.
 *
 * <p><b>What was wrong.</b> `Card` exists and owns §6.7's surface — a token background, a 1px border,
 * `--radius-lg`, the faintest step on the shadow scale, and an `<h2>` title at `--text-h4`. Fifteen
 * panels across four screens re-declared all of that inline, which is the same failure the page heading
 * had: the component exists, most screens ignore it, and the copies drift. One of them had already lost
 * the shadow.</p>
 *
 * <p><b>What this does not forbid.</b> The token itself. A warning banner, a KPI strip and a form are
 * allowed to be rounded surfaces without being cards; what they may not do is reproduce the card's
 * <i>whole</i> signature - the surface background AND the border AND a `--text-h4` heading - because at
 * that point they are a card written out longhand.</p>
 */
const ROUTES = resolve(process.cwd(), 'src/routes')

/** Panels that carry the full signature for a reason. Each entry says why. */
const EXEMPT: Record<string, string> = {}

function routeFiles(dir = ROUTES): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return routeFiles(full)
    if (!entry.endsWith('.tsx') || entry.includes('.test.')) return []
    return [full]
  })
}

/** The card's whole signature: its surface, its border, and a section heading, in one element's vicinity. */
function hasHandRolledCard(source: string): boolean {
  const openings = [...source.matchAll(/className="[^"]*rounded-\[var\(--radius-lg\)\][^"]*p-6"/g)]
  return openings.some((match) => {
    const window = source.slice(match.index, match.index + 600)
    return /var\(--color-bg-surface\)/.test(window)
      && /1px solid var\(--color-border\)/.test(window)
      && /text-\[length:var\(--text-h4\)\]/.test(window)
  })
}

describe('every card comes from the Card component', () => {
  const files = routeFiles()

  it('sweeps the route tree it claims to', () => {
    expect(files.length).toBeGreaterThanOrEqual(60)
  })

  it('no screen writes a card out longhand', () => {
    const handRolled = files
      .filter((file) => hasHandRolledCard(readFileSync(file, 'utf8')))
      .map((file) => relative(ROUTES, file))
      .filter((name) => !(name in EXEMPT))

    expect(handRolled, 'these reproduce Card\'s whole signature inline, and the copies drift').toEqual([])
  })

  it('the check can fail', () => {
    const longhand = `<div className="rounded-[var(--radius-lg)] p-6" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
      <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]">Title</h2>`
    const banner = `<div className="rounded-[var(--radius-lg)] p-6" style={{ backgroundColor: 'var(--warning-50)' }}>`

    expect(hasHandRolledCard(longhand)).toBe(true)
    // A rounded surface that is not pretending to be a card stays allowed.
    expect(hasHandRolledCard(banner)).toBe(false)
  })
})
