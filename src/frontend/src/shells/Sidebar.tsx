import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { Icon } from '../components/ui/Icon'
import type { NavContext, NavGroup, NavItem } from './navigation'

/**
 * Whether a row is the page you are on.
 *
 * <p>Prefix rather than equality, so a step inside a section still lights the section that owns it -
 * `/onboarding/banking` marks Complete profile, `/back-office/rfqs/RFQ-1/award` marks Tenders. `exact`
 * is for the handful of destinations whose path is a prefix of another's, which would otherwise light
 * two rows at once and tell the reader nothing.</p>
 */
export function isCurrent(item: NavItem, pathname: string): boolean {
  return item.exact ? pathname === item.to : pathname === item.to || pathname.startsWith(`${item.to}/`)
}

/** The rows a given account can see, with empty groups dropped rather than rendered as bare headings. */
export function visibleGroups(groups: readonly NavGroup[], context: NavContext): NavGroup[] {
  return groups
    .map((group) => ({ ...group, items: group.items.filter((item) => item.when?.(context) ?? true) }))
    .filter((group) => group.items.length > 0)
}

/**
 * Every destination this account can reach, grouped and named.
 *
 * <p>One `nav` landmark per group rather than one for the whole list: §D3 settled that a screen-reader
 * user should be able to jump between the named groups the same way a sighted reader jumps between the
 * headings, and a single landmark holding thirty-one rows is the flat list again with a border drawn
 * round it.</p>
 */
export function NavGroups({ groups, context, pathname, onNavigate }: Readonly<{
  groups: readonly NavGroup[]
  context: NavContext
  pathname: string
  /** Called after a row is followed, so the narrow-viewport disclosure can close itself. */
  onNavigate?: () => void
}>) {
  const { t } = useTranslation()

  return (
    <>
      {visibleGroups(groups, context).map((group) => {
        const label = t(group.headingKey ?? 'nav.groupOverview')
        return (
          <nav key={group.headingKey ?? 'overview'} aria-label={label} className="flex flex-col gap-px">
            {group.headingKey ? (
              <h2
                className="m-0 mb-1.5 px-2 text-[length:var(--text-caption)] font-[var(--fw-semibold)] uppercase tracking-[0.11em]"
                style={{ color: 'var(--color-on-field-muted)' }}
              >
                {label}
              </h2>
            ) : null}
            {group.items.map((item) => {
              const current = isCurrent(item, pathname)
              return (
                <Link
                  key={item.to}
                  to={item.to as never}
                  onClick={onNavigate}
                  aria-current={current ? 'page' : undefined}
                  className="flex items-center gap-2.5 rounded-[var(--radius-sm)] px-2 py-[7px] text-[length:var(--text-body-sm)] no-underline"
                  style={{
                    color: current ? 'var(--color-on-field)' : 'var(--color-on-field-muted)',
                    backgroundColor: current ? 'var(--color-field-active)' : undefined,
                    fontWeight: current ? 'var(--fw-medium)' : undefined,
                  }}
                >
                  <span style={{ color: current ? 'var(--color-brand-solid-active)' : 'var(--color-on-field-muted)' }}>
                    <Icon as={item.icon} />
                  </span>
                  {t(item.labelKey)}
                </Link>
              )
            })}
          </nav>
        )
      })}
    </>
  )
}

/**
 * The sidebar itself: the mark, every destination, and who you are signed in as.
 *
 * <p>It replaces a wrapping row of up to thirty-one identically-coloured links that had no grouping and
 * no current-page marker. The rail is dark in both themes, which is what tells a member of staff at a
 * glance that they are on the internal side of the product rather than the supplier side.</p>
 */
export function Sidebar({ groups, context, pathname, title, subtitle, account }: Readonly<{
  groups: readonly NavGroup[]
  context: NavContext
  pathname: string
  title: string
  subtitle: string
  /**
   * Who is signed in, shown at the foot. Absent while there is no session.
   *
   * <p>The email, because that is what the token actually carries: `AuthClaims` holds a user id, an
   * email and a permission list, and no display name or role name. A foot that read "Procurement
   * officer" would be inventing it from the permission list, and the two can disagree.</p>
   */
  account?: { email: string }
}>) {
  return (
    <aside
      className="sticky top-0 hidden h-screen shrink-0 flex-col overflow-y-auto md:flex"
      style={{
        inlineSize: 'var(--sidebar-width)',
        backgroundColor: 'var(--color-field)',
        color: 'var(--color-on-field)',
      }}
    >
      <div className="flex items-center gap-2.5 px-4.5 pb-4.5 pt-5">
        <span
          aria-hidden="true"
          className="grid size-[30px] shrink-0 place-items-center rounded-[var(--radius-sm)] text-[length:var(--text-body-sm)] font-[var(--fw-bold)]"
          style={{ backgroundColor: 'var(--color-brand-solid)', color: 'var(--color-on-brand)' }}
        >
          {title.trim().charAt(0)}
        </span>
        <span className="min-w-0 text-[length:var(--text-body-sm)] font-[var(--fw-semibold)] leading-tight">
          {title}
          <small className="block font-[var(--fw-regular)] text-[length:var(--text-caption)]" style={{ color: 'var(--color-on-field-muted)' }}>
            {subtitle}
          </small>
        </span>
      </div>

      <div className="flex flex-col gap-4.5 px-2.5 pb-5">
        <NavGroups groups={groups} context={context} pathname={pathname} />
      </div>

      {account ? (
        <div
          className="mt-auto flex items-center gap-2.5 px-4.5 py-3.5"
          style={{ borderBlockStart: '1px solid var(--color-field-raised)' }}
        >
          <span
            aria-hidden="true"
            className="grid size-7 shrink-0 place-items-center rounded-full text-[length:var(--text-caption)] font-[var(--fw-semibold)]"
            style={{ backgroundColor: 'var(--color-field-active)', color: 'var(--color-on-field)' }}
          >
            {account.email.trim().charAt(0).toUpperCase()}
          </span>
          <span className="min-w-0 truncate text-[length:var(--text-caption)] leading-tight" title={account.email}>
            {account.email}
          </span>
        </div>
      ) : null}
    </aside>
  )
}
