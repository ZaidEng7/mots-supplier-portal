import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'

/**
 * Sweep: every design decision in the product comes from the token layer, and every token a component
 * asks for actually exists.
 *
 * <p><b>The defect this closes.</b> `docs/ux/DESIGN-SYSTEM.md` §4 declares spacing, radius, elevation,
 * motion and z-index scales. `tokens.css` shipped colours and type and none of the rest — and 41
 * references across 14 names asked for tokens that were never defined, 37 of them with no fallback. An
 * undefined custom property makes the whole declaration invalid, so `var(--radius-md)` rendered square
 * corners, `var(--text-heading-lg)` rendered at the inherited size, and `var(--color-danger)` dropped the
 * colour entirely. Nothing caught it: TypeScript does not read CSS, the axe suite computes the colours
 * that actually rendered (an inherited colour passes contrast), and a screenshot looks plausible.</p>
 *
 * <p><b>Why this is a test and not a lint rule.</b> The question is not "is this class allowed" but "does
 * this name resolve against the file that defines the names", which needs both halves read together. The
 * exemption lists below are typed out by hand for the reason every other sweep in this repository does it
 * that way: a pattern-matched exemption lets the next instance join it silently.</p>
 */

/** vitest runs from src/frontend, the same anchor `preconditionCoverage.test.ts` uses. */
const SRC = resolve(process.cwd(), 'src')
const TOKEN_FILES = ['styles/tokens.css', 'index.css']

/** Files whose z-index is deliberately container-local rather than a layer of the app. */
const LOCAL_STACKING_CONTEXT: Record<string, string> = {
  'components/ui/Table.tsx':
    'A sticky table header and sticky first column order themselves INSIDE one scroll container. They '
    + 'are not app chrome and must not be lifted onto the app scale, where --z-sticky (100) would put a '
    + 'table header above a page element that is meant to cover it.',
  'components/ui/Skeleton.tsx':
    'The skeleton table mirrors the real one, so its sticky first column carries the same local z-index. '
    + 'Any other value would make the placeholder stack differently from the thing it stands in for.',
}

function sourceFiles(): string[] {
  const out: string[] = []
  const walk = (dir: string) => {
    for (const entry of readdirSync(dir)) {
      const full = join(dir, entry)
      if (statSync(full).isDirectory()) { walk(full); continue }
      if (!/\.tsx?$/.test(entry)) continue
      if (/\.(test|stories)\.tsx?$/.test(entry)) continue
      out.push(full.slice(SRC.length + 1))
    }
  }
  walk(SRC)
  return out.sort()
}

/** Source with comments removed: prose that mentions `shadow-xl` is discussion, not a declaration. */
function code(relative: string): string {
  const text = readFileSync(join(SRC, relative), 'utf8')
  return text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '')
}

/**
 * Components that reach past the semantic layer and read a primitive directly.
 *
 * <p><b>The defect this closes.</b> tokens.css states the rule at the top of the file - components
 * consume semantics only - and nothing enforced it. BackOfficeShell painted the wordmark with
 * `var(--accent-gold-500)`, so when the redesign re-stepped that primitive to fix its ratio against a
 * light page, it silently changed a colour that only ever appears on the dark chrome bar: 5.93:1 became
 * 4.25:1, and the only instrument that noticed was the axe sweep, which failed 86 times and named a hex
 * value rather than a token. A semantic name would have made the pair visible to the contrast guard and
 * the re-step impossible to get wrong.</p>
 *
 * <p><b>Why a list and not a ban.</b> These are real, and fixing them is the component phase's work, not
 * the token layer's. What the token layer can do is stop the list growing. The assertion below is exact
 * equality, so a new instance fails and so does a fixed one - the list is the current truth about this
 * debt, and it has to be edited down as the debt is paid rather than drifting out of date.</p>
 *
 * <p>Every entry is a status pair inlined before the semantic status tokens existed, or a chart fill.
 * They are not contrast failures today: the primitive holds the same value the semantic token points at
 * in the light theme. They are THEME failures - a primitive does not change between themes, so each of
 * these renders light-theme status colours on a dark page.</p>
 */
const PRIMITIVE_DEBT: Record<string, readonly string[]> = {
  'components/AcceptInvitePageBase.tsx': ['success-600'],
  'components/ErpStatusBanner.tsx': ['warning-50', 'warning-600'],
  'components/charts/CoverageChart.tsx': ['brand-200'],
  'components/ui/Badge.tsx': ['danger-50', 'danger-600', 'info-50', 'info-600', 'success-50', 'success-600', 'warning-50', 'warning-600'],
  'components/ui/Button.tsx': ['danger-600'],
  'components/ui/Toast.tsx': ['success-500'],
  'routes/OnboardingPage.tsx': ['info-50', 'info-500', 'info-600', 'success-600', 'warning-50', 'warning-500', 'warning-600'],
  'routes/SettingsPage.tsx': ['success-600'],
  'routes/VerifyEmailPage.tsx': ['success-600'],
  'routes/ministry/MinistryAwardAnalyticsPage.tsx': ['warning-50'],
  'routes/ministry/MinistryRfqDetailPage.tsx': ['warning-50', 'warning-600'],
  'routes/onboarding/AddressesPage.tsx': ['danger-50', 'danger-600', 'warning-50', 'warning-600'],
  'routes/onboarding/BankingPage.tsx': ['danger-50', 'danger-600'],
  'routes/onboarding/ContactsPage.tsx': ['danger-50', 'danger-600'],
  'routes/onboarding/OfferingsPage.tsx': ['warning-50', 'warning-600'],
}

/** The primitive ramps. A semantic token is every other `--color-*` name; these are the raw steps. */
const PRIMITIVE = /var\(--((?:n|brand|success|warning|danger|info|accent-gold)-\d+)\)/g

function primitivesRead(file: string): string[] {
  return [...new Set([...code(file).matchAll(PRIMITIVE)].map((m) => m[1]))].sort()
}

function definedTokens(): Set<string> {
  const names = new Set<string>()
  for (const file of TOKEN_FILES) {
    const css = readFileSync(join(SRC, file), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')
    for (const match of css.matchAll(/(--[a-zA-Z0-9-]+)\s*:/g)) names.add(match[1])
  }
  return names
}

/**
 * Tailwind's own scales are close to this product's but not the same - its `text-sm` is 14px where
 * `--text-body-sm` is 13, its `shadow-sm` a different curve from §4.3's layered pair. Mixing the two is
 * how one screen ends up a step off from the screen beside it.
 */
const BANNED_UTILITIES: [RegExp, string][] = [
  [/(?<![\w-])text-(xs|sm|base|lg|xl|[2-9]xl)(?![\w-])/g, 'use text-[length:var(--text-*)]'],
  [/(?<![\w-])font-(thin|light|normal|medium|semibold|bold|extrabold|black)(?![\w-])/g, 'use font-[var(--fw-*)]'],
  [/(?<![\w-])shadow-(sm|md|lg|xl|2xl|inner)(?![\w-])/g, 'use boxShadow: var(--shadow-*)'],
  [/rounded-\[(?!var\()/g, 'use rounded-[var(--radius-*)]'],
]

/** Literal colours, and z-index outside the one scale. */
const RAW_COLOUR = /#[0-9a-fA-F]{3,8}\b|\brgba?\(/g
const RAW_Z_INDEX = /(?<![\w-])z-\[?\d+\]?(?![\w-])|zIndex:\s*\d/g

const FILES = sourceFiles()

describe('design tokens', () => {
  it('the sweep reads the product, not a handful of files', () => {
    // The denominator, asserted before the rules. An empty file list passes every expectation below
    // while checking nothing - the failure mode this repository has now found in six instruments.
    expect(FILES.length).toBeGreaterThan(100)
    expect(definedTokens().size).toBeGreaterThan(80)
    const references = FILES.reduce((n, f) => n + (code(f).match(/var\(--/g)?.length ?? 0), 0)
    expect(references).toBeGreaterThan(500)
  })

  it('every token a component asks for is defined', () => {
    const defined = definedTokens()
    const missing: string[] = []

    for (const file of FILES) {
      const text = code(file)
      for (const match of text.matchAll(/var\((--[a-zA-Z0-9-]+)\s*(,[^)]*)?\)/g)) {
        if (defined.has(match[1])) continue
        // A fallback makes the declaration valid, so it is a smell rather than a defect - and every
        // one of them was removed in this pass. Reported together so neither class can creep back.
        const line = text.slice(0, match.index).split('\n').length
        missing.push(`${file}:${line}  ${match[1]}${match[2] ? ' (has a fallback)' : ''}`)
      }
    }

    expect(missing, 'these resolve to nothing, so the declaration is dropped and the element renders '
      + 'with the inherited value instead:\n  ' + missing.join('\n  ')).toEqual([])
  })

  it('colours come from the token layer, never from a literal', () => {
    const offenders: string[] = []
    for (const file of FILES) {
      const text = code(file)
      for (const match of text.matchAll(RAW_COLOUR)) {
        const line = text.slice(0, match.index).split('\n').length
        offenders.push(`${file}:${line}  ${match[0]}`)
      }
    }
    expect(offenders, 'a literal colour cannot be themed and was not contrast-checked:\n  '
      + offenders.join('\n  ')).toEqual([])
  })

  it('type, weight and elevation come from the scale, not from Tailwind utilities', () => {
    const offenders: string[] = []
    for (const file of FILES) {
      const text = code(file)
      for (const [rx, hint] of BANNED_UTILITIES) {
        for (const match of text.matchAll(rx)) {
          const line = text.slice(0, match.index).split('\n').length
          offenders.push(`${file}:${line}  ${match[0]} - ${hint}`)
        }
      }
    }
    expect(offenders, offenders.join('\n  ')).toEqual([])
  })

  it('z-index comes from the one scale, so two layers cannot both claim the top', () => {
    // This is what the literals were hiding: the toast viewport and the dialog content were both z-50,
    // so whether a "saved" confirmation appeared above or behind the dialog that produced it came down
    // to DOM order. §4.5 answers it by name - toast 700 over modal 500.
    const offenders: string[] = []
    for (const file of FILES) {
      if (file in LOCAL_STACKING_CONTEXT) continue
      const text = code(file)
      for (const match of text.matchAll(RAW_Z_INDEX)) {
        const line = text.slice(0, match.index).split('\n').length
        offenders.push(`${file}:${line}  ${match[0]}`)
      }
    }
    expect(offenders, 'use zIndex: var(--z-*) from §4.5:\n  ' + offenders.join('\n  ')).toEqual([])
  })

  it('every stacking exemption still names a file that exists and still needs it', () => {
    for (const [file, reason] of Object.entries(LOCAL_STACKING_CONTEXT)) {
      expect(FILES, `${file} is exempted and no longer exists`).toContain(file)
      expect(reason.length, `${file}'s exemption must say why`).toBeGreaterThan(60)
      expect(code(file), `${file} no longer has a local z-index, so its exemption describes the past`)
        .toMatch(/zIndex:\s*\d/)
    }
  })

  it('no component reads a primitive the token layer has not named', () => {
    const found: Record<string, readonly string[]> = {}
    for (const file of FILES) {
      const primitives = primitivesRead(file)
      if (primitives.length > 0) found[file] = primitives
    }

    // Exact, both directions. A new instance is a regression; a fixed one means this list is stale.
    expect(found, 'a primitive read from a component cannot be re-stepped without changing that component')
      .toEqual(PRIMITIVE_DEBT)
  })

  it('the primitive sweep can fail', () => {
    // The control, in the shape of the defect: the wordmark as it was written, and as it is now.
    const offending = `<span style={{ color: 'var(--accent-gold-500)' }} />`
    const conforming = `<span style={{ color: 'var(--color-chrome-accent)' }} />`
    expect(new RegExp(PRIMITIVE.source, 'g').test(offending)).toBe(true)
    expect(new RegExp(PRIMITIVE.source, 'g').test(conforming)).toBe(false)
    // And the fix itself holds: the file that caused it reads no primitive now.
    expect(primitivesRead('shells/BackOfficeShell.tsx')).toEqual([])
  })

  it('the check can fail', () => {
    // The control. Five matchers that never matched anything would make this file green forever, which
    // is the failure this batch found four times over in other sweeps. Run them against a line that
    // deliberately breaks each rule.
    const offending = `
      <div className="text-sm font-semibold shadow-lg rounded-[0.5rem] z-50"
           style={{ color: '#ff0000', background: 'rgba(0,0,0,0.5)', zIndex: 40 }} />
    `
    expect(BANNED_UTILITIES.every(([rx]) => new RegExp(rx.source, 'g').test(offending))).toBe(true)
    expect(new RegExp(RAW_COLOUR.source, 'g').test(offending)).toBe(true)
    expect(new RegExp(RAW_Z_INDEX.source, 'g').test(offending)).toBe(true)

    // And the other direction: the forms this product actually uses must NOT match, or the sweep would
    // fail on conforming code and get exempted into uselessness.
    const conforming = `
      <div className="text-[length:var(--text-body-sm)] font-[var(--fw-semibold)] rounded-[var(--radius-md)] max-w-sm"
           style={{ boxShadow: 'var(--shadow-sm)', zIndex: 'var(--z-modal)' }} />
    `
    expect(BANNED_UTILITIES.some(([rx]) => new RegExp(rx.source, 'g').test(conforming))).toBe(false)
    expect(new RegExp(RAW_COLOUR.source, 'g').test(conforming)).toBe(false)
    expect(new RegExp(RAW_Z_INDEX.source, 'g').test(conforming)).toBe(false)
  })
})
