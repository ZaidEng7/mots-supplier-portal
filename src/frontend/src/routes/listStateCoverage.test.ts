// A screen's four states come from one component, not from a ternary chain per screen.
//
// What was wrong. Loading, failed, empty and loaded are four answers to "what is here?", and seven screens spelled all
// four out by hand. They did not agree: some rendered a bare skeleton with no card around it and then a DIFFERENT card
// on failure, so the screen changed shape twice on its way to a table. Some offered a retry, some did not. Some said "no
// history" for a failed fetch, which is the defect ListState was written for in the first place and which had simply
// come back elsewhere.
//
// Why the check is shaped this way. A route may still branch on isPending - a button's spinner, a disabled control, a
// section that is not a list. What it may not do is spell out the whole chain, and the chain is what this looks for: a
// loading branch and an empty branch inside one expression.
//
// The exemptions are screens that spell the chain out for a reason, each entry saying why.
//
// The sweep asserts it reads the route tree it claims to, then that no screen spells out loading, failed and empty by
// hand. The last test is the control.

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

function hasHandRolledChain(source: string): boolean {
  const opens = [...source.matchAll(/\{[^{}]*?is(?:Pending|Loading)\s*\?\s*\(/g)]
  return opens.some((match) => {
    const window = source.slice(match.index, match.index + 1400)
    return /length === 0 \?/.test(window) && /Skeleton/.test(window)
  })
}

describe('the four states of a list come from one component', () => {
  const files = routeFiles()

  it('sweeps the route tree it claims to', () => {
    expect(files.length).toBeGreaterThanOrEqual(60)
  })

  it('no screen spells out loading, failed and empty by hand', () => {
    const handRolled = files
      .filter((file) => hasHandRolledChain(readFileSync(file, 'utf8')))
      .map((file) => relative(ROUTES, file))
      .filter((name) => !(name in EXEMPT))

    expect(
      handRolled,
      'these answer "what is here?" four times in their own words, and the answers drift',
    ).toEqual([])
  })

  it('the check can fail', () => {
    const chain = "{q.isPending ? (<SkeletonTable />) : q.isError ? (<E/>) : rows.length === 0 ? (<p/>) : (<Table/>)}"
    const delegated = "<ListState isPending={q.isPending} isEmpty={rows.length === 0}><Table/></ListState>"

    expect(hasHandRolledChain(chain)).toBe(true)
    expect(hasHandRolledChain(delegated)).toBe(false)
  })
})
