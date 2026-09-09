import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'

/**
 * Sweep: a screen that fetches must be able to say "we could not load this".
 *
 * <p><b>The defect this closes.</b> Eighteen route components distinguished loading from empty and
 * stopped there. React Query does not throw to the router's error boundary unless a query opts in, and
 * none do, so a failed fetch left `data` undefined and the page rendered its EMPTY state: "you have no
 * invitations", "nothing waiting for you", "no recommendation yet". Every one of those is a statement
 * about the world that the product had not checked - the same class of error D-66 got right on the
 * Ministry screens, where a withheld value renders as withheld rather than as zero.</p>
 *
 * <p><b>What this checks and what it cannot.</b> It reads source, so it proves a branch EXISTS, not that
 * its copy is right or that it renders. `ReviewQueuePage.test.tsx` and `SupplierRfqListPage.test.tsx`
 * render the failure for real and assert the reader is told the truth; this sweep is what stops the
 * nineteenth screen shipping without either.</p>
 */

const ROUTES = resolve(process.cwd(), 'src/routes')

/**
 * Screens that fetch but deliberately have no error branch of their own, each with the reason. Typed out
 * by hand: a pattern-matched exemption lets the next instance join it silently.
 */
const NO_ERROR_BRANCH_NEEDED: Record<string, string> = {
  'AboutPage.tsx':
    'The version read is decoration on a page whose real content is static text. A failed fetch leaves '
    + '"Loading…" beside the build number and costs the reader nothing - there is no decision here that '
    + 'depends on it.',
  'RegisterPage.tsx':
    'The only query is public settings, used to decide whether registration is open. Its failure is '
    + 'handled by the form itself refusing to submit, and an error panel over the registration form '
    + 'would block a person from a task that may still be possible.',
  'admin/AuditExplorerPage.tsx':
    'Has an error branch already, spelled with its own `error` variable rather than `isError`, because '
    + 'it must distinguish a refused FIELD (a permission answer, shown inline) from a failed query.',
}

function pageFiles(dir = ROUTES, prefix = ''): string[] {
  const out: string[] = []
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) { out.push(...pageFiles(full, `${prefix}${entry}/`)); continue }
    if (!entry.endsWith('.tsx') || /\.(test|stories)\.tsx$/.test(entry)) continue
    out.push(`${prefix}${entry}`)
  }
  return out.sort()
}

const FETCHING_PAGES = pageFiles().filter((f) => {
  const s = readFileSync(join(ROUTES, f), 'utf8')
  return s.includes('useQuery') || s.includes('useInfiniteQuery')
})

describe('async state coverage', () => {
  it('the sweep reads the routes, not a handful of them', () => {
    // The denominator, before the rule: an empty list would make every assertion below vacuous.
    expect(FETCHING_PAGES.length).toBeGreaterThan(40)
  })

  it('every fetching screen can say the fetch failed', () => {
    const silent = FETCHING_PAGES.filter((file) => {
      if (file in NO_ERROR_BRANCH_NEEDED) return false
      const source = readFileSync(join(ROUTES, file), 'utf8')
      // Either the screen handles it itself, or it hands the whole state machine to a component that
      // does - ListCard and ListState take the failure copy as a prop and render it in place.
      return !/isError|<QueryError|<ListCard|<ListState/.test(source)
    })

    expect(silent, 'these render their EMPTY state when the fetch fails, telling the reader there is '
      + 'nothing here when the truth is that we could not ask:\n  ' + silent.join('\n  ')).toEqual([])
  })

  it('every exemption still names a page that exists, still fetches, and still says why', () => {
    for (const [file, reason] of Object.entries(NO_ERROR_BRANCH_NEEDED)) {
      expect(FETCHING_PAGES, `${file} is exempted but no longer fetches anything`).toContain(file)
      expect(reason.length, `${file}'s exemption must say why`).toBeGreaterThan(80)
    }
  })

  it('the check can fail', () => {
    // The control. A matcher that answered true for everything would keep this green forever, which is
    // the failure this repository has now found in five other sweeps.
    const silentPage = "const q = useQuery({}); return q.data.length === 0 ? <p>empty</p> : <Table />"
    const handledPage = "const q = useQuery({}); if (q.isError) return <QueryError />"
    const delegatedPage = "const q = useInfiniteQuery({}); return <ListCard query={q} labels={labels} />"

    expect(/isError|<QueryError|<ListCard|<ListState/.test(silentPage)).toBe(false)
    expect(/isError|<QueryError|<ListCard|<ListState/.test(handledPage)).toBe(true)
    expect(/isError|<QueryError|<ListCard|<ListState/.test(delegatedPage)).toBe(true)
  })
})
