// Every route the router declares, parsed out of the router itself.
//
// Why this is shared rather than copied. It was written inside the accessibility sweep, whose own
// header explains at length why the list must be derived and never hand-typed: a route added to the
// router without reaching that suite is a page nobody scans, and a hand-maintained array drifts
// quietly. The Phase F capture driver needs exactly the same list, and a second parser beside this
// one would be the drift this file exists to prevent - two denominators that agree until the day
// they do not. Moved verbatim; the accessibility sweep still asserts the count, which is the
// assertion that makes this parser trustworthy.
//
// resolveFullPath walks a route up to its parent and joins the segments, taking care not to double
// the slash when both sides are present and non-root.
//
// isShellWrapper excludes layout routes. A shell renders only `<Shell><Outlet /></Shell>` - chrome
// around a child route, never independent content of its own - and that is the basis for excluding
// it from the page count, not whether it happens to declare a `path:`. backOfficeLayoutRoute does
// declare one, and is still a shell.

import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const dirname = path.dirname(fileURLToPath(import.meta.url))

export interface RouteEntry {
  name: string
  fullPath: string
  requiresSupplierAuth: boolean
  requiresReviewerAuth: boolean
}

export function extractRoutes(): RouteEntry[] {
  const source = readFileSync(path.join(dirname, '../../src/router.tsx'), 'utf-8')
  const blocks = [...source.matchAll(/const (\w+Route) = createRoute\(\{([\s\S]*?)\n\}\)/g)]

  type Raw = { name: string; body: string; path: string | null; id: string | null; parent: string | null }
  const raw: Raw[] = blocks.map(([, name, body]) => {
    const pathMatch = body.match(/path:\s*'([^']*)'/)
    const idMatch = body.match(/id:\s*'([^']*)'/)
    const parentMatch = body.match(/getParentRoute:\s*\(\)\s*=>\s*(\w+)/)
    return {
      name,
      body,
      path: pathMatch ? pathMatch[1] : null,
      id: idMatch ? idMatch[1] : null,
      parent: parentMatch ? parentMatch[1] : null,
    }
  })

  const byName = new Map(raw.map((r) => [r.name, r]))

  function resolveFullPath(r: Raw): string {
    const own = r.path ?? ''
    if (r.parent === 'rootRoute' || !r.parent) return own
    const parent = byName.get(r.parent)
    if (!parent) return own
    const parentPath = resolveFullPath(parent)
    if (!own) return parentPath
    return `${parentPath === '/' ? '' : parentPath}${own}`
  }

  const isShellWrapper = (body: string) => /<\w*Shell>[\s\S]*<Outlet \/>/.test(body)

  return raw
    .filter((r) => !isShellWrapper(r.body))
    .map((r) => {
      const fullPath = resolveFullPath(r)
      const requiresSupplierAuth = r.parent === 'supplierLayoutRoute'
      const requiresReviewerAuth = r.parent === 'backOfficeLayoutRoute'
      return { name: r.name, fullPath, requiresSupplierAuth, requiresReviewerAuth }
    })
}
