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
      // Serial: concurrent AxeBuilder.analyze() calls against the same origin can race
      // ("Axe is already running") even across separate page/context instances. A retry
      // absorbs the rare remaining race on a cold browser/page.
      fullyParallel: false,
      workers: 1,
      retries: 1,
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:6007' },
    },
    {
      name: 'app-smoke',
      testMatch: 'app-smoke.spec.ts',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5173' },
    },
  ],
})
