// The client half of §8.1's sweep: every guarded write this SPA makes, against a read it could have taken the
// version from.
//
// What this is for. Five of batch 13's thirteen findings were the same question with four different causes - where
// does the version come from, and does anything fetch it. A documents call used the wrong field name so the path
// carried `undefined`; the reviewer's supplier read filed its ETag under a path no write would look at; the
// offerings editor patched an item it had never read; two RFQ children had no update route at all. Each compiled,
// each passed the suite, and each produced a 428 the first time somebody clicked the button. Nothing had ever asked
// whether the two halves of the contract - a write demanding a version, and a read issuing one for a path the write
// can reach - were both present.
//
// Why the OpenAPI document is the input. Which writes need If-Match is the server's fact, and until now it lived
// only in a C# filter: the published contract said nothing about it, so neither this sweep nor any other client
// could have known. ConcurrencyOpenApiTransformer publishes it, and contracts/openapi-v1.baseline.json is the
// committed copy - the same file the backend contract gate compares against, refreshed by
// UPDATE_OPENAPI_BASELINE=1 dotnet test. From it come two sets: the guarded operations, as METHOD and path, and the
// GET paths whose success response carries an ETag header.
//
// The rule. etags.ts walks a path's prefixes UPWARD, never sideways, so a guarded write to /a/b/c is satisfiable
// only if some ETag-emitting GET that this client actually calls sits at /a/b/c, /a/b or /a. A template
// substitution in the client and an OpenAPI parameter both normalise to the same empty placeholder, because they
// are the same URL differently declared, and a parameter segment in the READ matches anything - at runtime the
// client filed its ETag under a concrete URL and the placeholder is only how that URL is declared - while a
// parameter in the WRITE against a literal in the read is not a match, because those are different URLs.
//
// The scanner reads every apiFetch call in src/api with the method it passes. A module that never names a method
// makes only GETs, so a call site the scanner cannot read there cannot be hiding a guarded write - dashboards.ts is
// that case, where every path goes through a local get<T>(path) wrapper and teaching the scanner to follow wrappers
// would buy nothing. Unreadable call sites in modules that DO write are recorded rather than skipped, because a
// scanner that silently ignores what it cannot read is measuring a subset and reporting a total, which is the shape
// of the six vacuous instruments this repository has already found. proposals.ts was exactly that case: every one
// of its paths is built from a base() helper, so the first version of this sweep skipped the whole module and the
// five proposal writes it was specifically written to cover. So path-building helpers are resolved too, in the two
// forms src/api actually uses - an arrow const returning one template literal, and a function whose body is a
// single return of one - and anything else is left unresolved rather than guessed at. The argument list of one call
// is read on its own, so a method belonging to the NEXT call is not read as this one's.
//
// Two exemption lists, written by hand for the same reason the router guard's are: a pattern-matched exemption is
// one the next defect joins silently. The first is writes whose version comes from a read the prefix walk cannot
// reach, where the client files the same ETag under a second path on purpose - each one a place the upward-only
// rule had to be worked around. The second is distinct from it: there the two paths name one resource, whereas
// here the reviewer reads a supplier through a reviewing route and then writes to the supplier's own.
//
// The denominators are asserted before the rule, because every assertion here is satisfied by an empty set - which
// is the failure mode this repository has now found in seven of its own instruments. Only reads this client
// actually performs are counted: a route that emits an ETag and which nothing fetches cannot hand anybody a
// version, and that distinction is the whole of finding F-6.
//
// The second test is the control on the matcher - a prefix rule that answered true for everything would make the
// sweep green forever, which is exactly how an instrument stops measuring - and it checks the exemptions are still
// live, because an exemption for a route that no longer requires a precondition is one nobody is reading, and the
// next defect inherits it.

import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'


const repoRoot = resolve(process.cwd(), '../..')

interface Call {
  method: string
  path: string
  file: string
}

function normalise(path: string): string {
  return path
    .split('?')[0]
    .replace(/\$\{[^}]*\}/g, '{}')
    .replace(/\{[^}]*\}/g, '{}')
    .replace(/\/+$/, '')
}

function segmentsOf(path: string): string[] {
  return normalise(path).split('/').filter(Boolean)
}

function covers(read: string, write: string): boolean {
  const r = segmentsOf(read)
  const w = segmentsOf(write)
  if (r.length > w.length) return false
  return r.every((segment, i) => segment === '{}' || segment === w[i])
}

function collectCalls(): { calls: Call[], unresolved: string[] } {
  const modules = import.meta.glob('./*.ts', { query: '?raw', import: 'default', eager: true }) as Record<string, string>
  const calls: Call[] = []
  const unresolved: string[] = []

  for (const [file, source] of Object.entries(modules)) {
    if (/\.(test|spec)\./.test(file)) continue

    const helpers = pathHelpersIn(source)

    const writesAnything = /method:\s*'[A-Z]+'/.test(source)

    for (let index = source.indexOf('apiFetch('); index !== -1; index = source.indexOf('apiFetch(', index + 1)) {
      if (/(function|const)\s+$/.test(source.slice(Math.max(0, index - 30), index))) continue

      const slice = balanced(source, index + 'apiFetch('.length - 1)
      const literal = /^[\s(]*[`'"]([^`'"]*)[`'"]/.exec(slice)?.[1]
      const argument = /^[\s(]*([A-Za-z0-9_]+)\(/.exec(slice)?.[1]
      const path = literal !== undefined ? expand(literal, helpers) : argument !== undefined ? helpers[argument] : undefined

      if (path === undefined || !path.startsWith('/api/')) {
        if (writesAnything) unresolved.push(`${file}: ${slice.slice(0, 60).replace(/\s+/g, ' ')}`)
        continue
      }

      calls.push({ method: (/method:\s*'([A-Z]+)'/.exec(slice)?.[1] ?? 'GET').toUpperCase(), path, file })
    }
  }

  return { calls, unresolved }
}

function pathHelpersIn(source: string): Record<string, string> {
  const helpers: Record<string, string> = {}

  for (const match of source.matchAll(/const\s+([A-Za-z0-9_]+)\s*=\s*\([^)]*\)\s*(?::[^=]*)?=>\s*`(\/api\/[^`]*)`/g)) {
    helpers[match[1]] = match[2]
  }
  for (const match of source.matchAll(/function\s+([A-Za-z0-9_]+)\([^)]*\)[^{]*\{\s*return\s+`(\/api\/[^`]*)`/g)) {
    helpers[match[1]] = match[2]
  }

  return helpers
}

function expand(literal: string, helpers: Record<string, string>): string {
  return literal.replace(/\$\{\s*([A-Za-z0-9_]+)\s*\([^}]*\)\s*\}/g, (whole, name: string) => helpers[name] ?? whole)
}

function balanced(source: string, openParen: number): string {
  let depth = 0
  for (let i = openParen; i < source.length; i++) {
    if (source[i] === '(') depth++
    else if (source[i] === ')') {
      depth--
      if (depth === 0) return source.slice(openParen, i + 1)
    }
  }
  return source.slice(openParen)
}

interface Contract {
  guarded: Set<string>
  etagReads: string[]
}

function readContract(): Contract {
  const document = JSON.parse(
    readFileSync(resolve(repoRoot, 'contracts/openapi-v1.baseline.json'), 'utf8'),
  ) as {
    paths: Record<string, Record<string, {
      parameters?: { name?: string, in?: string }[]
      responses?: Record<string, { headers?: Record<string, unknown> }>
    }>>
  }

  const guarded = new Set<string>()
  const etagReads: string[] = []

  for (const [path, operations] of Object.entries(document.paths)) {
    for (const [method, operation] of Object.entries(operations)) {
      const requiresIfMatch = (operation.parameters ?? [])
        .some((p) => p.in === 'header' && p.name === 'If-Match')
      if (requiresIfMatch) guarded.add(`${method.toUpperCase()} ${normalise(path)}`)

      const emitsETag = Object.entries(operation.responses ?? {})
        .some(([status, response]) => status.startsWith('2') && response.headers?.ETag !== undefined)
      if (emitsETag && method.toLowerCase() === 'get') etagReads.push(normalise(path))
    }
  }

  return { guarded, etagReads }
}

const FILED_UNDER_A_SECOND_PATH: Record<string, string> = {
  'PATCH /api/v1/proposals/{}': 'api/proposals.ts getProposal files the ETag from GET /rfqs/{code}/proposals under the proposal path too — one resource at two addresses, and the walk never goes sideways. Batch 11, D-47.',
  'POST /api/v1/proposals/{}/submit': 'Same ETag as the PATCH above, filed by the same read.',
  'POST /api/v1/proposals/{}/withdraw': 'Same ETag as the PATCH above.',
  'POST /api/v1/proposals/{}/revise': 'Same ETag as the PATCH above.',
  'POST /api/v1/proposals/{}/decline': 'Same ETag as the PATCH above.',
  'PATCH /api/v1/suppliers/{}': 'api/supplier.ts profileFrom files every profile response under /suppliers/{code}, because a supplier reads itself at /suppliers/me and is written at /suppliers/{code}. Batch 11, the same shape as D-47 one aggregate later.',
  'POST /api/v1/suppliers/{}/onboarding/submit': 'Same ETag as the PATCH above, filed by the same helper.',
  'POST /api/v1/evaluation-templates/{}/activate': 'api/evaluationTemplates.ts templateFrom files a template response under /evaluation-templates/{id}: the list GET carries no ETag and a create files its fresh one under the collection, so activate had nothing to send and answered 428 every time. Batch 12, found by walking a tender from an empty database.',
  'POST /api/v1/evaluation-templates/{}/archive': 'Same ETag as activate, filed by the same helper.',
}

const READ_THROUGH_ANOTHER_PERSONAS_ROUTE: Record<string, string> = {
  'POST /api/v1/suppliers/{}/documents/{}/approve': 'The reviewer never reads /suppliers/{code}: getReviewerSupplierView reads GET /review/{code} and files its ETag under the supplier path as well. Batch 13, F-4.',
  'POST /api/v1/suppliers/{}/documents/{}/reject': 'Same read as the approve above.',
}

describe('every guarded write has a version it can actually send', () => {
  it('names a read for each one, or an exemption', () => {
    const { guarded, etagReads } = readContract()
    const { calls, unresolved } = collectCalls()

    expect(guarded.size, 'the contract must document which writes need If-Match').toBeGreaterThan(30)
    expect(etagReads.length, 'and which reads issue one').toBeGreaterThan(5)
    expect(calls.length, 'apiFetch call sites must be discoverable by the scanner').toBeGreaterThan(150)
    expect(
      unresolved,
      'the scanner could not read the path of these calls, so they were checked by nothing — teach it the form rather than leaving them out',
    ).toEqual([])

    const writes = calls.filter((c) => guarded.has(`${c.method} ${normalise(c.path)}`))
    expect(writes.length, 'the SPA must be calling guarded writes for this sweep to mean anything').toBeGreaterThan(20)

    const performedReads = new Set(
      calls.filter((c) => c.method === 'GET').map((c) => normalise(c.path)),
    )
    const usableReads = etagReads.filter((path) => [...performedReads].some((p) => normalise(p) === path))
    expect(usableReads.length, 'the client must fetch at least some ETag-emitting reads').toBeGreaterThan(3)

    const unsatisfiable = writes
      .map((c) => ({ key: `${c.method} ${normalise(c.path)}`, file: c.file }))
      .filter(({ key }) => !(key in FILED_UNDER_A_SECOND_PATH) && !(key in READ_THROUGH_ANOTHER_PERSONAS_ROUTE))
      .filter(({ key }) => !usableReads.some((read) => covers(read, key.split(' ')[1])))
      .map(({ key, file }) => `${key}  (${file})`)
      .sort()

    expect(
      [...new Set(unsatisfiable)],
      'these writes demand a version no read this client performs can supply — the store walks prefixes upward, never sideways',
    ).toEqual([])
  })

  it('can fail, and the exemptions are all still live', () => {
    expect(covers('/api/v1/suppliers/me', '/api/v1/suppliers/me/contacts/{}')).toBe(true)
    expect(covers('/api/v1/suppliers/{}', '/api/v1/suppliers/me/contacts')).toBe(true)
    expect(covers('/api/v1/rfqs/{}/items', '/api/v1/rfqs/{}/requirements')).toBe(false)
    expect(covers('/api/v1/proposals/{}', '/api/v1/rfqs/{}/proposals')).toBe(false)
    expect(covers('/api/v1/suppliers/me/contacts', '/api/v1/suppliers/me')).toBe(false)

    const { guarded, etagReads } = readContract()
    const exempted = [...Object.keys(FILED_UNDER_A_SECOND_PATH), ...Object.keys(READ_THROUGH_ANOTHER_PERSONAS_ROUTE)]

    expect(
      exempted.filter((key) => !guarded.has(key)),
      'exempted routes that are no longer guarded writes',
    ).toEqual([])

    const { calls } = collectCalls()
    const performed = new Set(calls.filter((c) => c.method === 'GET').map((c) => normalise(c.path)))
    const usableReads = etagReads.filter((path) => performed.has(path))

    expect(
      exempted.filter((key) => usableReads.some((read) => covers(read, key.split(' ')[1]))),
      'exemptions that are no longer needed — a read now covers these, so the entry should go',
    ).toEqual([])
  })
})
