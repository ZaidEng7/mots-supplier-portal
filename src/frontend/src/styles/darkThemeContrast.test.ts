import { describe, expect, it } from 'vitest'
import { hex } from 'wcag-contrast'

/**
 * Task #21/MSP-72 follow-up: the light theme's contrast audit (task #14) never checked dark mode
 * - a theme that looks fine can still fail the actual ratio math. Real math (wcag-contrast,
 * tmcw's well-known implementation), not eyeballing, and a permanent regression test rather than
 * a one-off script - a color value can drift back below AA in a later edit with nothing to catch
 * it otherwise.
 *
 * <p>Standalone from storybook-axe / the Playwright a11y suite on purpose: task #23 tracks that
 * setup's own flakiness separately, and this check must not inherit it.</p>
 *
 * <p><b>Scope.</b> Every custom property the .theme-dark / [data-theme="dark"] block in
 * tokens.css redefines - the ONLY place in the whole frontend that defines dark-theme colors
 * (grep for "theme-dark|data-theme=.dark" across src/ matches exactly this one file). Semantic
 * text/link/brand/border tokens are designed to compose with any surface background (tokens.css's
 * own "two-layer model" comment), so the denominator is the full cross product of {token} x
 * {surface it can realistically sit on}, not just today's component instances - a sample would
 * under-count exactly the pairs a future component is free to use. Semi-transparent backgrounds
 * (bg-selected, the status *-bg tokens) are alpha-composited onto the real surface beneath them
 * before measuring - un-flattened rgba has no defined contrast ratio against anything.</p>
 */

const dark = {
  bgApp: '#1C1B19', bgSurface: '#26241F', bgSunken: '#1F1D19', bgInset: '#201E1A', bgHover: '#2F2C26',
  bgSelected: 'rgba(31, 128, 105, 0.22)', bgOverlay: 'rgba(0, 0, 0, 0.60)',

  textPrimary: '#F4F1EC', textSecondary: '#C4BDB1', textMuted: '#9C9588', textDisabled: '#6B6357',
  textInverse: '#FFFFFF', textLink: '#6FBAA8', textBrand: '#6FBAA8',

  border: '#787061', borderStrong: '#827A6A', borderFocus: '#6FBAA8', borderInput: '#827A6A',

  brandSolid: '#1F8069', brandSubtle: 'rgba(31, 128, 105, 0.16)',

  successFg: '#6FCF97', successBg: 'rgba(30,135,75,0.16)', successSolid: '#1E874B',
  warningFg: '#E3B15C', warningBg: 'rgba(183,121,31,0.16)', warningSolid: '#B7791F',
  dangerFg: '#E8897E', dangerBg: 'rgba(192,57,43,0.18)', dangerSolid: '#CD3D2E',
  infoFg: '#7FB0E0', infoBg: 'rgba(37,99,166,0.18)', infoSolid: '#2B73C1',
  goldFg: '#E0BE6E', goldSolid: '#C8A045',
}

function flatten(rgbaOrHex: string, backgroundHex: string): string {
  const m = /rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+))?\)/.exec(rgbaOrHex)
  if (!m) return rgbaOrHex
  const [, r, g, b, aStr] = m
  const a = aStr === undefined ? 1 : parseFloat(aStr)
  const bg = hexToRgb(backgroundHex)
  const blend = (fg: number, bgc: number) => Math.round(fg * a + bgc * (1 - a))
  return rgbToHex(blend(+r, bg.r), blend(+g, bg.g), blend(+b, bg.b))
}
function hexToRgb(h: string) {
  const n = h.replace('#', '')
  return { r: parseInt(n.slice(0, 2), 16), g: parseInt(n.slice(2, 4), 16), b: parseInt(n.slice(4, 6), 16) }
}
function rgbToHex(r: number, g: number, b: number): string {
  return '#' + [r, g, b].map((v) => v.toString(16).padStart(2, '0')).join('')
}

interface Pair { label: string; fg: string; bg: string; threshold: number; exempt?: boolean }

const surfaces: Record<string, string> = {
  'bg-app': dark.bgApp, 'bg-surface': dark.bgSurface, 'bg-sunken': dark.bgSunken,
  'bg-inset': dark.bgInset, 'bg-hover': dark.bgHover,
}
const textTokens: Record<string, string> = {
  'text-primary': dark.textPrimary, 'text-secondary': dark.textSecondary,
  'text-muted': dark.textMuted, 'text-disabled': dark.textDisabled,
  'text-link': dark.textLink, 'text-brand': dark.textBrand,
}

const pairs: Pair[] = []

for (const [tName, tVal] of Object.entries(textTokens)) {
  for (const [sName, sVal] of Object.entries(surfaces)) {
    pairs.push({
      label: `${tName} on ${sName}`, fg: tVal, bg: sVal, threshold: 4.5,
      exempt: tName === 'text-disabled', // WCAG 1.4.3: disabled/inactive UI text is exempt
    })
  }
}

for (const [bName, bVal] of [
  ['border', dark.border], ['border-strong', dark.borderStrong],
  ['border-focus', dark.borderFocus], ['border-input', dark.borderInput],
] as const) {
  for (const [sName, sVal] of [['bg-app', dark.bgApp], ['bg-surface', dark.bgSurface]] as const) {
    pairs.push({ label: `${bName} on ${sName} (UI component)`, fg: bVal, bg: sVal, threshold: 3 })
  }
}

pairs.push({ label: 'brand-solid on bg-app (UI component)', fg: dark.brandSolid, bg: dark.bgApp, threshold: 3 })
pairs.push({ label: 'text-inverse on brand-solid (primary button text)', fg: dark.textInverse, bg: dark.brandSolid, threshold: 4.5 })
pairs.push({ label: 'text-inverse on danger-solid (danger button text)', fg: dark.textInverse, bg: dark.dangerSolid, threshold: 4.5 })

for (const [name, fg, bgRaw] of [
  ['success', dark.successFg, dark.successBg], ['warning', dark.warningFg, dark.warningBg],
  ['danger', dark.dangerFg, dark.dangerBg], ['info', dark.infoFg, dark.infoBg],
] as const) {
  for (const [sName, sVal] of [['bg-surface', dark.bgSurface], ['bg-app', dark.bgApp]] as const) {
    pairs.push({ label: `${name}-fg on flatten(${name}-bg over ${sName})`, fg, bg: flatten(bgRaw, sVal), threshold: 4.5 })
  }
}

for (const [name, val] of [
  ['success-solid', dark.successSolid], ['warning-solid', dark.warningSolid],
  ['danger-solid', dark.dangerSolid], ['info-solid', dark.infoSolid], ['gold-solid', dark.goldSolid],
] as const) {
  pairs.push({ label: `${name} on bg-surface (UI component)`, fg: val, bg: dark.bgSurface, threshold: 3 })
}

for (const [sName, sVal] of [['bg-surface', dark.bgSurface], ['bg-app', dark.bgApp]] as const) {
  pairs.push({ label: `gold-fg on ${sName}`, fg: dark.goldFg, bg: sVal, threshold: 4.5 })
}

pairs.push({ label: 'text-brand on flatten(brand-subtle over bg-surface)', fg: dark.textBrand, bg: flatten(dark.brandSubtle, dark.bgSurface), threshold: 4.5 })
pairs.push({ label: 'text-primary on flatten(bg-selected over bg-surface)', fg: dark.textPrimary, bg: flatten(dark.bgSelected, dark.bgSurface), threshold: 4.5 })

describe('dark theme contrast', () => {
  it('checks a real, non-trivial denominator of color pairs', () => {
    // The count itself is asserted so a future edit that quietly drops pairs (rather than fixing
    // them) fails loudly instead of passing over a shrunken set - the exact pathology this
    // session keeps finding in other "denominator" checks across the codebase.
    expect(pairs.length).toBe(58)
  })

  it.each(pairs.filter((p) => !p.exempt))('$label meets WCAG AA ($threshold:1)', ({ fg, bg, threshold }) => {
    expect(hex(fg, bg)).toBeGreaterThanOrEqual(threshold)
  })
})
