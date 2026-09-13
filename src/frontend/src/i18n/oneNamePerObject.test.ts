// One object has one name, and it is the name its reader uses.
//
// The decision this pins. The thing a buyer publishes and a supplier bids on is a TENDER in English and a مناقصة in Arabic.
// RFQ is the internal term: it stays in reference codes like RFQ-2026-000001, in API routes like /api/v1/rfqs, in type names
// and in this catalogue's own KEYS. It does not reach a reader.
//
// The defect. Thirty-seven English strings said "RFQ" while the supplier's own navigation said "Tenders" - so one object had
// two names, and the one a supplier met most was the acronym they had never been taught. The Rams audit scored the product 1
// out of 3 on understandable and named this first: "one word per object". The Arabic catalogue had the same split three ways
// over, with three strings saying "طلب عرض أسعار" where twelve said "مناقصة".
//
// Why a test and not a style note. A catalogue is 4,000 lines and two languages, and the way a word creeps back in is one
// string at a time, each of them reasonable on its own. The rename is only worth doing if it holds.
//
// THE EXEMPTION MAP IS EMPTY, and that is the finding. The first draft of this file carried one entry - a key for the RFQ-
// reference prefix - written from memory rather than from the catalogue. No such key exists: the prefix lives in the codes the
// server issues, never in a translated string. An exemption for a key nobody declares is an exemption nobody is reading, and it
// would have silently covered whatever took that name next - the same rot FilterGuardTests caught in its own list on its first
// run. An empty map is the strongest form of this rule: every English string is held to it, and adding an entry means arguing
// for one here, by hand, with its reason.
//
// The denominator comes before the rule that uses it, because a bundle that failed to load would otherwise report a clean pass
// over nothing - the failure this repository has paid for repeatedly.
//
// Then: every English string says tender and never RFQ, and never spells the acronym out either. That second half is the one
// the first pass missed and the screenshot found: the tender list's own subtitle read "Create and manage Requests for Quotation
// through their full lifecycle" - the object named in full, under a heading that had just called it something else - and the
// Arabic catalogue carried the same long form eight times. Every Arabic string says مناقصة and never طلب عرض.
//
// The KEYS still use rfq, and that control matters: without it this file would pass just as well against a catalogue that had
// renamed the keys too - a breaking change to every call site, in service of a rule that was only ever about what a reader
// sees. The last test is revert-to-red in the shape the defect takes: one reasonable-looking string at a time.

import { describe, expect, it } from 'vitest'
import i18n from './config'


function leaves(node: unknown, prefix = ''): Array<[string, string]> {
  if (typeof node === 'string') return [[prefix, node]]
  if (node === null || typeof node !== 'object') return []
  return Object.entries(node as Record<string, unknown>)
    .flatMap(([key, value]) => leaves(value, prefix ? `${prefix}.${key}` : key))
}

const STILL_CORRECT: Record<string, string> = {}

describe('the reader never meets the acronym', () => {
  const english = leaves(i18n.getResourceBundle('en', 'translation'))

  it('reads a real catalogue', () => {
    expect(english.length).toBeGreaterThan(1500)
    expect(english.some(([key]) => key.startsWith('rfq.'))).toBe(true)
  })

  it('says tender, never RFQ, in every English string', () => {
    const offending = english
      .filter(([key]) => !(key in STILL_CORRECT))
      .filter(([, value]) => /\bRFQs?\b/.test(value))
      .map(([key, value]) => `  ${key}: ${value}`)

    expect(offending, 'the acronym is the internal name; a reader is shown "tender"').toEqual([])
  })

  it('never spells the acronym out either', () => {
    const offending = english
      .filter(([, value]) => /requests? for quotation/i.test(value))
      .map(([key, value]) => `  ${key}: ${value}`)

    expect(offending, 'spelling it out is the same defect with more letters').toEqual([])
  })

  it('says مناقصة, never طلب عرض, in every Arabic string', () => {
    const arabic = leaves(i18n.getResourceBundle('ar', 'translation'))

    const offending = arabic
      .filter(([, value]) => value.includes('طلب عرض') || value.includes('طلبات عروض'))
      .map(([key, value]) => `  ${key}: ${value}`)

    expect(offending, 'the Arabic catalogue already said مناقصة twelve times; it says it everywhere now')
      .toEqual([])
  })

  it('still uses rfq as a KEY, which is the half that does not change', () => {
    expect(english.some(([key]) => key.startsWith('rfq.'))).toBe(true)
    expect(english.find(([key]) => key === 'rfq.title')?.[1]).toBe('Tenders')
  })

  it('can fail', () => {
    const sample: Array<[string, string]> = [
      ['rfq.add', 'New RFQ'],
      ['rfq.title', 'Tenders'],
    ]

    expect(sample.filter(([, value]) => /\bRFQs?\b/.test(value))).toHaveLength(1)
  })
})
