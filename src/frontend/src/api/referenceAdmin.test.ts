// The Reference Data page offers exactly the tables the server accepts.
//
// The server's list is ReferenceTables.All in src/backend/Application/ReferenceData/ReferenceAdminViews.cs. This list
// had five of its six for as long as Incoterms existed: the server accepted the sixth table and the page never offered
// it, so the eleven delivery terms a bid is matched against could be neither seen nor edited anywhere. Nothing failed,
// because a table missing from a picker is not an error anybody sees.
//
// So the two lists are compared directly, reading the server's constants out of its source the way the translation
// sweep reads the catalogue. Order is left out, because the picker's order is this screen's choice. A table the page
// offers and the server refuses is caught as well, because that one answers not-found on every request. The extractor
// fails loudly when it cannot find the list or a name in it, so a renamed constant is a red test rather than an empty
// list compared with nothing.

import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { REFERENCE_TABLES } from './referenceAdmin'

const SERVER = readFileSync(resolve(process.cwd(), '../backend/Application/ReferenceData/ReferenceAdminViews.cs'), 'utf8')

function serverTables(): string[] {
  const block = SERVER.slice(SERVER.indexOf('public static class ReferenceTables'))
  const values = new Map([...block.matchAll(/public const string (\w+) = "([^"]+)";/g)].map((m) => [m[1], m[2]]))
  const all = /All\s*=\s*\[([^\]]*)\]/.exec(block)
  if (!all) throw new Error('ReferenceTables.All was not found in ReferenceAdminViews.cs')
  return all[1].split(',').map((name) => name.trim()).filter(Boolean).map((name) => {
    const value = values.get(name)
    if (!value) throw new Error(`ReferenceTables.All names ${name}, which has no constant`)
    return value
  })
}

describe('reference tables', () => {
  it('the page offers exactly the tables the server accepts', () => {
    expect([...REFERENCE_TABLES].sort()).toEqual(serverTables().sort())
  })
})
