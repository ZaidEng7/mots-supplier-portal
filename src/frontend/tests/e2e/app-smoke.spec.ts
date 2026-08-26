import { test, expect } from '@playwright/test'

/** docs/backlog gap item 3: Playwright smoke test against the running app. Exercises the
 * real route tree — unauthenticated redirect, 404 boundary, and login-page rendering.
 * A full authenticated login is covered by manual/CI integration tests since it needs a
 * seeded backend user; this smoke test only needs the frontend dev server. */
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
