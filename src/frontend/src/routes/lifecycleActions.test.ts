import { describe, expect, it } from 'vitest'
import { lifecycleActionsFor } from './lifecycleActions'

/**
 * MSP-63: the reviewer's lifecycle action gating.
 *
 * Every lifecycle state is enumerated, including the ones where the answer is "no actions". That
 * exhaustiveness is deliberate - the same technique that caught a missing onboarding state in the
 * eligibility theory. A state the table does not mention would otherwise inherit whatever the
 * boolean expressions happen to do.
 */
describe('lifecycleActionsFor', () => {
  it('offers only suspension on an active supplier', () => {
    expect(lifecycleActionsFor('Active')).toEqual({
      canSuspend: true,
      canReactivate: false,
      canDeactivate: false,
    })
  })

  it('offers reactivate and deactivate on a suspended supplier', () => {
    expect(lifecycleActionsFor('Suspended')).toEqual({
      canSuspend: false,
      canReactivate: true,
      canDeactivate: true,
    })
  })

  it('offers nothing on a deactivated supplier, because it is terminal', () => {
    // The assertion that matters most. Deactivated is terminal in the domain; a Reactivate button
    // here would promise the reviewer something the server will refuse with 409.
    expect(lifecycleActionsFor('Deactivated')).toEqual({
      canSuspend: false,
      canReactivate: false,
      canDeactivate: false,
    })
  })

  it('offers nothing before the lifecycle has begun', () => {
    // 'None' is an application that has not been approved yet. Suspending something that was never
    // active is meaningless, and the domain refuses it.
    expect(lifecycleActionsFor('None')).toEqual({
      canSuspend: false,
      canReactivate: false,
      canDeactivate: false,
    })
  })

  it('offers nothing for an unrecognised state', () => {
    // Fails closed. If the backend adds a lifecycle state the SPA has not learned about, the
    // reviewer sees no actions rather than being offered one that cannot work.
    expect(lifecycleActionsFor('SomethingNewFromTheBackend')).toEqual({
      canSuspend: false,
      canReactivate: false,
      canDeactivate: false,
    })
  })

  it('never offers suspend and reactivate at the same time', () => {
    // They are opposites; offering both would mean the state is neither.
    for (const state of ['Active', 'Suspended', 'Deactivated', 'None']) {
      const actions = lifecycleActionsFor(state)
      expect(actions.canSuspend && actions.canReactivate).toBe(false)
    }
  })
})
