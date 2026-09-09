import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync } from 'node:fs'
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
  /** Every file that renders a page shell or a whole public screen — the places the link is mounted over. */
  function screensThatNeedMain(): string[] {
    const shells = readdirSync(join(SRC, 'shells')).filter((f) => f.endsWith('.tsx') && !f.includes('.test.'))
      .map((f) => join('shells', f))
    const publicScreens = [
      'routes/LoginPage.tsx', 'routes/RegisterPage.tsx', 'routes/ForgotPasswordPage.tsx',
      'routes/VerifyEmailPage.tsx', 'components/AcceptInvitePageBase.tsx',
    ]
    return [...shells, ...publicScreens]
  }

  it('sweeps the screens it claims to', () => {
    // The denominator. A sweep that silently matched nothing would pass every assertion below.
    expect(screensThatNeedMain().length).toBeGreaterThanOrEqual(7)
  })

  it('every one of them renders the id the link points at', () => {
    const missing = screensThatNeedMain().filter((relative) => {
      const source = readFileSync(join(SRC, relative), 'utf8')
      return !source.includes('id="main"')
    })

    expect(missing, 'the skip link moves focus nowhere on these, and looks identical while doing it').toEqual([])
  })

  it('the sweep can fail', () => {
    // Revert-to-red: the same check against a file that has no main landmark and is not supposed to.
    const source = readFileSync(join(SRC, 'components/SkipLink.tsx'), 'utf8')

    expect(source.includes('id="main"')).toBe(false)
  })
})
