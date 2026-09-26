// Where a sign-in lands, given who signed in and where they were trying to go.
//
// THIS EXISTS BECAUSE A SUCCESSFUL LOGIN COULD END ON A 403. Visiting a page while signed out sends you to the
// login screen with ?redirect= pointing back at it, and that redirect was followed unconditionally. An
// administrator who had touched a supplier page - or who still had the parameter sitting in a bookmarked login
// URL - signed in correctly and landed on "403 Forbidden" with nothing but a Home link. Nothing had gone wrong:
// the supplier area refuses anyone without a supplier, which is exactly its job. The refusal was right and the
// destination was wrong.
//
// THE TWO AREAS ARE EXCLUSIVE, which is what makes this decidable without asking the server. A supplier is
// refused by /back-office and /evaluation; anyone without a supplier is refused by everything else behind a
// login. So a redirect into the other side's half is one this account cannot follow, and following it can only
// produce the dead end above.
//
// AN UNKNOWN REDIRECT IS STILL FOLLOWED for the account's own half. This is not a permission check and must not
// pretend to be one: a reviewer may be sent to a back-office page their role cannot open, and they will get a
// refusal from the guard that owns it. What this removes is the case where the account could never have
// followed the link, which is the one a login screen can see for itself.
//
// EVALUATORS GO TO THEIR OWN HOME because the back-office dashboard is not theirs to read: an evaluator holds
// scoring permissions and not the tender permissions that screen is built from.

export interface Claims {
  supplierId?: string | null
  permissions?: string[]
}

export const SupplierHome = '/dashboard'
export const StaffHome = '/back-office/dashboard'
export const EvaluatorHome = '/evaluation'

const StaffAreas = ['/back-office', '/evaluation']

export function homeFor(claims: Claims | null): string {
  if (claims?.supplierId) return SupplierHome

  const permissions = claims?.permissions ?? []
  const isEvaluator = permissions.includes('evaluation.score') && !permissions.includes('rfq.read')

  return isEvaluator ? EvaluatorHome : StaffHome
}

// A redirect that leaves this product is refused outright, and that is a security fix rather than tidiness.
// The parameter used to be followed as given, so a link carrying ?redirect=//attacker.example produced a real
// sign-in followed by a hand-off to somebody else's page - an open redirect, which is the shape a phishing
// campaign wants precisely because everything about it is genuine until the last step.
//
// A leading slash is not enough to prove a path is ours: // is protocol-relative and /\ is read the same way by
// browsers, so both are treated as absolute. Only a single leading slash counts.
function isInternal(redirect: string | undefined): redirect is string {
  return redirect !== undefined
    && redirect.startsWith('/')
    && !redirect.startsWith('//')
    && !redirect.startsWith('/\\')
}

export function loginDestination(claims: Claims | null, redirect: string | undefined): string {
  const home = homeFor(claims)

  if (!isInternal(redirect)) return home

  const isStaffArea = StaffAreas.some(area => redirect === area || redirect.startsWith(`${area}/`))

  return isStaffArea === !claims?.supplierId ? redirect : home
}
