import { defineConfig, devices } from '@playwright/test'

/** docs/backlog gap items 1 & 3: axe smoke against the built Storybook, plus a real Playwright
 * smoke test against the running app. Two projects, each with its own server and baseURL. */

/**
 * Every project below except storybook-axe (its own server/baseURL/serial-execution shape) is
 * identical Desktop Chrome config against the same dev-server origin, differing only in name and
 * which spec file it runs - flagged by SonarCloud as a real duplicated block (5 occurrences of
 * the same `use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5173' }` line).
 */
function chromeProject(name: string, testMatch: string, baseURL = 'http://localhost:5173') {
  return { name, testMatch, use: { ...devices['Desktop Chrome'], baseURL } }
}

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list']],
  webServer: [
    {
      command: 'python3 -m http.server 6007 --directory storybook-static',
      port: 6007,
      reuseExistingServer: !process.env.CI,
      timeout: 30_000,
    },
    {
      command: 'npm run dev',
      port: 5173,
      reuseExistingServer: !process.env.CI,
      timeout: 30_000,
    },
  ],
  projects: [
    {
      name: 'storybook-axe',
      testMatch: 'storybook-axe.spec.ts',
      // Serial because each analyze() injects axe into the page; running them concurrently
      // against one origin invites interference. The per-project `retries: 1` that used to sit
      // here was masking the "Axe is already running" race rather than fixing it (MSP-79) - the
      // race is now removed in the spec by waiting for the story to actually render, so a failure
      // here is a real failure and must not be retried away.
      fullyParallel: false,
      workers: 1,
      retries: 0,
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:6007' },
    },
    chromeProject('app-smoke', 'app-smoke.spec.ts'),
    // NFR-A11Y-001/002/003/007: axe against every real application route, both locales.
    // Shares the app-smoke project's server/baseURL - no backend needed, every /api/v1 call is
    // intercepted (see mockBackend in the spec) the same way app-smoke needs none for its own
    // unauthenticated-only checks.
    chromeProject('app-a11y', 'app-a11y.spec.ts'),
    // NFR-A11Y: prefers-reduced-motion guard in index.css - proves the media query actually
    // changes rendered transition duration, not just that the rule text exists.
    chromeProject('reduced-motion', 'reduced-motion.spec.ts'),
    // ACCESSIBILITY.md's reflow clause at 320px, on the reports screen this batch added - four
    // tables, which is the control most likely to force a page to scroll sideways.
    chromeProject('reports-reflow', 'reports-reflow.spec.ts'),
    // Task #22/NFR-A11Y-002: real keyboard-only interaction (Tab/Enter/Escape/Arrow), not axe's
    // static DOM/ARIA checks - axe cannot verify tab order, focus traps, or that a control is
    // operable rather than merely focusable.
    chromeProject('app-keyboard', 'app-keyboard.spec.ts'),
    // Task #22/NFR-A11Y-007: real DOM/ARIA read proving aria-describedby resolves to an actual,
    // non-empty error element and aria-invalid is set - axe does not check that an
    // aria-describedby id target exists or holds the visible error text.
    chromeProject('app-error-association', 'app-error-association.spec.ts'),
  ],
})
