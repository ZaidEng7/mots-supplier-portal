import { test, expect } from '@playwright/test'

/**
 * NFR-A11Y: prefers-reduced-motion guard in src/index.css. Proves the media query actually
 * changes rendered behavior when the OS preference is set, not merely that the rule text exists
 * in the stylesheet - a real, previously-shipping build bug was caught exactly this way: a
 * multi-line CSS comment placed directly above this rule silently corrupted it during the
 * production build (Vite's CSS transform mangled the selector list and dropped the whole rule),
 * so the guard would have been completely inert while looking correct in source. Comments near
 * this rule must stay single-line block comments (`/* ... *\/` per line) - confirmed via repeated
 * `npm run build` + grep of dist output while isolating the cause.
 *
 * /login is unauthenticated and uses the shared Button component (transition-colors), so no
 * network mocking is needed - same reasoning as app-smoke.spec.ts.
 */
test('submit button has a real transition when no motion preference is set', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await page.goto('/login')
  const button = page.locator('button', { hasText: /Sign in|دخول/i }).first()
  const duration = await button.evaluate((el) => getComputedStyle(el).transitionDuration)
  // Tailwind's transition-colors default is 150ms; asserting it is not collapsed proves the
  // *reduce* case below is actually doing something, not just always-zero regardless of pref.
  expect(parseFloat(duration)).toBeGreaterThan(0.05)
})

test('submit button transition collapses to near-zero when prefers-reduced-motion is reduce', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.goto('/login')
  const button = page.locator('button', { hasText: /Sign in|دخول/i }).first()
  const duration = await button.evaluate((el) => getComputedStyle(el).transitionDuration)
  expect(parseFloat(duration)).toBeLessThan(0.001)
})
