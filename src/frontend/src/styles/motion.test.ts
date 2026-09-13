// The motion layer is measured against the rule it was written under, and against its own denominator.
//
// Why this file exists at all. The motion tokens shipped with the token layer and almost nothing consumed them, which nothing
// noticed, because no instrument in this repository looked at motion. The lesson from the rest of this suite applies here too:
// a rule that is only written down in a comment is a transcript rather than a guard. Three rules were chosen for this product
// and all three are checkable - every duration stays under the 300ms ceiling a UI animation has, exit is never slower than
// enter, and nothing animates from nothing.
//
// THE DENOMINATOR it asserts: every motion class declared in index.css must be USED by a component. That is the check that
// would have caught the original defect, because a stylesheet can declare a beautiful animation for a class name no element
// carries and a test that only reads the CSS would pass on it forever. So the class list comes out of the stylesheet and the
// consumers are counted in the source tree - neither side is hardcoded here, and adding a fourth animated class without wiring
// it up fails. Four classes carry state-driven animation today: the dialog's overlay and panel, the toast, and the select
// popover.
//
// Comments come out of the source first, and that is not theoretical: the first run of this guard passed while the class had
// been deleted from the Select, because the comment above the deletion still said the word msp-pop. Two other guards in this
// repository have been caught reading their own documentation - a parser that cannot tell a usage from a mention of one is not
// measuring the code. "//" is only treated as a comment when it is not part of a URL scheme.
//
// The duration tokens are READ rather than restated, so re-timing a token re-runs every check. A duration may be
// var(--motion-base) or a bare 150ms - both appear, and both have to be resolvable to a number.
//
// TWO EXCLUSIONS are asserted rather than quietly dropped, because a filter nobody counts is how a guard ends up measuring
// three of nine things and reporting nine passes. `animation: none` is a cancellation rather than an animation: it appears
// twice, both times on the toast while a finger is dragging it, because the element has to follow the pointer exactly and an
// animation competing with that would make the toast lag behind the thing moving it. And an INDEFINITE animation is exempt
// from the 300ms ceiling, because that ceiling is about how long a person waits for a state change to finish while a loading
// shimmer is not a state change - it runs until the data arrives, and a 300ms loop would strobe. The exemption is narrow on
// purpose: it has to declare `infinite`, which a state animation never does, so it is impossible to claim by accident - and
// the check fails if the shimmer ever stops being indefinite without being re-timed.
//
// The ceiling check is deliberately not a token check: a literal is allowed, and two of these are literals on purpose. What is
// not allowed is a duration a person has to wait for.
//
// Nothing animates from scale(0), because nothing appears from nothing.
//
// Everything closes faster than it opens, for every pair: by the time something is closing the user has already decided. Pairs
// are matched BY NAME - msp-x-in against msp-x-out - so a new pair is covered the moment it is written, and a pair that loses
// its exit is reported rather than skipped.
//
// REDUCED MOTION flattens animation and transition durations globally, with !important - which matters here and nowhere else
// in this stylesheet, because these rules have to beat the animation shorthands above them, which are more specific than `*`.
//
// THE LAST GROUP is the screens an officer opens all day, which stay still. The brief for this pass was frequency rather than
// polish: the tender list, the review queue and the tab strip on a tender are opened dozens of times a day by the same
// person, and an animation seen forty times a day is latency. It asserts the ABSENCE, because an absence decided on purpose is
// the thing most likely to be undone by someone who reads it as an omission.
//
// Keyframe bodies are matched by counting braces, because several of these are written on one line.

import { describe, expect, it } from 'vitest'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'

const FRONTEND = process.cwd()
const css = readFileSync(resolve(FRONTEND, 'src/index.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')
const tokens = readFileSync(resolve(FRONTEND, 'src/styles/tokens.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')

const durations = Object.fromEntries(
  [...tokens.matchAll(/(--motion-[a-z]+)\s*:\s*(\d+)ms/g)].map((m) => [m[1], Number(m[2])]),
)

function ms(value: string): number {
  const token = /var\((--motion-[a-z]+)\)/.exec(value)
  if (token) {
    const resolved = durations[token[1]]
    if (resolved === undefined) throw new Error(`${value} names a duration token that tokens.css does not declare`)
    return resolved
  }
  const literal = /^(\d+(?:\.\d+)?)ms$/.exec(value.trim())
  if (!literal) throw new Error(`cannot read a duration out of "${value}"`)
  return Number(literal[1])
}

const declarations = [...css.matchAll(/([^{}]+)\{[^{}]*?animation:\s*([^;}]+)[;}]/g)].map(([, selector, shorthand]) => {
  const parts = shorthand.trim().split(/\s+/)
  return { selector: selector.trim(), name: parts[0], duration: parts[1], shorthand: shorthand.trim() }
})

const cancellations = declarations.filter((a) => a.name === 'none')

const indefinite = declarations.filter((a) => a.name !== 'none' && /\binfinite\b/.test(a.shorthand))
const animations = declarations.filter((a) => a.name !== 'none' && !/\binfinite\b/.test(a.shorthand))

function keyframeBlocks(source: string): Array<{ name: string; body: string }> {
  const blocks: Array<{ name: string; body: string }> = []
  const header = /@keyframes\s+([a-z0-9-]+)\s*\{/g
  let match: RegExpExecArray | null
  while ((match = header.exec(source)) !== null) {
    let depth = 1
    let index = header.lastIndex
    while (index < source.length && depth > 0) {
      if (source[index] === '{') depth += 1
      else if (source[index] === '}') depth -= 1
      index += 1
    }
    blocks.push({ name: match[1], body: source.slice(header.lastIndex, index - 1) })
    header.lastIndex = index
  }
  return blocks
}

describe('the motion layer is wired to something', () => {
  const declared = [...new Set([...css.matchAll(/\.(msp-[a-z-]+)\[data-/g)].map((m) => m[1]))]

  const code = (source: string) => source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1')

  const sources: string[] = []
  ;(function walk(dir: string) {
    for (const entry of readdirSync(dir)) {
      const path = join(dir, entry)
      if (statSync(path).isDirectory()) walk(path)
      else if (/\.tsx?$/.test(entry) && !/\.test\.tsx?$/.test(entry)) sources.push(code(readFileSync(path, 'utf8')))
    }
  })(resolve(FRONTEND, 'src'))

  it('finds the animated classes to check', () => {
    expect(declared.length).toBeGreaterThanOrEqual(4)
  })

  it.each(declared)('%s is used by a component', (name: string) => {
    const consumers = sources.filter((source) => new RegExp(`\\b${name}\\b`).test(source)).length
    expect(consumers, `${name} is animated in index.css but no component carries the class`).toBeGreaterThan(0)
  })
})

describe('every animation obeys the rules this product chose', () => {
  it('finds the animations to check, and accounts for the ones it excludes', () => {
    expect(animations.length).toBeGreaterThanOrEqual(8)
    expect(cancellations.map((a) => a.selector).sort()).toEqual([
      ".msp-toast[data-swipe='cancel']",
      ".msp-toast[data-swipe='move']",
    ])
    expect(indefinite.map((a) => a.selector)).toEqual(['.msp-skeleton'])
  })

  it.each(animations.map((a) => [a.selector, a] as const))(
    '%s stays under the 300ms ceiling a UI animation has',
    (_selector, animation) => {
      expect(ms(animation.duration)).toBeLessThanOrEqual(300)
    },
  )

  it('never animates from scale(0), because nothing appears from nothing', () => {
    const keyframes = keyframeBlocks(css)
    expect(keyframes.length).toBeGreaterThanOrEqual(8)
    for (const { name, body } of keyframes) {
      expect(body, `${name} animates from or to a zero scale`).not.toMatch(/scale\(0(?:\.0+)?\)/)
    }
  })

  it('closes faster than it opens, for every pair', () => {
    const byName = new Map(animations.map((a) => [a.name, a]))
    const pairs = [...byName.keys()].filter((name) => name.endsWith('-in'))
    expect(pairs.length).toBeGreaterThanOrEqual(4)
    for (const enter of pairs) {
      const exit = byName.get(`${enter.slice(0, -3)}-out`)
      expect(exit, `${enter} has no matching exit animation`).toBeDefined()
      expect(ms(exit!.duration), `${exit!.name} is slower than ${enter}`).toBeLessThanOrEqual(ms(byName.get(enter)!.duration))
    }
  })
})

describe('reduced motion still reaches all of it', () => {
  it('flattens animation and transition durations globally, with !important', () => {
    const block = /@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{\s*\*,\s*\*::before,\s*\*::after\s*\{([\s\S]*?)\}/.exec(css)
    expect(block, 'the global reduced-motion block is gone').not.toBeNull()
    expect(block![1]).toMatch(/animation-duration:\s*0\.01ms\s*!important/)
    expect(block![1]).toMatch(/transition-duration:\s*0\.01ms\s*!important/)
  })
})

describe('the screens an officer opens all day stay still', () => {
  const stillSurfaces = [
    'src/components/ui/ListScreen.tsx',
    'src/components/ui/Table.tsx',
    'src/components/ui/Card.tsx',
    'src/shells/Sidebar.tsx',
    'src/shells/TopBar.tsx',
  ]

  it.each(stillSurfaces)('%s carries no entrance animation', (path) => {
    const source = readFileSync(resolve(FRONTEND, path), 'utf8')
    expect(source).not.toMatch(/\bmsp-(?:dialog|toast|pop|overlay)\b/)
    expect(source).not.toMatch(/animation:|animate-/)
  })
})
