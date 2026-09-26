// Where a sign-in lands.
//
// THE CASE THIS WAS WRITTEN FOR is an administrator signing in with ?redirect=/dashboard still in the URL, left
// behind by having visited a supplier page while signed out. The redirect was followed, the supplier area
// refused an account with no supplier, and a correct sign-in ended on "403 Forbidden" with a Home link. It was
// reported from the screen, not from a test, which is the reason this file exists.
//
// BOTH DIRECTIONS ARE ASSERTED, because a rule that ignored every redirect would fix that report and quietly
// break the feature: a reviewer who followed a link to a tender must still arrive at the tender after signing
// in, not at their dashboard.
//
// THE UNKNOWN REDIRECT IS DELIBERATELY KEPT. This is not a permission check - it cannot be, from the login
// screen - so a staff redirect into a staff area is followed even when that particular role may be refused
// there. What is removed is only the case the login screen can decide for itself: an account being sent where
// its own half of the product does not reach.

import { describe, expect, it } from 'vitest'
import { EvaluatorHome, StaffHome, SupplierHome, loginDestination } from './loginDestination'

const supplier = { supplierId: 's-1', permissions: ['supplier.edit'] }
const admin = { supplierId: null, permissions: ['admin.users.manage', 'rfq.read'] }
const evaluator = { supplierId: null, permissions: ['evaluation.score'] }

describe('loginDestination', () => {
  it('sends an administrator to the back office when a stale supplier redirect is carried in', () => {
    expect(loginDestination(admin, '/dashboard')).toBe(StaffHome)
  })

  it('sends a supplier to their dashboard when a back-office redirect is carried in', () => {
    expect(loginDestination(supplier, '/back-office/reports')).toBe(SupplierHome)
  })

  it('still follows a redirect the account can actually reach', () => {
    expect(loginDestination(admin, '/back-office/reports')).toBe('/back-office/reports')
    expect(loginDestination(supplier, '/proposals')).toBe('/proposals')
  })

  it('sends each kind of caller to their own home when there is no redirect', () => {
    expect(loginDestination(supplier, undefined)).toBe(SupplierHome)
    expect(loginDestination(admin, undefined)).toBe(StaffHome)
    expect(loginDestination(evaluator, undefined)).toBe(EvaluatorHome)
  })

  it('treats the evaluation area as staff, because a supplier is refused there too', () => {
    expect(loginDestination(supplier, '/evaluation')).toBe(SupplierHome)
    expect(loginDestination(evaluator, '/evaluation')).toBe('/evaluation')
  })

  // A path that only starts with the same letters is a different area: /back-office-archive is not inside
  // /back-office, and matching on a bare prefix would send a supplier there to be refused.
  it('does not mistake a longer name for the area it starts with', () => {
    expect(loginDestination(admin, '/back-office-archive')).toBe(StaffHome)
    expect(loginDestination(supplier, '/back-office-archive')).toBe('/back-office-archive')
  })

  // An absolute URL in the redirect is somebody else's site. Following it would turn the login screen into an
  // open redirect, which is a phishing primitive: a link that genuinely signs the victim in and then hands
  // them to an attacker's page.
  it('refuses a redirect that leaves this product', () => {
    expect(loginDestination(admin, 'https://example.test/steal')).toBe(StaffHome)
    expect(loginDestination(supplier, '//example.test/steal')).toBe(SupplierHome)
  })

  it('ignores a redirect with no destination at all', () => {
    expect(loginDestination(admin, '')).toBe(StaffHome)
  })
})
