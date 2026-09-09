import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'

/**
 * The next page of a list comes from one component, not from a ternary per screen.
 *
 * <p><b>What was wrong.</b> `LoadMore` was written, exported, documented against the defect it exists to
 * prevent — MSP-84, a list that silently ended at twenty rows — and then adopted by nobody. Six screens
 * each wrote the same `hasNextPage ? <Button/> : null` in their own hand. They did not agree on size, on
 * spacing, or on whether the button sat inside the card at all, and none of them was the component.</p>
 *
 * <p><b>Why the check is shaped this way.</b> Screens legitimately hold the paging fields — they own the
 * query, and some hand `ListCard` a structural object carrying them. What they may not do is build the
 * affordance. So the check looks for the affordance itself: a `hasNextPage` guarding a `<Button>` a few
 * lines later, which is what all six hand-written versions were and what none of them should be.</p>
 */
const ROUTES = resolve(process.cwd(), 'src/routes')

/** Screens that call `fetchNextPage` outside `LoadMore` for a reason. Each entry says why. */
const EXEMPT: Record<string, string> = {}

function routeFiles(dir = ROUTES): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return routeFiles(full)
    if (!entry.endsWith('.tsx') || entry.includes('.test.')) return []
    return [full]
  })
}

/** A `hasNextPage` test standing in front of a button the screen built itself. */
function hasHandRolledNextPage(source: string): boolean {
  return /hasNextPage[\s\S]{0,240}?<Button/.test(source.replace(/<LoadMore\b[\s\S]*?\/>/g, ''))
}

describe('the next page of a list comes from one component', () => {
  const files = routeFiles()

  it('sweeps the route tree it claims to', () => {
    expect(files.length).toBeGreaterThanOrEqual(60)
  })

  /**
   * The denominator. A sweep that found no paged screens would pass the test below while proving
   * nothing, and that is exactly the shape of the bug being fixed: a component nobody used. Counted by
   * `useInfiniteQuery` rather than by the paging fields, because a screen that has adopted `ListCard`
   * hands it the whole query and never names `fetchNextPage` itself.
   */
  it('finds the paged screens it is meant to be checking', () => {
    const paged = files.filter((file) => /useInfiniteQuery/.test(readFileSync(file, 'utf8')))
    expect(paged.length).toBeGreaterThanOrEqual(10)
  })

  it('no screen builds its own next-page button', () => {
    const handRolled = files
      .filter((file) => hasHandRolledNextPage(readFileSync(file, 'utf8')))
      .map((file) => relative(ROUTES, file))
      .filter((name) => !(name in EXEMPT))

    expect(
      handRolled,
      'these reach for fetchNextPage outside LoadMore, which is how a list quietly ends at twenty rows',
    ).toEqual([])
  })

  it('the check can fail', () => {
    const handRolled = '{q.hasNextPage ? (<Button onClick={() => q.fetchNextPage()}>More</Button>) : null}'
    const delegated = '<LoadMore hasNextPage={q.hasNextPage} isFetching={q.isFetchingNextPage} onClick={() => q.fetchNextPage()} label="More" />'

    expect(hasHandRolledNextPage(handRolled)).toBe(true)
    expect(hasHandRolledNextPage(delegated)).toBe(false)
  })
})
