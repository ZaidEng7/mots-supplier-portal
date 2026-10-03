// Sweep: every key a screen BUILDS at runtime resolves in both languages.
//
// The defect this closes. ReviewApplicationPage rendered t(`onboarding.fields.${f}`) over PROFILE_DISPLAY_FIELDS - the profile
// model's own field names. Four of the five had a label in that namespace by coincidence; the fifth, defaultCurrency, did not,
// because the wizard calls the same thing currencyCode. i18next falls back to printing the key, so a government officer
// deciding a company's application read the literal string onboarding.fields.defaultCurrency where a field label belongs - in
// both Arabic and English. It survived 137 axe scans, 630 unit tests and a screenshot review, because a key rendered as text is
// still text.
//
// What this can and cannot check. The product builds keys at 53 sites, and this checks the ones whose inputs are enumerable
// from a CONSTANT - where the full set of possible keys is knowable without running the app. A site whose variable comes from
// the server, a status code or a category code, is not covered here and is listed as such, so the gap is written down rather
// than implied. Each covered entry is a namespace and the exact set of names a screen will interpolate into it, so adding a
// field to one of those constants without adding its label fails here rather than on a reviewer's screen. The profile set
// mirrors PROFILE_FIELDS in ReviewApplicationPage.tsx, which mirrors ProfileFieldCodes.cs.
//
// THE BLOCK EXTRACTOR reads `name: { ... }` as a DIRECT CHILD of its parent, brace-balanced - not "first occurrence anywhere".
// The catalogue has an `onboarding` under `status`, the state-machine labels, as well as the wizard's own `onboarding`, and a
// naive indexOf finds the state machine: 362 characters with no fields block in it. That is how this sweep failed on its first
// run, which is a small demonstration of the thing it exists to catch. Comments are stripped first, because this file's prose
// is full of apostrophes and reading one as a string literal swallows every brace after it. Arabic is the first resource in the
// file and English the last.
//
// The sweep asserts it covers the sites it claims to - the denominator before the rule, because an empty list would pass every
// assertion after it - and then that every enumerable key resolves in each language.
//
// A name with a hyphen in it is a quoted key in the catalogue, 'document-types': rather than documentTypes:, so the matcher
// takes the name bare or quoted. Before that, no site here had a hyphenated name, and a matcher that only read bare names
// would have reported the two hyphenated tables, document-types and units-of-measure, as missing.
//
// The last test is the control: a matcher that found every name would keep this green forever, which is the failure this
// repository has now found in six other sweeps. It also pins the specific regression - the wizard namespace does NOT carry the
// profile model's name for the currency, which is exactly why the profile grid must not read from it. The control runs the
// sweep's own matcher rather than a copy of it, so loosening the matcher loosens the control with it and it goes red.

import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { PROFILE_DISPLAY_FIELDS, LEGAL_INFO_FIELDS } from '../routes/profileDisplayFields'
import { REFERENCE_TABLES } from '../api/referenceAdmin'
import { DASHBOARD_AUDIT_ACTIONS, dashboardAuditActionKey } from '../api/dashboardAuditActions'
import { DASHBOARD_ATTENTION_GROUPS, DASHBOARD_ATTENTION_KEYS, DASHBOARD_JOB_IDS, DASHBOARD_JOB_SCHEDULES, DASHBOARD_JOB_VERDICTS } from '../api/adminDashboard'


const CONFIG = readFileSync(resolve(process.cwd(), 'src/i18n/config.ts'), 'utf8')

const ENUMERABLE_SITES: { site: string; namespace: string; keys: readonly string[] }[] = [
  {
    site: 'ReviewApplicationPage / ProfilePage — the profile grid',
    namespace: 'profile.fields',
    keys: PROFILE_DISPLAY_FIELDS,
  },
  {
    site: 'ReviewApplicationPage / ProfilePage — the legal-information grid',
    namespace: 'profile.fields',
    keys: LEGAL_INFO_FIELDS,
  },
  {
    // The action names come from ErpImportAction in api/erpImport.ts, which mirrors the server's own enum. Four
    // names, all enumerable and copied here by hand, so a fifth action listed here without a label fails at this
    // sweep rather than printing "erpImport.action.Whatever" on an administrator's screen.
    site: 'ErpImportPage - the outcome badge on each supplier',
    namespace: 'erpImport.action',
    keys: ['Create', 'Update', 'Refuse', 'Suspend'],
  },
  {
    // The run's outcomes, from ErpImportOutcome. Separate from the preview's actions on purpose: a forecast says
    // what WOULD happen and a result says what DID, and Refused means the same thing in both while Failed has no
    // forecast equivalent at all.
    site: 'ErpImportPage - the outcome badge on each imported supplier',
    namespace: 'erpImport.outcome',
    keys: ['Created', 'Updated', 'Refused', 'Failed', 'Suspended'],
  },
  {
    // The push's states from ErpPushStatus in api/review.ts, which mirrors the server's SupplierErpPushStatus. The
    // chip is drawn for these four; NotRequested draws no chip, so it has no label to resolve.
    site: 'ReviewApplicationPage - the chip saying how far creating the supplier in the ERP has got',
    namespace: 'review.erpPush',
    keys: ['Requested', 'Linked', 'Created', 'Failed'],
  },
  {
    // The tables come from REFERENCE_TABLES in api/referenceAdmin.ts, the list the Reference Data page draws its table
    // picker and its heading from. The server's sixth table, incoterms, had no label here at all: the overview's
    // reference-data card fell back to printing the raw name "incoterms", and adding the table to the page without a
    // label would have printed the whole key over it in both languages.
    site: 'ReferenceDataPage - the table picker and the heading of the table being edited',
    namespace: 'adminOverview.tables',
    keys: REFERENCE_TABLES,
  },
  {
    // Every audit action the server writes, from DASHBOARD_AUDIT_ACTIONS in api/dashboardAuditActions.ts, which the
    // backend's DashboardAuditActionLabelTests holds to the server's own audit writes. The administrator's dashboard
    // labels its security counts, its sensitive changes and its recent activity feed from these, and the feed can
    // show any action a person wrote, so an action without a label here would print its key in both languages.
    // The keys are the actions with their dots turned into underscores, through the same function the screen uses.
    site: 'Admin dashboard - the security counts, the sensitive changes and the recent activity feed',
    namespace: 'dashboard.auditActions',
    keys: DASHBOARD_AUDIT_ACTIONS.map(dashboardAuditActionKey),
  },
  {
    // The needs-attention checks the server raises, from DASHBOARD_ATTENTION_CHECKS in api/adminDashboard.ts, which
    // mirrors DashboardNeedsAttentionChecks. Each item on the dashboard is labelled by its key, so a check without a
    // label here would print the key in both languages.
    site: 'Admin dashboard - each item under needs attention',
    namespace: 'adminDashboard.attention.items',
    keys: DASHBOARD_ATTENTION_KEYS,
  },
  {
    // The second line under each needs-attention item, saying what the item means for somebody.
    site: 'Admin dashboard - the hint under each item under needs attention',
    namespace: 'adminDashboard.attention.hints',
    keys: DASHBOARD_ATTENTION_KEYS,
  },
  {
    // The parts of the dashboard a check belongs to, named when a check could not run.
    site: 'Admin dashboard - the note naming the checks that could not run',
    namespace: 'adminDashboard.attention.groups',
    keys: DASHBOARD_ATTENTION_GROUPS,
  },
  {
    // The eight recurring jobs the system health section judges, and the verdicts it gives them.
    site: 'Admin dashboard - the scheduled jobs table, its job names',
    namespace: 'adminDashboard.health.jobs.names',
    keys: DASHBOARD_JOB_IDS,
  },
  {
    site: 'Admin dashboard - the scheduled jobs table, how often each job runs',
    namespace: 'adminDashboard.health.jobs.schedules',
    keys: [...new Set(Object.values(DASHBOARD_JOB_SCHEDULES))],
  },
  {
    site: 'Admin dashboard - the scheduled jobs table, its status chips',
    namespace: 'adminDashboard.health.jobs.verdicts',
    keys: DASHBOARD_JOB_VERDICTS,
  },
  {
    site: 'ReviewApplicationPage — the request-info checklist (MSP-77 field CODES, the wizard vocabulary)',
    namespace: 'onboarding.fields',
    keys: [
      'description', 'website', 'supplierGroup', 'currencyCode', 'primaryContactPhone',
      'legalInfo', 'address', 'contact', 'representative', 'branch', 'bankAccount', 'categoryLink', 'logo',
    ],
  },
]

function child(source: string, name: string): string {
  let depth = 0
  for (let i = 0; i < source.length; i += 1) {
    const ch = source[i]
    if (ch === '/' && source[i + 1] === '/') {
      const nl = source.indexOf('\n', i)
      if (nl === -1) break
      i = nl
      continue
    }
    if (ch === '/' && source[i + 1] === '*') {
      i = source.indexOf('*/', i) + 1
      continue
    }
    if (ch === "'" || ch === '"' || ch === '`') {
      const quote = ch
      i += 1
      while (i < source.length && source[i] !== quote) i += source[i] === '\\' ? 2 : 1
      continue
    }
    if (ch === '{') {
      depth += 1
      continue
    }
    if (ch === '}') {
      depth -= 1
      continue
    }
    if (depth !== 1) continue
    if (!source.startsWith(`${name}: {`, i)) continue

    // Found it at this level: return its balanced block.
    let inner = 0
    for (let j = source.indexOf('{', i); j < source.length; j += 1) {
      const c = source[j]
      if (c === '/' && source[j + 1] === '/') {
        const nl = source.indexOf('\n', j)
        if (nl === -1) break
        j = nl
        continue
      }
      if (c === "'" || c === '"' || c === '`') {
        const quote = c
        j += 1
        while (j < source.length && source[j] !== quote) j += source[j] === '\\' ? 2 : 1
        continue
      }
      if (c === '{') inner += 1
      else if (c === '}') {
        inner -= 1
        if (inner === 0) return source.slice(source.indexOf('{', i), j + 1)
      }
    }
    break
  }
  throw new Error(`no direct child named ${name}`)
}

function defines(fields: string, key: string): boolean {
  return new RegExp(`(^|[\\s,{])['"]?${key}['"]?\\s*:`).test(fields)
}

function namespaceFields(language: 'ar' | 'en', namespace: string): string {
  const from = language === 'en' ? CONFIG.lastIndexOf('en: {') : CONFIG.indexOf('ar: {')
  const resource = CONFIG.slice(from)
  const translation = child(resource, 'translation')
  const [parent, leaf] = namespace.split('.')
  return child(child(translation, parent), leaf)
}

describe('dynamic translation keys', () => {
  it('the sweep covers the sites it claims to', () => {
    expect(ENUMERABLE_SITES.length).toBeGreaterThanOrEqual(3)
    expect(ENUMERABLE_SITES.flatMap((s) => s.keys).length).toBeGreaterThanOrEqual(20)
  })

  for (const language of ['ar', 'en'] as const) {
    it(`every enumerable key resolves in ${language}`, () => {
      const missing: string[] = []
      for (const { site, namespace, keys } of ENUMERABLE_SITES) {
        const fields = namespaceFields(language, namespace)
        for (const key of keys) {
          if (!defines(fields, key)) {
            missing.push(`${namespace}.${key}  — built by ${site}`)
          }
        }
      }
      expect(missing, `these render as their own key on screen:\n  ${missing.join('\n  ')}`).toEqual([])
    })
  }

  it('the check can fail', () => {
    const fields = namespaceFields('en', 'profile.fields')
    expect(defines(fields, 'description')).toBe(true)
    expect(defines(fields, 'aFieldNobodyDefined')).toBe(false)
    expect(defines(namespaceFields('en', 'onboarding.fields'), 'defaultCurrency')).toBe(false)
    expect(defines(fields, 'defaultCurrency')).toBe(true)

    const tables = namespaceFields('en', 'adminOverview.tables')
    expect(defines(tables, 'document-types')).toBe(true)
    expect(defines(tables, 'units-of-weight')).toBe(false)
    expect(defines(tables, 'incoterm')).toBe(false)
    expect(defines(tables, 'types')).toBe(false)
    expect(defines(tables, 'measure')).toBe(false)

    const auditActions = namespaceFields('en', 'dashboard.auditActions')
    expect(defines(auditActions, dashboardAuditActionKey('supplier.erp_push_retried'))).toBe(true)
    expect(defines(auditActions, 'login_failure')).toBe(false)
  })

  it('no two audit actions share a label key', () => {
    const keys = DASHBOARD_AUDIT_ACTIONS.map(dashboardAuditActionKey)
    expect(new Set(DASHBOARD_AUDIT_ACTIONS).size).toBe(DASHBOARD_AUDIT_ACTIONS.length)
    expect(new Set(keys).size).toBe(keys.length)
    expect(keys.filter((key) => key.includes('.'))).toEqual([])
  })
})
