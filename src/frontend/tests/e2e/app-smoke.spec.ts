// The smoke test: docs/backlog gap item 3, a Playwright pass against the app as it actually runs.
//
// Four things, all reachable without a login: a protected route redirects to /login, an unknown
// route lands on the 404 boundary, the login form renders both of its fields, and the language
// switch changes the document direction. Together they prove the real route tree is wired - not a
// mocked router, the one the app ships.
//
// A full authenticated login is deliberately not here. It needs a seeded backend user, which is
// what the integration suite has and a frontend dev server does not; this file's whole value is
// that it runs against `npm run dev` and nothing else.

import { test, expect } from '@playwright/test'

test('unauthenticated visit to a protected route redirects to /login', async ({ page }) => {
  await page.goto('/dashboard')
  await expect(page).toHaveURL(/\/login/)
  await expect(page.getByRole('heading', { name: /sign in|تسجيل الدخول/i })).toBeVisible()
})

test('unknown route renders the 404 boundary', async ({ page }) => {
  await page.goto('/this-route-does-not-exist')
  await expect(page.getByText('404')).toBeVisible()
})

test('login page renders the email/password form', async ({ page }) => {
  await page.goto('/login')
  await expect(page.getByLabel(/email|البريد الإلكتروني/i)).toBeVisible()
  await expect(page.getByLabel(/^password|كلمة المرور/i)).toBeVisible()
})

test('language switch toggles direction', async ({ page }) => {
  await page.goto('/')
  const html = page.locator('html')
  const before = await html.getAttribute('dir')
  await page.getByRole('button', { name: /العربية|english/i }).click()
  await expect(html).not.toHaveAttribute('dir', before ?? '')
})
