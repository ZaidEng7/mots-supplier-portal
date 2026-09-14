// NFR-A11Y: the prefers-reduced-motion guard in src/index.css, proven by what it renders.
//
// Two tests, and the pair is the point. The first asserts the submit button's transition is real
// when no preference is set (Tailwind's transition-colors default is 150ms); the second asserts it
// collapses to near-zero under `reduce`. Asserting only the second would pass just as happily if
// the duration were always zero, which measures nothing - the no-preference case is the denominator
// that makes the reduce case mean something.
//
// Why it is measured rather than read out of the stylesheet: a previously-shipping build bug was
// caught exactly this way. A multi-line CSS comment sitting directly above the rule corrupted it
// during the production build - Vite's CSS transform mangled the selector list and dropped the whole
// rule - so the guard was completely inert while looking correct in source. Isolated by repeated
// `npm run build` plus grep of the dist output. Comments near that rule in index.css must stay
// single-line block comments, one per line.
//
// /login is unauthenticated and uses the shared Button component, so no network mocking is needed -
// the same reasoning as app-smoke.spec.ts.

import { test, expect } from '@playwright/test'

test('submit button has a real transition when no motion preference is set', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await page.goto('/login')
  const button = page.locator('button', { hasText: /Sign in|دخول/i }).first()
  const duration = await button.evaluate((el) => getComputedStyle(el).transitionDuration)
  expect(parseFloat(duration)).toBeGreaterThan(0.05)
})

test('submit button transition collapses to near-zero when prefers-reduced-motion is reduce', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.goto('/login')
  const button = page.locator('button', { hasText: /Sign in|دخول/i }).first()
  const duration = await button.evaluate((el) => getComputedStyle(el).transitionDuration)
  expect(parseFloat(duration)).toBeLessThan(0.001)
})
