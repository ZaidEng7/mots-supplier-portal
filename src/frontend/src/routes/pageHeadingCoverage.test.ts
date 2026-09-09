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

function routeFiles(dir = ROUTES): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return routeFiles(full)
    if (!entry.endsWith('.tsx') || entry.includes('.test.')) return []
    return [full]
  })
}

describe('every screen takes its title from one component', () => {
  const files = routeFiles()

  it('sweeps the route tree it claims to', () => {
    // The denominator. A sweep that matched nothing would pass every assertion below, which is exactly
    // the shape of instrument this batch has been removing.
    expect(files.length).toBeGreaterThanOrEqual(60)
  })

  it('no screen hand-rolls its own <h1>', () => {
    const handRolled = files
      .filter((file) => /<h1[\s>]/.test(readFileSync(file, 'utf8')))
      .map((file) => relative(ROUTES, file))
      .filter((name) => !(name in EXEMPT))

    expect(
      handRolled,
      'these render a page title that PageHeading does not own, so its size is theirs to get wrong',
    ).toEqual([])
  })

  it('no component outside the two heading components declares an <h1> either', () => {
    // The hole this closes was found the hard way. `AcceptInvitePageBase` is a whole screen that lives
    // under components/ because two routes share it, so a sweep of routes/ alone declared victory while
    // one of the three heading sizes was still there.
    const offenders = routeFiles(COMPONENTS)
      .filter((file) => /<h1[\s>]/.test(readFileSync(file, 'utf8')))
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
