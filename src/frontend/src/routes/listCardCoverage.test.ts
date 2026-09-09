import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'

/**
 * A card holding a list is one component, not a `Card` a screen wraps around a `ListState`.
 *
 * <p><b>What was wrong.</b> `ListCard` exists because two screens wrote the scaffolding out identically
 * and a third was about to. It then stopped at four adopters, and the reason was the component rather
 * than the screens: it could not express a retry, a skeleton shape, a control beside the title, or a note
 * belonging to the card rather than the rows. Every screen that needed one of those kept its own `Card`
 * and spelled the six props out again. The component now carries all four, so there is nothing left for a
 * screen to keep its own `Card` for.</p>
 *
 * <p><b>Why the check is shaped this way.</b> A `ListState` on its own is fine: a screen whose list is
 * not in a card, or is one part of a card holding other things, is using the component correctly. What
 * this looks for is the specific pair — a `Card` opened and a `ListState` as the next thing inside it —
 * because that pair IS `ListCard`, spelled out.</p>
 */
const ROUTES = resolve(process.cwd(), 'src/routes')

/** Screens that wrap `ListState` in their own `Card` for a reason. Each entry says why. */
const EXEMPT: Record<string, string> = {}

function routeFiles(dir = ROUTES): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return routeFiles(full)
    if (!entry.endsWith('.tsx') || entry.includes('.test.')) return []
    return [full]
  })
}

/** A `<Card>` opened with a `<ListState>` as the next element inside it. */
function wrapsListStateInACard(source: string): boolean {
  return /<Card[\s\S]{0,600}?>\s*(\{\/\*[\s\S]*?\*\/\}\s*)?<ListState/.test(source)
}

describe('a card holding a list comes from one component', () => {
  const files = routeFiles()

  it('sweeps the route tree it claims to', () => {
    expect(files.length).toBeGreaterThanOrEqual(60)
  })

  /** The denominator: a sweep that found no list cards at all would pass while proving nothing. */
  it('finds the list cards it is meant to be checking', () => {
    const listCards = files.filter((file) => /<ListCard/.test(readFileSync(file, 'utf8')))
    expect(listCards.length).toBeGreaterThanOrEqual(10)
  })

  it('no screen wraps ListState in its own Card', () => {
    const handRolled = files
      .filter((file) => wrapsListStateInACard(readFileSync(file, 'utf8')))
      .map((file) => relative(ROUTES, file))
      .filter((name) => !(name in EXEMPT))

    expect(
      handRolled,
      'these rebuild ListCard by hand, which is how two screens holding the same list stop agreeing',
    ).toEqual([])
  })

  it('the check can fail', () => {
    const handRolled = '<Card title={t("x")}>\n  <ListState isPending={q.isPending}><Table/></ListState>\n</Card>'
    const delegated = '<ListCard title={t("x")} query={q} isEmpty={rows.length === 0} labels={l}><Table/></ListCard>'

    expect(wrapsListStateInACard(handRolled)).toBe(true)
    expect(wrapsListStateInACard(delegated)).toBe(false)
  })
})
