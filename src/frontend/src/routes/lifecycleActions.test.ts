// MSP-63: the reviewer's lifecycle action gating.
//
// Every lifecycle state is enumerated, including the ones where the answer is "no actions". That exhaustiveness is
// deliberate - the same technique that caught a missing onboarding state in the eligibility theory - because a state the
// table does not mention would otherwise inherit whatever the boolean expressions happen to do.
//
// An active supplier is offered only suspension; a suspended one reactivate and deactivate. A deactivated one is
// offered NOTHING, and that is the assertion that matters most: Deactivated is terminal in the domain, so a Reactivate
// button here would promise the reviewer something the server will refuse with 409.
//
// 'None' is an application that has not been approved yet, and it is offered nothing either: suspending something that
// was never active is meaningless, and the domain refuses it. An unrecognised state fails CLOSED - if the backend adds a
// lifecycle state the SPA has not learned about, the reviewer sees no actions rather than being offered one that cannot
// work.
//
// And suspend and reactivate are never offered at the same time: they are opposites, and offering both would mean the
// state is neither.

import { describe, expect, it } from 'vitest'
import { lifecycleActionsFor } from './lifecycleActions'

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
    expect(lifecycleActionsFor('Deactivated')).toEqual({
      canSuspend: false,
      canReactivate: false,
      canDeactivate: false,
    })
  })

  it('offers nothing before the lifecycle has begun', () => {
    expect(lifecycleActionsFor('None')).toEqual({
      canSuspend: false,
      canReactivate: false,
      canDeactivate: false,
    })
  })

  it('offers nothing for an unrecognised state', () => {
    expect(lifecycleActionsFor('SomethingNewFromTheBackend')).toEqual({
      canSuspend: false,
      canReactivate: false,
      canDeactivate: false,
    })
  })

  it('never offers suspend and reactivate at the same time', () => {
    for (const state of ['Active', 'Suspended', 'Deactivated', 'None']) {
      const actions = lifecycleActionsFor(state)
      expect(actions.canSuspend && actions.canReactivate).toBe(false)
    }
  })
})
