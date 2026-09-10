import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SkipLink } from './SkipLink'

/**
 * WCAG 2.4.1 Bypass Blocks. `docs/ux/ACCESSIBILITY.md:52` has required this since it was written, and
 * nothing implemented it until Phase 4 — which then shipped it with no test at all, so this file exists
 * as much for that as for the feature.
 *
 * <p><b>What axe cannot see.</b> Its `skip-link` rule is tagged `best-practice`, and this project's scans
 * run the `wcag2a`/`wcag2aa`/`wcag22aa` tags only. 137 axe checks across two locales were green for the
 * whole time the product had no skip link, and would be green again the day it lost one.</p>
 *
 * <p><b>Why half of this test reads the source tree.</b> A skip link is two halves that live in different
 * files: the control, and the thing it lands on. Rendering the control in isolation proves nothing — an
 * `href="#main"` pointing at no `id="main"` moves focus nowhere, and looks identical in a unit test. So
 * the second half sweeps every screen that mounts the link and asserts the target is really there.</p>
 */
const SRC = resolve(process.cwd(), 'src')

describe('SkipLink, the control', () => {
  it('points at the main landmark', () => {
    render(<SkipLink />)

    expect(screen.getByRole('link').getAttribute('href')).toBe('#main')
  })

  it('is hidden until it is focused, and then is genuinely visible', async () => {
    render(<SkipLink />)
    const link = screen.getByRole('link')

    // sr-only is how it hides. A skip link nobody can see when they land on it is the same as no skip
    // link, which is why the focus variant has to remove exactly that class rather than merely restyle.
    expect(link.className).toContain('sr-only')
    expect(link.className).toContain('focus:not-sr-only')

    await userEvent.tab()
    expect(link).toHaveFocus()
  })

  it('is the first thing a keyboard reaches', async () => {
    render(
      <div>
        <SkipLink />
        <a href="/somewhere-else">A link that is not the skip link</a>
      </div>,
    )

    await userEvent.tab()
    expect(screen.getByRole('link', { name: 'nav.skipToContent' })).toHaveFocus()
  })
})

describe('SkipLink, the target it depends on', () => {
  /**
   * Every file that renders a page shell or a whole public screen - the places the link is mounted over.
   *
   * <p>The shells are read from the router rather than by listing the folder. The folder used to hold
   * one file per shell and now holds the frame they share and the pieces it is built from, so a sweep
   * over the folder would demand a `main` landmark of a sidebar. What the router imports as a layout is
   * what actually wraps a page, which is the thing this rule is about.</p>
   */
  function shellEntryPoints(): string[] {
    const router = readFileSync(join(SRC, 'router.tsx'), 'utf8')
    // Quoted rather than `from`-anchored: the shells are code-split, so the router names them inside
    // `lazy(() => import('./shells/X'))` and an import-statement pattern would have matched none of them.
    const named = [...router.matchAll(/'\.\/shells\/([A-Za-z]+)'/g)].map((m) => join('shells', `${m[1]}.tsx`))
    return [...new Set(named)]
  }

  /**
   * Whether a file provides the landmark itself or renders something beside it that does.
   *
   * <p>One level of delegation, not arbitrary depth: the shells hand off to exactly one frame, and a
   * resolver that chased imports forever would be a module graph rather than a test.</p>
   */
  function providesMain(relative: string): boolean {
    const source = readFileSync(join(SRC, relative), 'utf8')
    if (source.includes('id="main"')) return true
    return [...source.matchAll(/from '\.\/([A-Za-z]+)'/g)]
      .map((m) => join('shells', `${m[1]}.tsx`))
      .some((imported) => {
        try {
          return readFileSync(join(SRC, imported), 'utf8').includes('id="main"')
        } catch {
          return false
        }
      })
  }

  function screensThatNeedMain(): string[] {
    const publicScreens = [
      'routes/LoginPage.tsx', 'routes/RegisterPage.tsx', 'routes/ForgotPasswordPage.tsx',
      'routes/VerifyEmailPage.tsx', 'components/AcceptInvitePageBase.tsx',
    ]
    return [...shellEntryPoints(), ...publicScreens]
  }

  it('sweeps the screens it claims to', () => {
    // The denominator. A sweep that silently matched nothing would pass every assertion below.
    expect(screensThatNeedMain().length).toBeGreaterThanOrEqual(7)
    // And the router really is where the shells come from, so a rename cannot empty this quietly.
    expect(shellEntryPoints()).toContain(join('shells', 'BackOfficeShell.tsx'))
    expect(shellEntryPoints()).toContain(join('shells', 'SupplierShell.tsx'))
    // The delegation resolver has to actually resolve something, or every shell would pass by accident.
    expect(readFileSync(join(SRC, 'shells', 'BackOfficeShell.tsx'), 'utf8')).not.toContain('id="main"')
    expect(providesMain(join('shells', 'BackOfficeShell.tsx'))).toBe(true)
    expect(providesMain(join('shells', 'Sidebar.tsx'))).toBe(false)
  })

  it('every one of them renders the id the link points at', () => {
    const missing = screensThatNeedMain().filter((relative) => !providesMain(relative))

    expect(missing, 'the skip link moves focus nowhere on these, and looks identical while doing it').toEqual([])
  })

  it('the sweep can fail', () => {
    // Revert-to-red: the same check against a file that has no main landmark and is not supposed to.
    const source = readFileSync(join(SRC, 'components/SkipLink.tsx'), 'utf8')

    expect(source.includes('id="main"')).toBe(false)
  })
})
