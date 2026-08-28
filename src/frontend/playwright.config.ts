import { defineConfig, devices } from '@playwright/test'

/** docs/backlog gap items 1 & 3: axe smoke against the built Storybook, plus a real Playwright
 * smoke test against the running app. Two projects, each with its own server and baseURL. */
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
    {
      name: 'app-smoke',
      testMatch: 'app-smoke.spec.ts',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5173' },
    },
  ],
})
