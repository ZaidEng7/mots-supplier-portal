import type { ReactNode } from 'react'
import { useRouterState } from '@tanstack/react-router'
import { ErpStatusBanner } from '../components/ErpStatusBanner'
import { Sidebar } from './Sidebar'
import { TopBar } from './TopBar'
import type { NavContext, NavGroup, NavItem } from './navigation'
import { useAuthStore } from '../lib/authStore'
import { PublicFooter } from '../components/PublicFooter'
import { useDeclareShellMounted } from '../components/shellPresence'
import { logout as apiLogout } from '../api/auth'
import { clearETags } from '../api/etags'

export interface AppShellProps {
  groups: readonly NavGroup[]
  chrome: readonly NavItem[]
  context: NavContext
  /** The product name, and which side of it this is. */
  title: string
  subtitle: string
  home: { to: string; label: string }
  searchTo?: string
  /** A class that changes the reading density for everything inside, where a shell asks for one. */
  densityClass?: string
  /** Anything the shell adds below the content, such as the supplier's mobile tab bar. */
  footer?: ReactNode
  children: ReactNode
}

/**
 * The frame both shells share: a grouped sidebar, a top bar carrying context and controls, and the page.
 *
 * <p><b>Why one frame for two shells.</b> They had drifted into two different answers to the same
 * questions - one wrapped its links in two named groups and marked the current page, the other listed
 * thirty-one in a single row and marked nothing - and the difference was accident rather than design.
 * What legitimately differs between them is data: which destinations, which words, which density. That
 * is what the props are. What is the same is the frame, and it is now the same code.</p>
 *
 * <p>The two still look different where it matters, because the rail carries the same dark field in
 * both and the supplier's own subtitle names their side of the product. Staff and suppliers being
 * unable to mistake one surface for the other was the original reason these were separate components,
 * and it is a matter of what the sidebar says rather than how the page is built.</p>
 */
export function AppShell({
  groups, chrome, context, title, subtitle, home, searchTo, densityClass, footer, children,
}: Readonly<AppShellProps>) {
  // The root renders the same footer for anonymous pages and stands down while this is on screen -
  // otherwise it draws below the sidebar, which is a full viewport tall, and every short page ends in a
  // band of empty white with two links marooned in it.
  useDeclareShellMounted()

  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const email = useAuthStore((state) => state.claims?.email)
  const clearSession = useAuthStore((state) => state.clearSession)

  const handleLogout = async () => {
    await apiLogout()
    clearSession()
    // The ETag store is per tab and in memory, and `exportReachability.test.ts` has always described
    // clearETags as "called on sign-out" - while nothing called it. The full page load below happened
    // to empty the Map, so the documentation was true by accident and would have stopped being true
    // the day sign-out became a client-side navigation. One version belonging to the previous account,
    // sent as a precondition by the next one, is the shape of bug that takes a day to find.
    clearETags()
    window.location.href = '/login'
  }

  return (
    <div className={densityClass} style={{ backgroundColor: 'var(--color-bg-app)' }}>
      {/* SCR-045: above everything, so it is chrome rather than page content. */}
      <ErpStatusBanner />
      <div className="flex min-h-screen">
        <Sidebar
          groups={groups}
          context={context}
          pathname={pathname}
          title={title}
          subtitle={subtitle}
          account={email ? { email } : undefined}
        />
        <div className="flex min-w-0 flex-1 flex-col">
          <TopBar
            groups={groups}
            chrome={chrome}
            context={context}
            pathname={pathname}
            home={home}
            searchTo={searchTo}
            onLogout={handleLogout}
          />
          {/*
            A measure, which no screen had. On a wide monitor the content simply filled the window: a
            six-column table stretched to 1800 pixels puts the row's last cell an inch from the first,
            and a form put a label most of a screen away from its own field. 1440 is the approved
            template's own cap for a page; forms cap themselves tighter still, in `FormMeasure`.
          */}
          <main id="main" className="flex w-full max-w-[1440px] flex-1 flex-col px-4 py-8 sm:px-6">{children}</main>
          {/* In the content column, so it ends the page rather than the window - and inside the same
              1440 measure the page uses, so it lines up with the content above it rather than running
              out to the edge of a wide monitor. */}
          <div className="w-full max-w-[1440px]"><PublicFooter inShell /></div>
          {footer}
        </div>
      </div>
    </div>
  )
}
