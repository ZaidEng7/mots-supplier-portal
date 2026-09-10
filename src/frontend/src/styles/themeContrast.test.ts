import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

/**
 * Every pair of colours either theme can put together clears its WCAG floor, measured from tokens.css.
 *
 * <p><b>What this replaces, and why.</b> Three guards stood here before: a dark-theme sweep over 58
 * pairs, a light-theme sweep written the same day as the redesign, and a focus-ring check. All three
 * held their colours as literals copied out of tokens.css. So when the redesign re-stepped the palette,
 * the dark sweep went on passing 58 assertions about a theme that no longer shipped, and its own comment
 * had warned about exactly this: it asserted its pair count and never its source. A guard that cannot
 * see the file it is about is not a regression test, it is a transcript.</p>
 *
 * <p><b>What it measures.</b> Values are read out of tokens.css and `var()` indirection is followed
 * inside the theme's own block first, then the `:root` block it inherits from - which is how the
 * cascade resolves them, and the reason a dark token that is not overridden keeps its light value. The
 * denominator is the cross product of {token} x {surface it can realistically sit on}, not today's
 * component instances: the two-layer model lets any semantic text token land on any surface, so a
 * sample would under-count exactly the pair a future screen is free to invent. Semi-transparent
 * backgrounds are alpha-composited onto the surface beneath them before measuring, because
 * un-flattened rgba has no contrast ratio against anything.</p>
 *
 * <p><b>What it cannot do.</b> It checks the pairs the design intends. A component that puts together a
 * pair nobody intended is out of reach, and that is what the axe sweep over the routes is for. Two
 * instruments, neither sufficient alone.</p>
 */
const TOKENS = resolve(process.cwd(), 'src/styles/tokens.css')

/**
 * Comments go first, and that is not housekeeping. tokens.css documents its own fixes by quoting the
 * value each one replaced, so a reader looking for `--color-text-muted` finds the prose sentence
 * "--color-text-muted: #948C7E measured 4.18:1" several lines before the real declaration. The first
 * draft of this guard matched that sentence and spent a whole run measuring a colour that was deleted
 * months ago. A parser that cannot tell a declaration from a description of one is the same class of
 * instrument as the guard this file replaced.
 */
const css = readFileSync(TOKENS, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')

/** WCAG 2.2 AA: 4.5:1 for normal text, 3:1 for a UI component boundary or fill (SC 1.4.11, 2.4.7). */
const TEXT_FLOOR = 4.5
const UI_FLOOR = 3

const lightBlock = css.slice(css.indexOf(':root {'), css.indexOf('.theme-dark'))
const darkStart = css.indexOf('.theme-dark')
const darkBlock = css.slice(darkStart, css.indexOf('\n}', darkStart))

/**
 * The literal a token resolves to, searching the given blocks in cascade order and following `var()`
 * from inside whichever block declared it. Returns null rather than a guess, so an unresolvable token
 * shows up as an unchecked token instead of passing quietly.
 */
function lookup(name: string, blocks: readonly string[], depth = 0): string | null {
  if (depth > 8) return null
  for (const block of blocks) {
    const declared = new RegExp(`--${name}\\s*:\\s*([^;]+);`).exec(block)
    if (!declared) continue
    const value = declared[1].trim()
    if (value.startsWith('#') || value.startsWith('rgb')) return value
    const indirect = /var\(\s*--([a-z0-9-]+)/i.exec(value)
    return indirect ? lookup(indirect[1], blocks, depth + 1) : null
  }
  return null
}

function channel(c: number): number {
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
}

function rgb(colour: string): readonly [number, number, number] {
  const fn = /rgba?\(\s*(\d+)[\s,]+(\d+)[\s,]+(\d+)/.exec(colour)
  if (fn) return [+fn[1], +fn[2], +fn[3]]
  const h = colour.replace('#', '')
  return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16)) as unknown as [number, number, number]
}

function luminance(colour: string): number {
  const [r, g, b] = rgb(colour).map((v) => channel(v / 255))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}

/** A wash is a colour only once it is over something. Composite it before measuring, never after. */
function over(colour: string, background: string): string {
  const alpha = /rgba?\([^)]*?,\s*([\d.]+)\s*\)/.exec(colour)
  if (!alpha) return colour
  const a = parseFloat(alpha[1])
  const [fr, fg, fb] = rgb(colour)
  const [br, bg, bb] = rgb(background)
  const blend = (f: number, b: number) => Math.round(f * a + b * (1 - a))
  return `#${[blend(fr, br), blend(fg, bg), blend(fb, bb)].map((v) => v.toString(16).padStart(2, '0')).join('')}`
}

/** The ring is the outer colour of the --focus-ring box-shadow, which is the one a user sees. */
function ringColour(blocks: readonly string[]): string | null {
  for (const block of blocks) {
    const declared = /--focus-ring\s*:\s*([^;]+);/.exec(block)
    if (!declared) continue
    const vars = [...declared[1].matchAll(/var\(\s*--([a-z0-9-]+)/g)]
    const last = vars.at(-1)
    return last ? lookup(last[1], blocks) : null
  }
  return null
}

interface Pair {
  readonly label: string
  readonly fg: string
  readonly bg: string
  readonly floor: number
}

const SURFACES = ['color-bg-surface', 'color-bg-app', 'color-bg-sunken', 'color-bg-inset', 'color-bg-hover']

/**
 * WCAG 1.4.3 exempts inactive controls, so --color-text-disabled is listed and measured but not held to
 * the floor: its job is to read as unavailable. It is in the denominator so a future edit that makes it
 * the body-text colour by mistake is still visible in the output.
 */
const EXEMPT = new Set(['color-text-disabled'])

function pairsFor(blocks: readonly string[]): Pair[] {
  const value = (name: string) => {
    const resolved = lookup(name, blocks)
    if (resolved === null) throw new Error(`tokens.css does not resolve --${name}`)
    return resolved
  }
  const pairs: Pair[] = []
  const push = (label: string, fg: string, bg: string, floor: number) => pairs.push({ label, fg, bg, floor })

  // Body text, on every surface the two-layer model lets it land on.
  for (const text of ['color-text-primary', 'color-text-secondary', 'color-text-muted', 'color-text-disabled']) {
    for (const surface of SURFACES) {
      push(`${text} on ${surface}`, value(text), value(surface), EXEMPT.has(text) ? 0 : TEXT_FLOOR)
    }
  }

  // Brand text. The app background is the binding case and not white: the step that shipped on the day
  // this guard was written cleared white at 4.83 and missed --color-bg-app by three thousandths.
  for (const text of ['color-text-link', 'color-text-brand']) {
    for (const surface of ['color-bg-surface', 'color-bg-app', 'color-bg-hover']) {
      push(`${text} on ${surface}`, value(text), value(surface), TEXT_FLOOR)
    }
    for (const wash of ['color-accent-wash', 'color-brand-subtle', 'color-bg-selected']) {
      push(`${text} on ${wash} over bg-surface`, value(text), over(value(wash), value('color-bg-surface')), TEXT_FLOOR)
    }
  }

  // Button labels on the fills they actually sit on, each with the ink that belongs to its own fill.
  // A pressed state is read as often as a resting one, so it is in the denominator too.
  for (const fill of ['color-brand-solid', 'color-brand-solid-hover', 'color-brand-solid-active']) {
    push(`color-on-brand on ${fill}`, value('color-on-brand'), value(fill), TEXT_FLOOR)
  }
  push('color-text-inverse on color-danger-solid', value('color-text-inverse'), value('color-danger-solid'), TEXT_FLOOR)

  // Status text, over its own wash composited onto each surface that wash can appear on.
  for (const status of ['success', 'warning', 'danger', 'info']) {
    for (const surface of ['color-bg-surface', 'color-bg-app']) {
      push(
        `color-${status}-fg on ${status}-bg over ${surface}`,
        value(`color-${status}-fg`),
        over(value(`color-${status}-bg`), value(surface)),
        TEXT_FLOOR,
      )
      push(`color-${status}-fg on ${surface}`, value(`color-${status}-fg`), value(surface), TEXT_FLOOR)
    }
  }
  for (const surface of ['color-bg-surface', 'color-bg-app']) {
    push('color-gold-fg on ' + surface, value('color-gold-fg'), value(surface), TEXT_FLOOR)
  }

  // The navigation field, which is dark in both themes and so has its own foregrounds.
  for (const fieldSurface of ['color-field', 'color-field-raised', 'color-field-active']) {
    push(`color-on-field on ${fieldSurface}`, value('color-on-field'), value(fieldSurface), TEXT_FLOOR)
  }
  for (const fieldSurface of ['color-field', 'color-field-raised']) {
    push(`color-on-field-muted on ${fieldSurface}`, value('color-on-field-muted'), value(fieldSurface), TEXT_FLOOR)
  }
  // The back-office chrome, which is dark in both themes, so both themes measure the same bar. The
  // wordmark is here because it was not: it read a primitive directly, so re-stepping that primitive
  // moved a shipped colour to 4.25:1 and the only instrument that noticed was the axe sweep, 86 times.
  for (const text of ['color-chrome-text', 'color-chrome-accent']) {
    for (const surface of ['color-chrome-surface', 'color-chrome-bg']) {
      push(`${text} on ${surface}`, value(text), value(surface), TEXT_FLOOR)
    }
  }

  // Boundaries. An input whose edge cannot be seen against the page is an input somebody cannot find,
  // and this product shipped one at 1.53:1 until the redesign, because nothing computed it.
  //
  // --color-border and --color-accent-line are deliberately NOT here. SC 1.4.11 asks 3:1 of a boundary
  // that is needed to identify a control, and those two draw card edges, table rules and the sidebar's
  // accent hairline - separators, which identify nothing and would have to be near-black to pass. The
  // tokens that do identify a control are -strong (the secondary button's outline, the Stepper's marker)
  // and -input, and both answer for it here.
  for (const border of ['color-border-strong', 'color-border-input', 'color-border-focus']) {
    for (const surface of ['color-bg-surface', 'color-bg-app']) {
      push(`${border} on ${surface} (boundary)`, value(border), value(surface), UI_FLOOR)
    }
  }

  // Solid swatches used as dots, icons and fills carry meaning, so they are components, not decoration.
  for (const solid of ['color-brand-solid', 'color-success-solid', 'color-warning-solid', 'color-danger-solid', 'color-info-solid', 'color-gold-solid']) {
    for (const surface of ['color-bg-surface', 'color-bg-app']) {
      push(`${solid} on ${surface} (fill)`, value(solid), value(surface), UI_FLOOR)
    }
  }

  // The focus ring: SC 2.4.7 is not satisfied by an indicator existing, only by a visible one.
  const ring = ringColour(blocks)
  if (ring === null) throw new Error('tokens.css does not resolve the --focus-ring colour')
  for (const surface of ['color-bg-surface', 'color-bg-app']) {
    push(`focus ring on ${surface}`, ring, value(surface), UI_FLOOR)
  }

  return pairs
}

const THEMES = [
  ['light', [lightBlock]],
  ['dark', [darkBlock, lightBlock]],
] as const

describe.each(THEMES)('%s theme contrast', (_theme, blocks) => {
  const pairs = pairsFor(blocks)

  it('reads a real, non-trivial denominator out of tokens.css', () => {
    // The count AND the source. The guard this replaced asserted only the count, so it kept passing 58
    // assertions after the palette it had copied stopped shipping.
    expect(blocks[0].length).toBeGreaterThan(1000)
    expect(pairs.length).toBe(83)
    expect(pairs.filter((p) => p.floor === UI_FLOOR).length).toBe(20)
    expect(pairs.every((p) => /^#|^rgb/.test(p.fg) && /^#|^rgb/.test(p.bg))).toBe(true)
  })

  it('puts every pair above its floor', () => {
    const failures = pairs
      .filter((p) => p.floor > 0 && contrast(p.fg, p.bg) < p.floor)
      .map((p) => `${p.label} is ${contrast(p.fg, p.bg).toFixed(2)}:1, floor ${p.floor}`)

    expect(failures, 'WCAG 2.2 AA: 4.5:1 for text, 3:1 for a boundary or a meaningful fill').toEqual([])
  })
})

describe('the contrast arithmetic itself', () => {
  it('can fail, in the shape the real defect took', () => {
    // Revert-to-red. The brand step cleared white at 4.83 and missed the app background by three
    // thousandths, because the page is --n-50 and nobody had checked a link against the page it sits on.
    expect(contrast('#118164', '#F6F7F6')).toBeLessThan(TEXT_FLOOR)
    expect(contrast('#107A5E', '#F6F7F6')).toBeGreaterThanOrEqual(TEXT_FLOOR)
  })

  it('agrees with two ratios that are not a matter of opinion', () => {
    expect(contrast('#000000', '#FFFFFF')).toBeCloseTo(21, 1)
    expect(contrast('#777777', '#777777')).toBeCloseTo(1, 5)
  })

  it('composites a wash before measuring it, not after', () => {
    // A 22% green over the dark surface is a dark green, and measuring the un-composited rgba would
    // quietly compare a colour against nothing.
    expect(over('rgba(47, 160, 128, 0.22)', '#1C2421')).toBe('#203f36')
    expect(over('#107A5E', '#FFFFFF')).toBe('#107A5E')
  })
})
