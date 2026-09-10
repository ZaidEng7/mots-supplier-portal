import {
  Activity, BarChart3, Bell, Briefcase, Building2, ChartColumn, CircleHelp, ClipboardCheck,
  ClipboardList, Clock, Database, FileText, Handshake, KeyRound, Landmark, LayoutDashboard,
  LayoutGrid, List, Mail, ScrollText, Search, Settings, ShieldCheck, SlidersHorizontal, Trophy,
  User, UserCog, Users,
} from 'lucide-react'
import type { LucideProps } from 'lucide-react'
import type { ComponentType } from 'react'

/**
 * Where each shell can go, said once.
 *
 * <p><b>The defect this closes.</b> The back office listed thirty-one destinations as one wrapping row
 * of identically-coloured links, with no grouping and no current-page marker, and three of those
 * screens had already shipped permissioned and unreachable because nothing linked to them - found by
 * hand, twice. The links lived inside the shell's JSX, so the only way to ask "can this route be
 * reached" was to read a three-hundred-line component and hold the answer in your head.</p>
 *
 * <p>Navigation is data here. The shell renders this list and `reachability.test.tsx` audits it, so the
 * two cannot disagree: a route that no shell offers fails the guard unless it is written into
 * {@link ROUTE_EXEMPTIONS} with a reason a person can argue with.</p>
 *
 * <p><b>Hide, never gate.</b> Every `when` below decides visibility only. The API re-enforces the same
 * permission on every endpoint behind these routes, so a hidden link is a tidier surface and never a
 * security boundary. Two of them also ask whether the account belongs to a buying body: BRULE-029
 * scopes procurement by organization, and the two personas that have none - the bootstrap
 * administrator and the Ministry viewer - would otherwise be offered a screen that can only answer 404.</p>
 */
export interface NavContext {
  /** True when the signed-in account holds the permission. */
  can: (permission: string) => boolean
  /** BRULE-029: whether this account is scoped to a buying body at all. */
  inABuyingBody: boolean
}

export interface NavItem {
  /** The route this row goes to. Matched against the router's own paths by the reachability guard. */
  to: string
  /** i18n key for the row's label. Never a literal: both languages ship every phase. */
  labelKey: string
  icon: ComponentType<LucideProps>
  /**
   * Match the current path exactly rather than by prefix. Needed only where one destination's path is
   * a prefix of another's, which would otherwise light up two rows at once.
   */
  exact?: boolean
  /** Visibility only. Absent means every account that reaches this shell sees the row. */
  when?: (context: NavContext) => boolean
}

export interface NavGroup {
  /**
   * i18n key for the group's heading, or absent for the opening group, which carries the two
   * destinations that answer "where am I" and needs no name to do it.
   */
  headingKey?: string
  items: readonly NavItem[]
}

const inBuyingBody = (permission: string) => (context: NavContext) =>
  context.can(permission) && context.inABuyingBody

const holds = (permission: string) => (context: NavContext) => context.can(permission)

/**
 * The back office, grouped by the question a member of staff arrived with rather than by the team that
 * built each screen. Every gate below is the one the flat row already applied, moved rather than
 * rewritten - this phase changes how the product reads, not who can see what.
 */
export const BACK_OFFICE_NAV: readonly NavGroup[] = [
  {
    items: [
      { to: '/back-office/dashboard', labelKey: 'nav.dashboard', icon: LayoutDashboard },
      // Ungated, like the route: the server decides what each persona can find.
      { to: '/back-office/search', labelKey: 'search.title', icon: Search },
      { to: '/back-office/reports', labelKey: 'reports.title', icon: ChartColumn, when: holds('report.read') },
    ],
  },
  {
    headingKey: 'nav.groupTenders',
    items: [
      // rfq.read rather than rfq.create: procurement_manager approves tenders without authoring them,
      // so keying this on the authoring permission hid the section from the role whose job is to open it.
      { to: '/back-office/rfqs', labelKey: 'rfq.title', icon: List, when: inBuyingBody('rfq.read') },
      { to: '/back-office/procurement', labelKey: 'procurementDashboard.title', icon: Handshake, when: inBuyingBody('rfq.read') },
      {
        to: '/back-office/evaluation-templates', labelKey: 'evaluationTemplates.title',
        icon: ClipboardList, when: inBuyingBody('evaluation.template.manage'),
      },
      { to: '/evaluation', labelKey: 'evaluationDashboard.title', icon: ClipboardCheck, when: holds('evaluation.score') },
    ],
  },
  {
    headingKey: 'nav.groupSuppliers',
    items: [
      { to: '/back-office/suppliers', labelKey: 'supplierDirectory.title', icon: Users, when: inBuyingBody('supplier.directory.read') },
      { to: '/back-office/review', labelKey: 'review.title', icon: Clock, when: holds('supplier.review'), exact: true },
      { to: '/back-office/review-dashboard', labelKey: 'reviewDashboard.title', icon: BarChart3, when: holds('supplier.review') },
      { to: '/back-office/review/suppliers', labelKey: 'complianceDirectory.title', icon: ShieldCheck, when: holds('supplier.review') },
      { to: '/back-office/offerings', labelKey: 'offeringSearch.title', icon: Briefcase, when: inBuyingBody('offering.search') },
    ],
  },
  {
    headingKey: 'nav.groupMinistry',
    items: [
      // governance.read is the only permission ministry_viewer holds. Without these rows that persona
      // has to type the address: every other destination in this shell answers 403 for it.
      { to: '/back-office/ministry', labelKey: 'ministry.title', icon: Landmark, when: holds('governance.read'), exact: true },
      { to: '/back-office/ministry/categories', labelKey: 'categoryCoverage.title', icon: LayoutGrid, when: holds('governance.read') },
      { to: '/back-office/ministry/rfqs', labelKey: 'ministryRfqs.title', icon: FileText, when: holds('governance.read') },
      { to: '/back-office/ministry/suppliers', labelKey: 'ministrySuppliers.title', icon: Building2, when: holds('governance.read') },
      { to: '/back-office/ministry/awards', labelKey: 'ministryAwards.title', icon: Trophy, when: holds('governance.read') },
    ],
  },
  {
    headingKey: 'nav.groupAdministration',
    items: [
      { to: '/back-office/organizations', labelKey: 'organizations.title', icon: Building2, when: holds('admin.organizations.manage') },
      { to: '/back-office/staff', labelKey: 'staff.title', icon: UserCog, when: holds('admin.users.manage') },
      { to: '/back-office/roles', labelKey: 'roleManagement.title', icon: KeyRound, when: holds('admin.roles.manage') },
      // Its own permission rather than admin.users.manage: the two are separately grantable, and a role
      // that edits code lists need not administer accounts.
      { to: '/back-office/reference', labelKey: 'referenceAdmin.title', icon: Database, when: holds('reference.manage') },
      { to: '/back-office/notification-templates', labelKey: 'notificationTemplates.title', icon: Bell, when: holds('admin.users.manage') },
      { to: '/back-office/email-templates', labelKey: 'emailTemplates.title', icon: Mail, when: holds('admin.users.manage') },
      { to: '/back-office/ui-strings', labelKey: 'uiStrings.title', icon: ScrollText, when: holds('admin.users.manage') },
      { to: '/back-office/settings', labelKey: 'systemSettings.title', icon: SlidersHorizontal, when: holds('admin.users.manage') },
      { to: '/back-office/audit', labelKey: 'auditExplorer.title', icon: ScrollText, when: holds('audit.read') },
      { to: '/back-office/operations', labelKey: 'operations.title', icon: Activity, when: holds('admin.users.manage') },
      { to: '/back-office/admin', labelKey: 'adminOverview.title', icon: Settings, when: holds('admin.users.manage'), exact: true },
    ],
  },
]

/**
 * The supplier's side. Two groups, which is the shape §D3 arrived at and this phase keeps: a supplier
 * arriving to bid should not have to read the company-profile links to find the tender ones.
 */
export const SUPPLIER_NAV: readonly NavGroup[] = [
  {
    headingKey: 'nav.groupBidding',
    items: [
      { to: '/dashboard', labelKey: 'nav.dashboard', icon: LayoutDashboard, exact: true },
      { to: '/rfqs', labelKey: 'nav.rfqs', icon: List },
      { to: '/proposals', labelKey: 'nav.proposals', icon: FileText },
    ],
  },
  {
    headingKey: 'nav.groupCompany',
    items: [
      { to: '/onboarding', labelKey: 'nav.onboarding', icon: ClipboardCheck },
      { to: '/profile', labelKey: 'nav.profile', icon: Building2 },
      { to: '/documents', labelKey: 'nav.documents', icon: ScrollText },
      { to: '/offerings', labelKey: 'nav.offerings', icon: Briefcase },
      { to: '/team', labelKey: 'nav.team', icon: Users },
    ],
  },
]

/**
 * Account chrome: reachable from the top bar rather than the sidebar, because it is not on the path
 * between a supplier and a tender or between an officer and a queue. The reachability guard counts
 * these, so moving a destination here is still reaching it - it is not a way to hide one from the audit.
 */
export const BACK_OFFICE_CHROME: readonly NavItem[] = [
  { to: '/back-office/account', labelKey: 'nav.account', icon: User, exact: true },
  { to: '/back-office/account/notifications', labelKey: 'notificationPreferences.title', icon: Bell },
  { to: '/back-office/help', labelKey: 'help.title', icon: CircleHelp },
]

export const SUPPLIER_CHROME: readonly NavItem[] = [
  { to: '/settings', labelKey: 'nav.settings', icon: Settings, exact: true },
  { to: '/settings/notifications', labelKey: 'notificationPreferences.title', icon: Bell },
  { to: '/help', labelKey: 'help.title', icon: CircleHelp },
]

/**
 * Routes that no navigation offers, each with the reason it does not.
 *
 * <p>A list of paths with no reasons beside them would pass the guard and teach nobody anything, which
 * is how three screens shipped unreachable in the first place. Every entry here is a claim that a
 * person can disagree with, and the guard checks that each one is long enough to be a claim rather
 * than a shrug.</p>
 */
export const ROUTE_EXEMPTIONS: Readonly<Record<string, string>> = {
  '/': 'The landing page. It is where an unauthenticated visitor arrives, so nothing signed in links to it.',
  '/about': 'Linked from the landing page and the footer, which are outside both shells.',
  '/login': 'Reached by signing out or by arriving unauthenticated. A signed-in shell offering it would be offering a page that redirects.',
  '/register': 'Linked from the landing page and from the login screen, both outside the shells.',
  '/forgot-password': 'Linked from the login screen, which no shell wraps.',
  '/reset-password': 'Reached only from the link in a password-reset email, which carries the token that makes the page work.',
  '/verify-email': 'Reached only from the link in a verification email, for the same reason.',
  '/accept-invite': 'Reached only from a team invitation email. The token in the address is what identifies the invitation.',
  '/accept-staff-invite': 'Reached only from a staff invitation email, same shape.',
  '/notifications': 'Opened from the bell in the top bar, which is a control rather than a navigation row, and is present on every screen in both shells.',
  '/onboarding/contacts': 'A step inside the onboarding wizard, reached from the wizard itself rather than from the sidebar, which offers the wizard.',
  '/onboarding/addresses': 'An onboarding step, reached from the wizard that owns it, for the same reason as the contacts step above.',
  '/onboarding/banking': 'An onboarding step, reached from the wizard that owns it, for the same reason as the contacts step above.',
  '/onboarding/offerings': 'An onboarding step, reached from the wizard that owns it, for the same reason as the contacts step above.',
  '/back-office/procurement/approvals': 'Opened from the procurement dashboard, which is the screen that says how many approvals are waiting.',
  '/back-office/notifications': 'The back office bell opens this, and the bell is on every screen in the shell. Same shape as the supplier side.',
  '/back-office': 'The layout that wraps every back-office screen. It renders an Outlet and nothing of its own, so it is a container rather than a destination, and the shell points home at the dashboard instead.',
}

/**
 * Routes carrying a parameter, which no navigation row can link to because the row would have to
 * invent an identifier. Every one of these is opened from a list or a workspace that already holds
 * the identifier, and Phase D is where those lists are rebuilt.
 */
export const PARAMETERISED_ROUTE = /\$[A-Za-z]/
