// A theme the token layer defines is a theme something can turn on, or the fact that nothing can is written down.
//
// The defect this closes. tokens.css carries a complete dark palette - surfaces, text, borders, status colours, the navigation
// field, elevation tuned for a dark ground - and this redesign rewrote it in Phase A. themeContrast checks eighty-three colour
// pairs in it, and a pre-existing guard checked fifty-eight before that. And no user of this product had ever seen a single one
// of those colours, because nothing in the application applied .theme-dark or [data-theme="dark"]: not a component, not a
// stylesheet, not a media query. One Storybook story was the only thing in the repository that ever had.
//
// That is the same shape as every other defect this effort has found - an instrument measuring something that does not ship -
// except that here the instruments were right and the product was missing. Nothing failed. A dark theme that is never applied
// looks exactly like a dark theme nobody has got round to switching on, and the difference is a decision nobody recorded.
//
// What this test does: it refuses to let that be silent again. Either the application applies the theme somewhere, or the note
// beside it says why not, in a sentence a reader can disagree with - and a shrug is not a reason.
//
// That note is NULL now, because the theme is reachable: tokens.css binds it to prefers-color-scheme, so the portal follows
// the setting the reader already made in their operating system. It is left in place rather than deleted with the note, so
// that if a future change removes the binding the assertion demands a written reason again rather than letting the theme go
// quiet a second time. Its type is widened deliberately, because `const ... = null` narrows to the null type and the branch
// that demands a reason would stop compiling with it.
//
// THE SWEPT SET is everything a browser could load, minus the tests and stories that are allowed to force a theme: a story
// renders a component in isolation for a designer to look at, and is not the application. Source is read with COMMENTS
// REMOVED, because prose describing the theme is not an application of it - and the definition itself is not an application
// either, though a media query in tokens.css WOULD be.
//
// The denominator comes first: a sweep that read nothing would report "nothing applies the theme" and be technically right for
// the wrong reason, which is the failure mode this file is about. The last test is revert-to-red - if the detector matched
// nothing at all it would report every future theme switch as absent, and the note would stand for ever over a theme that had
// quietly shipped - and it checks the story is correctly outside the swept set, so it cannot satisfy the rule on its own.

import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'

const SRC = resolve(process.cwd(), 'src')
const TOKENS = join(SRC, 'styles/tokens.css')

const UNREACHABLE_BECAUSE = null as string | null

function shippedFiles(): string[] {
  const out: string[] = []
  const walk = (dir: string) => {
    for (const entry of readdirSync(dir)) {
      const full = join(dir, entry)
      if (statSync(full).isDirectory()) { walk(full); continue }
      if (!/\.(tsx?|css|html)$/.test(entry)) continue
      if (/\.(test|stories)\.tsx?$/.test(entry)) continue
      out.push(full)
    }
  }
  walk(SRC)
  return out
}

function code(file: string): string {
  return readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '')
}

function switchesTheme(file: string): boolean {
  const text = code(file)
  if (file === TOKENS) {
    return /@media[^{]*prefers-color-scheme:\s*dark/.test(text)
  }
  return /classList[^\n]*theme-dark|className[^\n]*theme-dark|data-theme/.test(text)
    || /@media[^{]*prefers-color-scheme:\s*dark/.test(text)
}

const FILES = shippedFiles()
const APPLIERS = FILES.filter(switchesTheme)

describe('the dark theme is reachable, or its unreachability is on the record', () => {
  it('sweeps the shipped source, not a corner of it', () => {
    expect(FILES.length).toBeGreaterThan(150)
    expect(FILES).toContain(TOKENS)
    expect(readFileSync(TOKENS, 'utf8')).toContain('.theme-dark')
  })

  it('either something applies it, or the reason it does not is written down', () => {
    if (APPLIERS.length > 0) {
      expect(
        UNREACHABLE_BECAUSE,
        `${APPLIERS.length} file(s) now apply the dark theme, so the note explaining why nothing does is stale - delete it`,
      ).toBeNull()
      return
    }

    expect(
      UNREACHABLE_BECAUSE,
      'the token layer defines a dark theme that nothing turns on: ship a way in, or say why not',
    ).not.toBeNull()
    expect(UNREACHABLE_BECAUSE?.length ?? 0).toBeGreaterThan(120)
  })

  it('the check can fail', () => {
    expect(switchesTheme(join(SRC, 'components/ui/Skeleton.stories.tsx'))).toBe(true)
    expect(switchesTheme(join(SRC, 'components/ui/Card.tsx'))).toBe(false)
    expect(FILES).not.toContain(join(SRC, 'components/ui/Skeleton.stories.tsx'))
  })
})
