import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'

/**
 * One page title, one component, one size.
 *
 * <p><b>What the audit measured.</b> `<h1>` rendered at <b>three different sizes</b> for one job — the
 * `--text-h1` token on 5 screens, `--text-h2` on 45 and `--text-h3` on 7 — because 51 screens hand-rolled
 * their own heading and `PageHeading`, used by 6, was itself set to the wrong one. That is principle #3
 * (aesthetic, 1/3) in one line: <i>"no visible system"</i>.</p>
 *
 * <p><b>Why a test and not a tidy.</b> Replacing 51 headings is an afternoon; keeping them replaced is the
 * hard part, and nothing in this repository would have noticed the 52nd. A sweep with no guard is a sweep
 * that comes undone, which is the failure mode this whole audit was about — and the reason every fix in
 * this batch ships with an instrument rather than a promise.</p>
 *
 * <p>Exemptions are hand-written, for the same reason the router guard's are: a pattern-matched exemption
 * is one the next instance joins without anybody deciding.</p>
 */
const ROUTES = resolve(process.cwd(), 'src/routes')
const COMPONENTS = resolve(process.cwd(), 'src/components')

/** The two components allowed to declare an `<h1>`, because between them they are the answer. */
const HEADING_COMPONENTS = ['ui/ListScreen.tsx', 'ui/AuthHeading.tsx']

/**
 * Screens that legitimately render their own `<h1>`. Each entry says why, and an empty list is NOT
 * automatically the healthy state — the point is that every one is a decision somebody took.
 *
 * <p>It is empty today because the case that looked like an exemption turned out to be a second
 * component instead: five screens are centred cards filling the viewport on their own, and forcing a
 * full-width `PageHeading` into a 24rem card gave a title bigger than the card's content. They use
 * `AuthHeading`, which the check below accepts, so nothing needs exempting.</p>
 */
const EXEMPT: Record<string, string> = {}

/**
 * Source with its comments removed.
 *
 * <p>This sweep matched the text of a doc comment that mentioned the tag it forbids, and reported the
 * file as hand-rolling a heading it does not render. Prose about a rule is not a breach of it - the
 * same mistake the contrast guard made when it matched a declaration quoted inside a comment, and the
 * same fix.</p>
 */
function code(file: string): string {
  return readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '')
}

function routeFiles(dir = ROUTES): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return routeFiles(full)
    if (!entry.endsWith('.tsx') || entry.includes('.test.')) return []
    return [full]
  })
}

/**
 * The screens the router actually mounts, from the router rather than from a listing of the folder.
 *
 * <p>The folder also holds pieces that are not screens - a tab strip, a section of a workspace, a test
 * harness - and demanding a page title of those would be demanding the wrong thing. What the router
 * names is what a person can land on.</p>
 */
function mountedScreens(): string[] {
  const router = readFileSync(resolve(process.cwd(), 'src/router.tsx'), 'utf8')
  const named = [...router.matchAll(/'\.\/routes\/([A-Za-z/]+)'/g)].map((m) => `${m[1]}.tsx`)
  return [...new Set(named)].sort()
}

/**
 * Whether a screen says its own name, itself or through something it renders.
 *
 * <p>One level of delegation, because that is the depth the product actually uses: three auth screens
 * hand the whole viewport to `AcceptInvitePageBase`, and the tender's six views hand their band to
 * `TenderHeader`. A resolver that chased imports without limit would be a module graph rather than a
 * test.</p>
 */
function saysItsName(relativePath: string): boolean {
  let source: string
  try {
    source = code(join(ROUTES, relativePath))
  } catch {
    return true
  }
  if (/<(PageHeading|AuthHeading|TenderHeader)[\s/>]/.test(source)) return true
  return [...source.matchAll(/from '([./A-Za-z-]+)'/g)]
    .map((m) => m[1])
    .filter((spec) => spec.startsWith('.'))
    .some((spec) => {
      for (const base of [ROUTES, COMPONENTS]) {
        try {
          const candidate = resolve(join(ROUTES, relativePath, '..'), `${spec}.tsx`)
          if (!candidate.startsWith(base) && base !== ROUTES) continue
          if (/<(PageHeading|AuthHeading)[\s/>]/.test(code(candidate))) return true
        } catch { /* not a local .tsx, or not readable - not a heading source */ }
      }
      return false
    })
}

/**
 * Screens that legitimately have no page title, each with the reason.
 *
 * <p>Empty, and that is the finding. Two screens - the notification centre and the evaluator's own
 * queue - carried their name in a card's header band, which renders at body size, so they had no page
 * heading at all. The rule above forbade hand-rolling one and never required having one, which is how
 * both passed every sweep in the suite: an accessibility scan tags a missing top-level heading as
 * best-practice, and this project scans the WCAG tags only.</p>
 */
const NO_TITLE_NEEDED: Record<string, string> = {}

describe('every screen takes its title from one component', () => {
  const files = routeFiles()

  it('sweeps the route tree it claims to', () => {
    // The denominator. A sweep that matched nothing would pass every assertion below, which is exactly
    // the shape of instrument this batch has been removing.
    expect(files.length).toBeGreaterThanOrEqual(60)
  })

  it('no screen hand-rolls its own <h1>', () => {
    const handRolled = files
      .filter((file) => /<h1[\s>]/.test(code(file)))
      .map((file) => relative(ROUTES, file))
      .filter((name) => !(name in EXEMPT))

    expect(
      handRolled,
      'these render a page title that PageHeading does not own, so its size is theirs to get wrong',
    ).toEqual([])
  })

  it('every screen the router mounts says its own name', () => {
    const silent = mountedScreens()
      .filter((screen) => !saysItsName(screen))
      .filter((screen) => !(screen in NO_TITLE_NEEDED))

    expect(
      silent,
      'a screen with no page heading has no <h1>: a reader landing on it is told nothing about where they are',
    ).toEqual([])
  })

  it('the name check can fail, and reads a real list of screens', () => {
    // The denominator, and the control. A resolver that found a heading in everything would pass the
    // assertion above while checking nothing.
    expect(mountedScreens().length).toBeGreaterThan(40)
    expect(saysItsName('LoginPage.tsx')).toBe(true)
    // Delegation really resolves: this one renders no heading itself and hands the viewport to a
    // component that does.
    expect(code(join(ROUTES, 'AcceptTeamInvitePage.tsx'))).not.toMatch(/<(PageHeading|AuthHeading)[\s/>]/)
    expect(saysItsName('AcceptTeamInvitePage.tsx')).toBe(true)
    // And a file that genuinely says no name is genuinely reported.
    expect(saysItsName('back-office/rfq/tenderTestHarness.tsx')).toBe(false)
  })

  it('no component outside the two heading components declares an <h1> either', () => {
    // The hole this closes was found the hard way. `AcceptInvitePageBase` is a whole screen that lives
    // under components/ because two routes share it, so a sweep of routes/ alone declared victory while
    // one of the three heading sizes was still there.
    const offenders = routeFiles(COMPONENTS)
      .filter((file) => /<h1[\s>]/.test(code(file)))
      .map((file) => relative(COMPONENTS, file))
      .filter((name) => !HEADING_COMPONENTS.includes(name))

    expect(offenders, 'a screen that lives under components/ is still a screen').toEqual([])
  })

  it('the check can fail', () => {
    // Revert-to-red, against a string rather than a file, so a genuine failure above produces one clear
    // message rather than two.
    expect(/<h1[\s>]/.test('<h1 className="whatever">A title</h1>')).toBe(true)
    expect(/<h1[\s>]/.test('<PageHeading title={t("x.title")} />')).toBe(false)
  })
})
