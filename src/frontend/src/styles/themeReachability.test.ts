import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'

/**
 * A theme the token layer defines is a theme something can turn on, or the fact that nothing can is
 * written down.
 *
 * <p><b>The defect this closes.</b> `tokens.css` carries a complete dark palette - surfaces, text,
 * borders, status colours, the navigation field, elevation tuned for a dark ground. This redesign
 * rewrote it in Phase A. `themeContrast` checks eighty-three colour pairs in it. A pre-existing guard
 * checked fifty-eight before that. And no user of this product has ever seen a single one of those
 * colours, because nothing in the application applies `.theme-dark` or `[data-theme="dark"]`: not a
 * component, not a stylesheet, not a media query. One Storybook story is the only thing in the
 * repository that ever has.</p>
 *
 * <p>That is the same shape as every other defect this effort has found - an instrument measuring
 * something that does not ship - except that here the instruments were right and the product was
 * missing. Nothing failed. A dark theme that is never applied looks exactly like a dark theme nobody
 * has got round to switching on, and the difference is a decision nobody recorded.</p>
 *
 * <p><b>What this test does.</b> It refuses to let that be silent again. Either the application
 * applies the theme somewhere, or {@link UNREACHABLE_BECAUSE} says why not, in a sentence a reader can
 * disagree with. When somebody adds a theme switch or binds the palette to `prefers-color-scheme`,
 * this test passes on the first branch and the note comes out.</p>
 */
const SRC = resolve(process.cwd(), 'src')
const TOKENS = join(SRC, 'styles/tokens.css')

/**
 * Why the dark theme is defined and cannot be reached.
 *
 * <p>Null, because it is reachable now. It was not for the whole of the redesign: the palette was
 * written, re-stepped and guarded pair-by-pair while nothing in the product applied it, and a Rams
 * audit scored the environmental-friendliness principle at zero on that single fact. `tokens.css` now
 * binds it to `prefers-color-scheme`, so the portal follows the setting the reader already made in
 * their operating system.</p>
 *
 * <p>Left in place rather than deleted with the note: if a future change removes that binding, the
 * assertion below demands a written reason again rather than letting the theme go quiet a second
 * time.</p>
 */
// Widened deliberately: a `const … = null` narrows to the null type, and the branch below - the one
// that demands a written reason if the binding is ever removed - would stop compiling with it.
const UNREACHABLE_BECAUSE = null as string | null

/** Everything a browser could load, minus the tests and stories that are allowed to force a theme. */
function shippedFiles(): string[] {
  const out: string[] = []
  const walk = (dir: string) => {
    for (const entry of readdirSync(dir)) {
      const full = join(dir, entry)
      if (statSync(full).isDirectory()) { walk(full); continue }
      if (!/\.(tsx?|css|html)$/.test(entry)) continue
      // A story renders a component in isolation for a designer to look at; it is not the application.
      if (/\.(test|stories)\.tsx?$/.test(entry)) continue
      out.push(full)
    }
  }
  walk(SRC)
  return out
}

/** Source with comments removed: prose describing the theme is not an application of it. */
function code(file: string): string {
  return readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '')
}

/** Anything that would actually put a reader in the dark theme. */
function switchesTheme(file: string): boolean {
  const text = code(file)
  if (file === TOKENS) {
    // The definition itself is not an application of it. A media query in this file WOULD be.
    return /@media[^{]*prefers-color-scheme:\s*dark/.test(text)
  }
  return /classList[^\n]*theme-dark|className[^\n]*theme-dark|data-theme/.test(text)
    || /@media[^{]*prefers-color-scheme:\s*dark/.test(text)
}

const FILES = shippedFiles()
const APPLIERS = FILES.filter(switchesTheme)

describe('the dark theme is reachable, or its unreachability is on the record', () => {
  it('sweeps the shipped source, not a corner of it', () => {
    // The denominator. A sweep that read nothing would report "nothing applies the theme" and be
    // technically right for the wrong reason - which is the failure mode this file is about.
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
    // A shrug is not a reason. The point of this note is that somebody can argue with it.
    expect(UNREACHABLE_BECAUSE?.length ?? 0).toBeGreaterThan(120)
  })

  it('the check can fail', () => {
    // Revert-to-red. If the detector matched nothing at all it would report every future theme switch
    // as absent, and this note would stand for ever over a theme that had quietly shipped.
    expect(switchesTheme(join(SRC, 'components/ui/Skeleton.stories.tsx'))).toBe(true)
    expect(switchesTheme(join(SRC, 'components/ui/Card.tsx'))).toBe(false)
    // And the story is correctly outside the swept set, so it cannot satisfy the rule on its own.
    expect(FILES).not.toContain(join(SRC, 'components/ui/Skeleton.stories.tsx'))
  })
})
