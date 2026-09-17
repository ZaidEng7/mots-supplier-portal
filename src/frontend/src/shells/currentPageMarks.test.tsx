// Under the real router, the only links marked as the current page are the ones the navigation itself marks.
//
// The defect this closes. TanStack's Link works out whether it is active and, when it is, writes aria-current="page" onto
// the anchor AFTER every prop the caller passed, so a component's own aria-current cannot take that mark away. By default
// "active" means the current path starts with the link's target. The breadcrumb's section crumb became a link so that a page
// below a section offered a way up, and on /back-office/rfqs/RFQ-1/award that link still told a screen reader Tenders was the
// page you were on. The rail had the same fault. It lit My evaluations alone on an evaluator's scoring screen and announced
// Tenders as current beside it, and it announced the `exact` Review queue row on the compliance directory. The account links
// announced Account on its own notification settings, and the tender strip announced its Tender tab from inside every other
// tab. Every one of those links now asks the router to match exactly.
//
// Why a file of its own. TopBar.test, Sidebar.test and TenderTabs.test replace Link with a plain anchor. That is what lets them
// assert which row a component chooses, and it is also why none of them could see this: the stub has no router and adds
// nothing. vi.mock applies to a whole file, so the real Link needs a file where it is not mocked. The router here is built by
// hand - a root that draws the real Sidebar, TopBar and, on a tender screen, TenderTabs, and one splat route so every address
// matches - because what is under test is the Link and not the app's route tree. The bell and the language switch are replaced,
// because both fetch and neither draws a navigation link. Every fetch is left unanswered, so the strip's counts stay unknown
// and nothing else about the page changes.
//
// THE DENOMINATOR is every row of the real back-office and supplier navigation and both account lists, each at its own path
// and one level below it, every tender screen the frame covers, and the evaluator's brief, which the My evaluations row claims
// by pattern. Every permission is granted, so every row, account link and tab is drawn. The list of pages is asserted against
// the navigation before any page is read. Each page is its own case, so a failure names the page, and each first checks that
// every surface it draws - the sidebar, the narrow-width menu, the account links, the trail and, on a tender screen, all seven
// tabs - is on the page with links in it, so a surface that never rendered cannot pass by having nothing to mark. The trail's
// one exception is the shell's own front page, where it is a single span.
//
// THE RULE compares, on each page, every anchor carrying aria-current="page" with what the components' own rules choose: the
// rows isCurrent lights once isClaimed is applied, in the sidebar and again in the menu; the account links isCurrent lights,
// in the header and again in the menu; the tab whose address is the page; and nothing in the trail, where the current page is
// a span and never a link.
//
// THE CONTROL renders the crumb as it was before this fix, under the same kind of router: a Link to Tenders with the default
// activeOptions, inside the trail's landmark, on the award screen. The check must report it. The same Link matching exactly
// must report nothing. The first half proves that this router really adds the mark and that the check can see it.

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Link,
  RouterProvider,
  useRouterState,
} from '@tanstack/react-router'
import { screen } from '@testing-library/react'
import { renderPage } from '../test/renderPage'
import { TENDER_CODE, TENDER_FRAME_SCREENS } from '../routes/back-office/rfq/tenderTestHarness'
import type { NavContext, NavGroup, NavItem } from './navigation'

vi.mock('../components/NotificationBell', () => ({ NotificationBell: () => null }))
vi.mock('../components/LanguageSwitch', () => ({ LanguageSwitch: () => null }))

const { TopBar } = await import('./TopBar')
const { Sidebar, isClaimed, isCurrent, visibleGroups } = await import('./Sidebar')
const { TenderTabs } = await import('../routes/back-office/rfq/TenderTabs')
const { BACK_OFFICE_CHROME, BACK_OFFICE_NAV, SUPPLIER_CHROME, SUPPLIER_NAV } = await import('./navigation')
const { useAuthStore } = await import('../lib/authStore')
const i18n = (await import('../i18n/config')).default

interface Shell {
  groups: readonly NavGroup[]
  chrome: readonly NavItem[]
  home: { to: string; label: string }
}

interface Page {
  shell: Shell
  path: string
  tabs: boolean
}

type Surface = 'sidebar' | 'menu' | 'account' | 'trail' | 'tabs'

const BACK_OFFICE: Shell = { groups: BACK_OFFICE_NAV, chrome: BACK_OFFICE_CHROME, home: { to: '/back-office/dashboard', label: 'Home' } }
const SUPPLIER: Shell = { groups: SUPPLIER_NAV, chrome: SUPPLIER_CHROME, home: { to: '/dashboard', label: 'Home' } }

const EVERYTHING: NavContext = { can: () => true, inABuyingBody: true }
const TAB_PERMISSIONS = ['rfq.read', 'comparison.view', 'evaluation.score', 'award.recommend', 'evaluation.open']

const TENDER_SCREENS = TENDER_FRAME_SCREENS.map((path) => path.replace('$referenceCode', TENDER_CODE))
const BRIEF = `/back-office/rfqs/${TENDER_CODE}/brief`

const ROWS = [BACK_OFFICE, SUPPLIER].flatMap((shell) =>
  [...shell.groups.flatMap((group) => group.items), ...shell.chrome].map((row) => ({ shell, row })))

const PAGES: Page[] = [
  ...ROWS.flatMap(({ shell, row }) => [
    { shell, path: row.to, tabs: false },
    { shell, path: `${row.to}/X-1`, tabs: false },
  ]),
  ...TENDER_SCREENS.map((path) => ({ shell: BACK_OFFICE, path, tabs: true })),
  { shell: BACK_OFFICE, path: BRIEF, tabs: false },
]

function renderUnderRouter(path: string, Navigation: () => ReactNode) {
  const root = createRootRoute({ component: Navigation })
  const everywhere = createRoute({ getParentRoute: () => root, path: '$', component: () => null })
  const router = createRouter({
    routeTree: root.addChildren([everywhere]),
    history: createMemoryHistory({ initialEntries: [path] }),
  })
  return renderPage(<RouterProvider router={router} />)
}

function navigationFor({ shell, tabs }: Page) {
  return function Navigation() {
    const pathname = useRouterState({ select: (state) => state.location.pathname })
    return (
      <>
        <Sidebar groups={shell.groups} context={EVERYTHING} pathname={pathname} title="Title" subtitle="Subtitle" />
        <TopBar
          groups={shell.groups} chrome={shell.chrome} context={EVERYTHING} pathname={pathname}
          home={shell.home} onLogout={() => {}}
        />
        {tabs ? <TenderTabs referenceCode={TENDER_CODE} /> : null}
      </>
    )
  }
}

function surfaceOf(anchor: Element): Surface {
  if (anchor.closest('aside')) return 'sidebar'
  const label = anchor.closest('nav')?.getAttribute('aria-label')
  if (label === i18n.t('nav.breadcrumb')) return 'trail'
  if (label === i18n.t('rfq.tabs.label')) return 'tabs'
  if (label === i18n.t('nav.groupAccount')) return 'account'
  return 'menu'
}

const surfacesWithLinks = (container: HTMLElement) =>
  [...new Set(Array.from(container.querySelectorAll('a')).map(surfaceOf))].sort()

const markedLinks = (container: HTMLElement) =>
  Array.from(container.querySelectorAll('a[aria-current="page"]')).map((a) => `${surfaceOf(a)} ${a.getAttribute('href')}`).sort()

function chosenByTheComponents({ shell, path, tabs }: Page): string[] {
  const visible = visibleGroups(shell.groups, EVERYTHING)
  const claimed = isClaimed(visible, path)
  const rows = visible.flatMap((group) => group.items).filter((item) => isCurrent(item, path, claimed)).map((item) => item.to)
  const account = shell.chrome.filter((item) => isCurrent(item, path)).map((item) => item.to)

  return [
    ...rows.map((to) => `sidebar ${to}`),
    ...rows.map((to) => `menu ${to}`),
    ...account.map((to) => `account ${to}`),
    ...account.map((to) => `account ${to}`),
    ...(tabs ? [`tabs ${path}`] : []),
  ].sort()
}

describe('the pages this file reads', () => {
  it('are every row of both navigations and both account lists, at and below its path, every framed tender screen and the brief', () => {
    expect(ROWS.map(({ row }) => row)).toEqual([
      ...BACK_OFFICE_NAV.flatMap((group) => group.items), ...BACK_OFFICE_CHROME,
      ...SUPPLIER_NAV.flatMap((group) => group.items), ...SUPPLIER_CHROME,
    ])
    expect(TENDER_SCREENS).toHaveLength(7)
    expect(PAGES).toHaveLength(ROWS.length * 2 + TENDER_SCREENS.length + 1)
    expect(new Set(PAGES.map((page) => page.path)).size).toBe(PAGES.length)
  })
})

describe('the current page under the real router', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', () => new Promise<Response>(() => {}))
    useAuthStore.setState({
      accessToken: 'test',
      status: 'authenticated',
      claims: { userId: 'u-1', email: 'staff@example.test', organizationId: 'org-1', permissions: TAB_PERMISSIONS },
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle' })
  })

  it.each(PAGES)('$path marks only the links the navigation marks', async (page) => {
    const { container } = renderUnderRouter(page.path, navigationFor(page))
    await screen.findByRole('navigation', { name: i18n.t('nav.breadcrumb') })

    const drawn: Surface[] = ['sidebar', 'menu', 'account']
    if (page.path !== page.shell.home.to) drawn.push('trail')
    if (page.tabs) drawn.push('tabs')
    expect(surfacesWithLinks(container)).toEqual(drawn.sort())
    if (page.tabs) {
      expect(screen.getByRole('navigation', { name: i18n.t('rfq.tabs.label') }).querySelectorAll('a')).toHaveLength(7)
    }

    expect(markedLinks(container)).toEqual(chosenByTheComponents(page))
  })
})

describe('the mark check can fail', () => {
  function trailWithCrumb(activeOptions?: { exact: boolean }) {
    return function Trail() {
      return (
        <nav aria-label={i18n.t('nav.breadcrumb')}>
          <Link to="/back-office/dashboard" activeOptions={{ exact: true }}>Home</Link>
          <Link to="/back-office/rfqs" activeOptions={activeOptions}>Tenders</Link>
        </nav>
      )
    }
  }

  it('reports the section crumb as it was, a link the router marks by prefix', async () => {
    const { container } = renderUnderRouter('/back-office/rfqs/RFQ-1/award', trailWithCrumb())
    await screen.findByRole('navigation', { name: i18n.t('nav.breadcrumb') })

    expect(container.querySelectorAll('a')).toHaveLength(2)
    expect(markedLinks(container)).toEqual(['trail /back-office/rfqs'])
  })

  it('reports nothing once that crumb matches exactly', async () => {
    const { container } = renderUnderRouter('/back-office/rfqs/RFQ-1/award', trailWithCrumb({ exact: true }))
    await screen.findByRole('navigation', { name: i18n.t('nav.breadcrumb') })

    expect(container.querySelectorAll('a')).toHaveLength(2)
    expect(markedLinks(container)).toEqual([])
  })
})
