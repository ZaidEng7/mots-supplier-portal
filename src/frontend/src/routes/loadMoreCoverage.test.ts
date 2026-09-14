// The next page of a list comes from one component, not from a ternary per screen.
//
// What was wrong. LoadMore was written, exported, documented against the defect it exists to prevent - MSP-84, a list
// that silently ended at twenty rows - and then adopted by nobody. Six screens each wrote the same
// "hasNextPage ? <Button/> : null" in their own hand. They did not agree on size, on spacing, or on whether the button
// sat inside the card at all, and none of them was the component.
//
// Why the check is shaped this way. Screens legitimately hold the paging fields - they own the query, and some hand
// ListCard a structural object carrying them. What they may not do is build the affordance. So the check looks for the
// affordance itself: a hasNextPage guarding a <Button> a few lines later, which is what all six hand-written versions
// were and what none of them should be.
//
// The exemptions are screens that call fetchNextPage outside LoadMore for a reason, each entry saying why.
//
// The sweep asserts it reads the route tree it claims to, then its own denominator: a sweep that found no paged screens
// would pass the rule while proving nothing, and that is exactly the shape of the bug being fixed - a component nobody
// used. It is counted by useInfiniteQuery rather than by the paging fields, because a screen that has adopted ListCard
// hands it the whole query and never names fetchNextPage itself. Then no screen builds its own next-page button, and the
// last test is the control.

import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'

const ROUTES = resolve(process.cwd(), 'src/routes')

const EXEMPT: Record<string, string> = {}

function routeFiles(dir = ROUTES): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return routeFiles(full)
    if (!entry.endsWith('.tsx') || entry.includes('.test.')) return []
    return [full]
  })
}

function hasHandRolledNextPage(source: string): boolean {
  return /hasNextPage[\s\S]{0,240}?<Button/.test(source.replace(/<LoadMore\b[\s\S]*?\/>/g, ''))
}

describe('the next page of a list comes from one component', () => {
  const files = routeFiles()

  it('sweeps the route tree it claims to', () => {
    expect(files.length).toBeGreaterThanOrEqual(60)
  })

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
