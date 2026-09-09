import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { errorDetail, ProblemError } from './problem'
import { AwardApiError } from './awards'
import { ComparisonApiError } from './comparison'
import { DashboardApiError } from './dashboards'
import { DocumentApiError } from './documents'
import { EvaluationApiError } from './evaluations'
import { EvaluationTemplateApiError } from './evaluationTemplates'
import { MyEvaluationsApiError } from './myEvaluations'
import { NotificationApiError } from './notifications'
import { OrganizationApiError } from './organizations'
import { ProposalApiError } from './proposals'
import { ReviewApiError } from './review'
import { RfqApiError } from './rfqs'
import { SettingsApiError } from './settings'
import { SupplierApiError } from './supplier'
import { SupplierDashboardApiError } from './supplierDashboard'
import { SupplierDirectoryApiError } from './supplierDirectory'
import { SupplierRfqApiError } from './supplierRfqs'
import { WorkspaceApiError } from './workspace'

/**
 * Eighteen API modules used to declare eighteen identical constructors. They now share `ProblemError`,
 * and this is the test that says the collapse did not change any of them.
 *
 * <p><b>Why it is one table rather than eighteen files.</b> The behaviour under test is the same
 * behaviour in every module — that is the whole reason the base class exists. Testing it once per module
 * would restate the duplication the refactor removed. What each row asserts is that this class still
 * carries the contract, so a subclass that quietly stops extending the base fails here.</p>
 *
 * <p><b>The distinction the read paths depend on.</b> `errorDetail` renders a message only when the
 * server actually wrote prose. A bare 503 makes `problemMessage` fall back to "Request failed: 503",
 * which is developer text and must never reach a supplier — so `isProblemError` is false for it, and the
 * screen shows the generic string instead. That is the case worth getting wrong, so every class is
 * checked for it.</p>
 */
const CLASSES: [string, new (status: number, body: unknown) => ProblemError][] = [
  ['AwardApiError', AwardApiError],
  ['ComparisonApiError', ComparisonApiError],
  ['DashboardApiError', DashboardApiError],
  ['DocumentApiError', DocumentApiError],
  ['EvaluationApiError', EvaluationApiError],
  ['EvaluationTemplateApiError', EvaluationTemplateApiError],
  ['MyEvaluationsApiError', MyEvaluationsApiError],
  ['NotificationApiError', NotificationApiError],
  ['OrganizationApiError', OrganizationApiError],
  ['ProposalApiError', ProposalApiError],
  ['ReviewApiError', ReviewApiError],
  ['RfqApiError', RfqApiError],
  ['SettingsApiError', SettingsApiError],
  ['SupplierApiError', SupplierApiError],
  ['SupplierDashboardApiError', SupplierDashboardApiError],
  ['SupplierDirectoryApiError', SupplierDirectoryApiError],
  ['SupplierRfqApiError', SupplierRfqApiError],
  ['WorkspaceApiError', WorkspaceApiError],
]

describe('every API error type carries the ProblemError contract', () => {
  it('covers every class that extends it', () => {
    // The denominator, read from the source rather than counted off the list above - otherwise a
    // nineteenth error class could join the codebase, be tested by nothing, and this file would still
    // report full coverage of "every" class.
    const api = resolve(process.cwd(), 'src/api')
    const declared = readdirSync(api)
      .filter((f) => f.endsWith('.ts') && !f.includes('.test.'))
      .flatMap((f) => [...readFileSync(join(api, f), 'utf8').matchAll(/export class (\w+) extends ProblemError\b/g)]
        .map((m) => m[1]))

    expect(declared.sort()).toEqual(CLASSES.map(([name]) => name).sort())
  })

  it.each(CLASSES)('%s shows the server explanation when there is one', (_name, Cls) => {
    const err = new Cls(409, { detail: 'The submission window has closed.', title: 'Conflict', status: 409 })

    expect(err).toBeInstanceOf(ProblemError)
    expect(err.status).toBe(409)
    expect(err.message).toBe('The submission window has closed.')
    expect(err.isProblemError).toBe(true)
    expect(errorDetail(err)).toBe('The submission window has closed.')
  })

  it.each(CLASSES)('%s falls back to title when there is no detail', (_name, Cls) => {
    const err = new Cls(403, { title: 'Forbidden', status: 403 })

    expect(err.message).toBe('Forbidden')
    expect(err.isProblemError).toBe(true)
  })

  it.each(CLASSES)('%s refuses to hand a reader developer text', (_name, Cls) => {
    // A bare failure with no problem document at all: the message becomes "Request failed: 503", which
    // is for a log and not for a supplier.
    const err = new Cls(503, null)

    expect(err.message).toBe('Request failed: 503')
    expect(err.isProblemError).toBe(false)
    expect(errorDetail(err)).toBeNull()
  })
})
