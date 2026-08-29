/**
 * Which FR-ONB-009 lifecycle actions a reviewer may see for a supplier's current state.
 *
 * This MIRRORS the domain's rules; it does not replace them. The endpoints reject an illegal
 * transition with 409 and the domain's own message regardless of what the UI offers, so hiding a
 * button is a convenience for the reviewer, not the enforcement (NFR-CMP-003/BRULE-097).
 *
 * Extracted from the page so the gating can be asserted without rendering. The rules are small
 * enough to look obviously right and exactly the kind of thing that silently goes wrong when a
 * fourth state is added - Deactivated offering a Reactivate button would be a one-word mistake
 * that reads fine in a diff.
 */
export interface LifecycleActions {
  canSuspend: boolean
  canReactivate: boolean
  canDeactivate: boolean
}

export function lifecycleActionsFor(lifecycleState: string): LifecycleActions {
  return {
    canSuspend: lifecycleState === 'Active',
    canReactivate: lifecycleState === 'Suspended',
    // Deactivation is reachable only from Suspended, matching the domain: a direct
    // Active -> Deactivated path would make an irreversible action a single click on a live
    // supplier.
    canDeactivate: lifecycleState === 'Suspended',
  }
}
