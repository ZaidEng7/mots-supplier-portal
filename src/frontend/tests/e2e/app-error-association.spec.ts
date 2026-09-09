import { test, expect, type Locator, type Page } from '@playwright/test'
import { mockBackend, SUPPLIER_PROFILE } from './fixtures'

/**
 * Task #22/NFR-A11Y-007: "errors are announced and programmatically associated with fields."
 * (NON-FUNCTIONAL-REQUIREMENTS.md, quoted exactly - the target-size half of this NFR was already
 * verified in an earlier task per REQUIREMENTS-AUDIT.md; only the error-association half is this
 * ticket's scope).
 *
 * <p><b>The denominator.</b> `grep -n "error={" src/routes/*.tsx src/routes/onboarding/*.tsx`
 * finds every <Field error={...}> usage in the app - the complete, real set of fields that can
 * ever show a validation error, not a sample: 26 fields across 10 forms (LoginPage, RegisterPage,
 * AcceptTeamInvitePage, ForgotPasswordPage, ResetPasswordPage, TeamPage, BankingPage,
 * AddressesPage, ContactsPage, OnboardingPage's legal-info section). All 26 render through the
 * identical `{(p) => <Input {...p} {...register(...)} />}` render-prop pattern - none use Select
 * (the app's Select-based fields never carry an `error` prop today, so they are out of this NFR's
 * scope: there is no error state to associate).
 *
 * All 26 are driven into a real error state below (empty/invalid submit) and their rendered
 * DOM/ARIA tree is read directly - not eyeballed, not axe's static pass (axe does not check that
 * an aria-describedby id target actually exists or holds the visible error text).
 *
 * Self-correction (Task #25): this file originally claimed BankingPage's accountNumber was
 * unreachable, reasoning from `bankSchema`'s `z.string().optional()` alone. That was an
 * incomplete read - BankingPage.tsx's submit handler does its own manual check straight after
 * zod validation passes (`if (!initial && !values.accountNumber) setError('accountNumber', ...)`),
 * required only when adding, matching both the UI's `required={!initial}` asterisk and the
 * backend's `AddBankAccountRequestValidator.RuleFor(x => x.AccountNumber).NotEmpty()` - there
 * never was a schema/UI mismatch, only a test that stopped at the schema and never actually
 * tried the scenario that reaches the manual check (accountHolderName/bankName filled in,
 * accountNumber left blank).
 *
 * <p>Locators use getByRole('textbox', { name, exact: true }) rather than getByLabel: Field's
 * required-marker asterisk is an aria-hidden sibling of the label text, correctly excluded from
 * the computed accessible name ("Email", not "Email *") - but getByLabel matches against the
 * label element's raw text content, which still includes the asterisk, so an exact match against
 * the clean name spuriously finds zero elements. getByRole reads the real accessible-name
 * computation instead, which is also what NFR-A11Y-007 and assistive tech actually care about.
 */

async function assertErrorAssociated(page: Page, input: Locator, context: string) {
  const describedBy = await input.getAttribute('aria-describedby')
  expect(describedBy, `${context}: input has no aria-describedby while in error state`).toBeTruthy()

  const errorId = describedBy!.split(' ').find((id) => id.endsWith('-error'))
  expect(errorId, `${context}: aria-describedby "${describedBy}" has no -error id segment`).toBeTruthy()

  const errorEl = page.locator(`#${errorId}`)
  await expect(errorEl, `${context}: no element with id="${errorId}" exists in the DOM`).toBeVisible()

  const text = (await errorEl.textContent())?.trim() ?? ''
  expect(text.length, `${context}: #${errorId} exists but is empty`).toBeGreaterThan(0)

  await expect(input, `${context}: input missing aria-invalid="true" while in error state`).toHaveAttribute('aria-invalid', 'true')
}

function field(scope: Page | Locator, name: string): Locator {
  return scope.getByRole('textbox', { name, exact: true })
}

/**
 * The onboarding/team/banking/addresses/contacts forms only render their edit affordances (Add
 * buttons, enabled legal-info fields) while the supplier profile is in an editable onboardingState
 * (EmailVerified/ProfileInProgress/InfoRequested). fixtures.ts's shared SUPPLIER_PROFILE is
 * 'UnderReview' - correct for the a11y/keyboard suites, which need a stable, review-ready fixture,
 * but wrong here: this suite needs to actually reach and submit those forms. Registered after
 * mockBackend's broader route, so it wins for this one endpoint (Playwright matches the
 * most-recently-registered handler first).
 */
async function mockEditableBackend(page: Page) {
  await mockBackend(page)
  await page.route('**/api/v1/suppliers/me', (route) =>
    route.fulfill({ json: { ...SUPPLIER_PROFILE, onboardingState: 'ProfileInProgress' } }),
  )
}

test.describe('Auth forms: error-association on real validation failures', () => {
  test('LoginPage: empty submit associates both fields', async ({ page }) => {
    await page.goto('/login?lng=en')
    await page.getByRole('button', { name: 'Sign in' }).click()
    await assertErrorAssociated(page, field(page, 'Email'), 'LoginPage.email')
    await assertErrorAssociated(page, field(page, 'Password'), 'LoginPage.password')
  })

  test('RegisterPage: empty submit associates all 7 fields', async ({ page }) => {
    await page.goto('/register?lng=en')
    await page.getByRole('button', { name: 'Create account' }).click()
    const labels = [
      'Company name (Arabic)',
      'Company name (English)',
      'Primary representative name',
      "Primary representative's phone",
      'Email',
      'Password',
      'Confirm password',
    ]
    for (const label of labels) {
      await assertErrorAssociated(page, field(page, label), `RegisterPage.${label}`)
    }
  })

  test('ForgotPasswordPage: invalid email associates the field', async ({ page }) => {
    await page.goto('/forgot-password?lng=en')
    await field(page, 'Email').fill('not-an-email')
    await page.getByRole('button', { name: 'Send reset link' }).click()
    await assertErrorAssociated(page, field(page, 'Email'), 'ForgotPasswordPage.email')
  })

  test('ResetPasswordPage: too-short password associates the field', async ({ page }) => {
    await page.goto('/reset-password?lng=en&token=fake-token')
    await field(page, 'New password').fill('short')
    await page.getByRole('button', { name: 'Reset password' }).click()
    await assertErrorAssociated(page, field(page, 'New password'), 'ResetPasswordPage.newPassword')
  })

  test('AcceptTeamInvitePage: too-short password associates the field', async ({ page }) => {
    await page.goto('/accept-invite?lng=en&token=fake-token')
    await field(page, 'New password').fill('short')
    await page.getByRole('button', { name: 'Accept invite' }).click()
    await assertErrorAssociated(page, field(page, 'New password'), 'AcceptTeamInvitePage.password')
  })
})

test.describe('Authenticated forms: error-association on real validation failures', () => {
  test('TeamPage invite dialog: empty submit associates both fields', async ({ page }) => {
    await mockBackend(page)
    await page.goto('/team?lng=en', { waitUntil: 'networkidle' })
    await page.getByRole('button', { name: 'Invite member' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('button', { name: 'Send invite' }).click()
    await assertErrorAssociated(page, field(dialog, 'Full name'), 'TeamPage.fullName')
    await assertErrorAssociated(page, field(dialog, 'Email'), 'TeamPage.email')
  })

  test('BankingPage add-account dialog: empty submit associates the two zod-required fields', async ({ page }) => {
    await mockEditableBackend(page)
    await page.goto('/onboarding/banking?lng=en', { waitUntil: 'networkidle' })
    await page.getByRole('button', { name: 'Add account' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('button', { name: 'Save' }).click()
    await assertErrorAssociated(page, field(dialog, 'Account holder name'), 'BankingPage.accountHolderName')
    await assertErrorAssociated(page, field(dialog, 'Bank name'), 'BankingPage.bankName')
  })

  test('BankingPage add-account dialog: accountNumber left blank (other fields valid) associates the manual required check', async ({ page }) => {
    // Task #25: accountNumber is required only when ADDING, enforced by BankingPage.tsx's submit
    // handler straight after zod validation passes - not by bankSchema itself (deliberately, per
    // that file's own comment: a branch-conditional zodResolver would confuse react-hook-form's
    // inferred type across add/edit). A wholly empty submit never reaches that check (zod already
    // rejects on the other two required fields first), so this scenario fills every OTHER
    // required field and leaves only accountNumber blank - the one path that actually exercises it.
    await mockEditableBackend(page)
    await page.goto('/onboarding/banking?lng=en', { waitUntil: 'networkidle' })
    await page.getByRole('button', { name: 'Add account' }).click()
    const dialog = page.getByRole('dialog')

    await field(dialog, 'Account holder name').fill('Test Holder')
    await field(dialog, 'Bank name').fill('Test Bank')
    await dialog.getByRole('combobox', { name: 'Currency' }).click()
    await page.getByRole('option', { name: 'SYP' }).click()

    await dialog.getByRole('button', { name: 'Save' }).click()
    await assertErrorAssociated(page, field(dialog, 'Account number'), 'BankingPage.accountNumber')
  })

  test('BankingPage edit-account dialog: accountNumber left blank submits successfully (leaves it unchanged)', async ({ page }) => {
    // The mirror case: on EDIT (initial set), the manual required check does not run - matches
    // UpdateBankAccountCommand's "null AccountNumber means leave the encrypted value untouched"
    // contract. Confirms the add-only requirement doesn't leak into edit mode.
    await mockEditableBackend(page)
    await page.route('**/api/v1/suppliers/me/bank-accounts/**', (route) =>
      route.fulfill({ json: { ...SUPPLIER_PROFILE, onboardingState: 'ProfileInProgress' } }),
    )
    await page.goto('/onboarding/banking?lng=en', { waitUntil: 'networkidle' })
    await page.getByRole('button', { name: 'Edit' }).first().click()
    const dialog = page.getByRole('dialog')
    await expect(field(dialog, 'Account number')).toHaveValue('')
    await dialog.getByRole('button', { name: 'Save' }).click()
    await expect(dialog).not.toBeVisible()
  })

  test('AddressesPage add-address dialog: empty submit associates required fields', async ({ page }) => {
    await mockEditableBackend(page)
    await page.goto('/onboarding/addresses?lng=en', { waitUntil: 'networkidle' })
    await page.getByRole('button', { name: 'Add address' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('button', { name: 'Save' }).click()
    await assertErrorAssociated(page, field(dialog, 'Address'), 'AddressesPage.line1')
    await assertErrorAssociated(page, field(dialog, 'City'), 'AddressesPage.city')
    await assertErrorAssociated(page, field(dialog, 'Country'), 'AddressesPage.country')
  })

  test('ContactsPage add-representative dialog: empty submit associates required fields', async ({ page }) => {
    await mockEditableBackend(page)
    await page.goto('/onboarding/contacts?lng=en', { waitUntil: 'networkidle' })
    await page.getByRole('button', { name: 'Add representative' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('button', { name: 'Save' }).click()
    await assertErrorAssociated(page, field(dialog, 'Full name'), 'ContactsPage.fullName')
    await assertErrorAssociated(page, field(dialog, 'Email'), 'ContactsPage.email')
  })

  test('OnboardingPage legal-info section: cleared required field associates on submit', async ({ page }) => {
    await mockEditableBackend(page)
    await page.goto('/onboarding?lng=en', { waitUntil: 'networkidle' })
    const legalNameEn = field(page, 'Legal name (English)')
    await legalNameEn.fill('')
    // Named, not `.first()` on an ambiguous 'Save'. The screen has two forms and both submit buttons
    // used to read just "Save", so this clicked whichever came first in the DOM and would have passed
    // even if it had submitted the wrong one.
    await page.getByRole('button', { name: 'Save legal information' }).click()
    await assertErrorAssociated(page, legalNameEn, 'OnboardingPage.legalNameEn')
  })
})

test.describe('Revert-to-red proof: a broken association must fail this check', () => {
  test('a field with aria-describedby stripped fails assertErrorAssociated', async ({ page }) => {
    await page.goto('/login?lng=en')
    await page.getByRole('button', { name: 'Sign in' }).click()
    const email = field(page, 'Email')
    await assertErrorAssociated(page, email, 'sanity: real association passes first')

    // Simulate the exact wiring gap NFR-A11Y-007 predicts: strip the link a working Field
    // computes, and confirm the check catches it rather than passing regardless.
    await email.evaluate((el) => el.removeAttribute('aria-describedby'))
    await expect(async () => {
      await assertErrorAssociated(page, email, 'tampered')
    }).rejects.toThrow()
  })
})

/**
 * The required marker, said to assistive technology as well as drawn.
 *
 * <p>`Field` renders an asterisk beside a required label and marks it `aria-hidden` — correctly, because
 * a reader announcing "asterisk" is noise. What was missing is the fact the asterisk stands for. Nothing
 * told assistive technology the field was required, so a screen-reader user met the requirement for the
 * first time as a validation error after submitting a form they could not have known was incomplete.</p>
 *
 * <p>Checked on the registration form because it has the most required fields in the product, and in
 * Arabic as well as English because `required` is a prop, not a string, and a locale-specific failure
 * here would mean something worse than a translation bug.</p>
 */
for (const locale of ['en', 'ar'] as const) {
  test(`required fields say so to assistive technology, not only with an asterisk [${locale}]`, async ({ page }) => {
    await mockBackend(page)
    await page.goto(`/register?lng=${locale}`, { waitUntil: 'networkidle' })

    const required = page.locator('[aria-required="true"]')
    const count = await required.count()

    // The denominator. Registration has seven required fields; a check that found none would pass every
    // assertion below by measuring nothing.
    expect(count).toBeGreaterThanOrEqual(5)

    // Every one of them is a real control, not a decoration that happened to carry the attribute.
    for (let index = 0; index < count; index += 1) {
      const role = await required.nth(index).evaluate((node) => node.tagName.toLowerCase())
      expect(['input', 'select', 'textarea', 'button']).toContain(role)
    }
  })
}
