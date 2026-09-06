/**
 * The walkthrough driver.
 *
 * This script IS the seeding. Every supplier, RFQ, proposal and award in the database after it runs
 * came into existence by being created through the UI, the same way a person would create it - which
 * is why the screenshots are worth looking at. Nothing here writes to the database directly.
 *
 * Run:  node walkthrough/walk.mjs            (whole walk)
 *       node walkthrough/walk.mjs --from 12  (resume at a step, against data already created)
 *
 * Output: walkthrough/screenshots/NN-persona-description.png
 *         walkthrough/GUIDE.md  - the companion file, one entry per screenshot
 */
// Resolved from the frontend's own node_modules: this script lives outside that package, and a bare
// specifier would look in walkthrough/ and the repo root and find nothing.
import { createRequire } from 'node:module'
const require_ = createRequire(new URL('../src/frontend/package.json', import.meta.url))
const { chromium } = require_('@playwright/test')
import { mkdirSync, writeFileSync, rmSync } from 'node:fs'
import { createHmac } from 'node:crypto'
import { execSync } from 'node:child_process'

const APP = 'http://localhost:5173'
const MAILHOG = 'http://localhost:8025'
const SHOTS = new URL('./screenshots/', import.meta.url).pathname
const GUIDE = new URL('./GUIDE.md', import.meta.url).pathname

const ADMIN = { email: 'admin@mots.local', password: 'motsadmin2026' }
/**
 * Read from the database, not pasted in.
 *
 * The bootstrap admin's authenticator secret is generated once, on the run that creates the account,
 * so it changes on every reset. Hard-coding it meant editing this file after each one and, twice,
 * forgetting to - which surfaces as a two-factor screen that will not accept any code.
 */
const TOTP_SECRET = execSync(
  `docker compose exec -T postgres psql -U postgres -d mots_supplier_portal -t -A -c ` +
  `"select t.\\"Value\\" from identity.app_user u join identity.user_token t on t.\\"UserId\\"=u.\\"Id\\" where t.\\"Name\\"='AuthenticatorKey';"`,
  { cwd: new URL('..', import.meta.url).pathname, encoding: 'utf8' },
).trim()

/** Accounts this walk creates. Passwords are uniform so the guide can publish them. */
const PW = 'Walkthrough2026!'
const STAFF = {
  officer: { email: 'officer@mots.local', name: 'Rana Khoury', role: 'procurement_officer' },
  manager: { email: 'manager@mots.local', name: 'Samir Haddad', role: 'procurement_manager' },
  manager2: { email: 'manager2@mots.local', name: 'Nour Aziz', role: 'procurement_manager' },
  evaluator: { email: 'evaluator@mots.local', name: 'Dr Layla Suleiman', role: 'evaluator' },
  reviewer: { email: 'reviewer@mots.local', name: 'Tarek Nassar', role: 'onboarding_reviewer' },
  ministry: { email: 'ministry@mots.local', name: 'Maha Darwish', role: 'ministry_viewer' },
}
const SUPPLIER = {
  email: 'supplier@gulfcatering.example',
  repName: 'Yara Mansour',
  repPhone: '944112233',
  password: PW,
  nameEn: 'Gulf Catering Company',
  nameAr: 'شركة الخليج للتموين',
}

let step = 0
let lastApiFailure = null
const entries = []

/** RFC 6238 TOTP. SHA-1 because the spec says SHA-1 and the server uses it. */
function totp(secret) {
  const key = Buffer.from(base32(secret))
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)))
  const h = createHmac('sha1', key).update(counter).digest()
  const o = h[19] & 0xf
  const code = ((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0')
  return code
}
function base32(s) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of s.replace(/=+$/, '')) bits += A.indexOf(c).toString(2).padStart(5, '0')
  const out = []
  for (let i = 0; i + 8 <= bits.length; i += 8) out.push(parseInt(bits.slice(i, i + 8), 2))
  return out
}

/**
 * A screenshot plus its companion entry. `what` is what just happened; `next` is what the user does
 * from here - the two questions a screenshot on its own cannot answer.
 */
async function shot(page, persona, screen, what, next) {
  step += 1
  const slug = `${String(step).padStart(2, '0')}-${persona}-${screen.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')}`
  const file = `${slug}.png`
  await page.screenshot({ path: SHOTS + file, fullPage: true })
  entries.push({ step, file, persona, screen, what, next, url: page.url().replace(APP, '') })
  console.log(`  [${String(step).padStart(2, '0')}] ${persona.padEnd(10)} ${screen}`)
}

async function mail(predicate, { timeout = 20000 } = {}) {
  const until = Date.now() + timeout
  while (Date.now() < until) {
    const res = await fetch(`${MAILHOG}/api/v2/messages?limit=60`)
    const body = await res.json()
    for (const m of body.items ?? []) {
      const to = (m.To ?? []).map((t) => `${t.Mailbox}@${t.Domain}`).join(',')
      const subject = m.Content?.Headers?.Subject?.[0] ?? ''
      // Decoded, not read raw: these mails go out base64 with a utf-8 charset (the bodies are Arabic
      // first), so a regex over the wire form finds no links at all - which is exactly what the first
      // run reported, and it looked like the mail was missing rather than merely encoded.
      const enc = (m.Content?.Headers?.['Content-Transfer-Encoding'] ?? [])[0] ?? ''
      const body = m.Content?.Body ?? ''
      const raw = /base64/i.test(enc)
        ? Buffer.from(body, 'base64').toString('utf8')
        : body.replace(/=\r?\n/g, '').replace(/=3D/g, '=')
      if (predicate({ to, subject, raw })) return { to, subject, raw }
    }
    await new Promise((r) => setTimeout(r, 800))
  }
  throw new Error('no matching mail within timeout')
}

function firstLink(raw, pathHint) {
  const m = raw.match(new RegExp(`https?://[^\\s"'<>]*${pathHint}[^\\s"'<>]*`))
  if (!m) throw new Error(`no ${pathHint} link in mail`)
  return m[0].replace(/&amp;/g, '&')
}

/**
 * Waits for the app to actually settle, not just for the network to go quiet.
 *
 * networkidle fires while a mutation's own navigation is still in flight - the first run of this
 * script screenshotted the two-factor screen with its Verify button mid-spinner and then looked for a
 * navigation link that was one redirect away.
 */
async function settle(page) {
  // networkidle is a best effort, not a requirement. Some screens hold a connection open (the
  // verify-email page was the first to prove it), and a walkthrough driver that dies because a page
  // is still talking to the server is reporting on itself rather than on the product.
  await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => {})
  await page.waitForTimeout(500)
}

/**
 * Sign-ins are paced.
 *
 * NFR-SEC-009 limits authentication attempts per minute, and this walk signs in far more often than a
 * person would - the bootstrap admin, six staff accepting invitations, then every persona in turn. The
 * first clean run tripped the limiter and the screen reported "Invalid email or password", which sent
 * me looking for a wrong password for a while. The product's side of that is fixed (LoginPage now
 * distinguishes 429); this is the driver's side, so the walk documents the product rather than the
 * limiter.
 */
let lastSignIn = 0
async function paceSignIn() {
  const gap = Date.now() - lastSignIn
  if (gap < 6500) await new Promise((r) => setTimeout(r, 6500 - gap))
  lastSignIn = Date.now()
}

async function signIn(page, email, password, opts = {}) {
  // Retries once, after waiting the limiter's window out. Pacing alone is not enough: the window is
  // ten attempts a minute and this walk makes roughly that many, so a slow drift is all it takes.
  // Now that LoginPage tells 429 apart from a bad password, the driver can see which it hit.
  try {
    await attemptSignIn(page, email, password, opts)
  } catch (e) {
    // Retried on ANY sign-in failure, not only on a recognised rate-limit message. Keying the retry
    // on that message worked until the limiter refused the request before the page could render one,
    // and the walk then failed on a correct password. The message is still reported, because which
    // one it was is worth knowing.
    const shown = await page.locator('body').innerText().catch(() => '')
    const why = /too many attempts|محاولات كثيرة/i.test(shown) ? 'rate limited' : 'sign-in failed'
    console.log(`      ${why} - waiting out the limiter window and retrying`)
    await new Promise((r) => setTimeout(r, 62000))
    await attemptSignIn(page, email, password, opts)
  }
}

async function attemptSignIn(page, email, password, { totpSecret } = {}) {
  await paceSignIn()
  await page.goto(`${APP}/login`)
  await page.getByLabel(/email/i).fill(email)
  await page.getByLabel(/password/i).first().fill(password)
  await page.getByRole('button', { name: /^sign in$|^تسجيل الدخول$/i }).click()
  if (totpSecret) {
    const code = page.getByLabel(/authenticator|code|رمز/i)
    await code.waitFor({ state: 'visible' })
    await code.fill(totp(totpSecret))
    await page.getByRole('button', { name: /^verify$|^تأكيد$/i }).click()
  }
  // The assertion that the sign-in worked: the login route is gone. Without this the script races
  // ahead and reports a missing nav link when the real problem is that it is still on /login.
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 25000 })
  await settle(page)
}

/**
 * Answers SCR-010's language question, and does nothing if it is not on screen.
 *
 * The walk is captured in English because the guide is written in English; the product itself is
 * Arabic-first and every screen mirrors right-to-left when Arabic is chosen. Step 03's screenshot is
 * the Arabic interface, deliberately kept, because it is what a new account actually meets.
 */
async function chooseEnglish(page) {
  const dialog = page.getByRole('dialog').filter({ hasText: /choose your language|اختر لغة/i })
  if (await dialog.count()) {
    await dialog.getByRole('button', { name: /^english$/i }).click()
    await settle(page)
  }
}

async function signOut(page) {
  const out = page.getByRole('button', { name: /sign out|log out|تسجيل الخروج/i })
  if (await out.count()) {
    await out.first().click()
    await settle(page)
  } else {
    await page.evaluate(() => localStorage.clear())
    await page.goto(`${APP}/login`)
  }
}

function writeGuide() {
  const lines = [
    '# Supplier Portal — a walk through the whole system',
    '',
    'Every screenshot in `screenshots/` was taken while driving the real application against a database',
    'that started empty. Nothing here was seeded by a script: each supplier, tender, proposal and award',
    'below came into existence through the interface, in the order a real procurement would produce them.',
    '',
    '## Accounts this walk created',
    '',
    '| Persona | Email | Password |',
    '|---|---|---|',
    `| system_admin (bootstrap) | \`${ADMIN.email}\` | \`${ADMIN.password}\` + TOTP |`,
    ...Object.values(STAFF).map((s) => `| ${s.role} | \`${s.email}\` | \`${PW}\` |`),
    `| supplier_admin | \`${SUPPLIER.email}\` | \`${PW}\` |`,
    '',
    '## Two things that are decisions, not defects',
    '',
    '- **The Ministry dashboard shows no commercial figures.** That is a deliberate hold until MOT Legal',
    '  answers on what the Ministry may see. Aggregates only, by BRULE-086.',
    '- **`clamd` must be running or every document upload is refused.** Fail-closed by design: the scanner',
    '  folds any failure into "infected" rather than letting an unscanned file through.',
    '',
    '## The walk',
    '',
  ]
  for (const e of entries) {
    lines.push(
      `### ${String(e.step).padStart(2, '0')}. ${e.screen}`,
      '',
      `![${e.screen}](screenshots/${e.file})`,
      '',
      `- **Persona:** ${e.persona}`,
      `- **Screen:** ${e.screen} — \`${e.url}\``,
      `- **What just happened:** ${e.what}`,
      `- **What the user does next:** ${e.next}`,
      '',
    )
  }
  writeFileSync(GUIDE, lines.join('\n'))
}

// ---------------------------------------------------------------------------------------------

async function main() {
  // Cleared each run: a folder holding shots from a previous, half-finished attempt is a guide that
  // lies about which screen followed which.
  rmSync(SHOTS, { recursive: true, force: true })
  mkdirSync(SHOTS, { recursive: true })
  const browser = await chromium.launch()
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } })
  const page = await ctx.newPage()
  page.setDefaultTimeout(20000)

  // The last API response that failed, kept so a refusal can be reported from the SERVER's own words
  // rather than by scraping whatever happens to be on screen. Reading the DOM gave me an attachment
  // filename three times in a row while the actual answer was a 428 in the network tab.
  page.on('response', async (res) => {
    if (!res.url().includes('/api/v1/') || res.status() < 400) return
    const body = await res.text().catch(() => '')
    lastApiFailure = `${res.request().method()} ${res.url().replace(/^https?:\/\/[^/]+/, '')} -> ${res.status()} ${body.slice(0, 600)}`
  })

  try {
    await act1PublicAndAdmin(page)
    await act2StaffAccounts(page, ctx)
    await act3SupplierRegisters(page, ctx)
    await act4Onboarding(page)
    await act5ReviewAndApprove(page)
    await act6Approve(page)
    await act6bOfferings(page)
    await act7EvaluationTemplate(page)
    await act7AuthorRfq(page)
    await act8ItemsAndReview(page)
    await act9PublishAndInvite(page)
  } catch (e) {
    // A driver that dies without saying where it was is a driver you debug by guessing. This is the
    // one screenshot that is not part of the guide.
    await page.screenshot({ path: SHOTS + '00-FAILURE.png', fullPage: true }).catch(() => {})
    // The controls actually on screen, by accessible name. Faster to read than the screenshot and it
    // says exactly what a locator should have asked for.
    const controls = await page.evaluate(() => {
      const name = (el) => el.getAttribute('aria-label')
        || document.querySelector(`label[for="${el.id}"]`)?.textContent?.trim()
        || el.getAttribute('placeholder') || ''
      return [...document.querySelectorAll('input,textarea,select,button,[role=combobox]')]
        .filter((el) => !el.disabled || el.tagName === 'BUTTON')
        .map((el) => `${el.tagName.toLowerCase()}${el.disabled ? '[disabled]' : ''} :: ${name(el) || el.textContent?.trim()?.slice(0, 40) || '(unnamed)'}`)
    }).catch(() => [])
    console.error('  controls on screen:'); for (const c of controls) console.error('    ' + c)
    console.error(`\n  failed at step ${step + 1}, on ${page.url()}`)
    console.error(`  see walkthrough/screenshots/00-FAILURE.png`)
    throw e
  } finally {
    writeGuide()
    await browser.close()
    console.log(`\nWrote ${entries.length} screenshots and GUIDE.md`)
  }
}

async function act1PublicAndAdmin(page) {
  console.log('\nAct 1 — the public surface and the bootstrap admin')

  await page.goto(APP)
  await settle(page)
  await shot(page, 'anonymous', 'Landing page',
    'The portal answers with a live health check and the currencies the system booted with. This is a clean database: reference data only.',
    'Click Sign in, or About in the footer to see which build is deployed.')

  await page.getByRole('link', { name: /^about$/i }).click()
  await settle(page)
  await shot(page, 'anonymous', 'About',
    'Reached by clicking the footer, not by typing the address — this screen had no link to it until batch 12. It names the version and the exact commit deployed.',
    'Quote the build reference when reporting a problem. Go back and sign in.')

  await signIn(page, ADMIN.email, ADMIN.password, { totpSecret: TOTP_SECRET })
  await shot(page, 'system_admin', 'Choose your language',
    'SCR-010, asked once and only of someone who has not answered it. The product ships Arabic-first, so this is the first thing a new account sees, in both languages at once — a language chooser written only in the language you cannot read is no use.',
    'Pick a language. The choice is stored against the account, so it is not asked again on any device.')
  await chooseEnglish(page)
  await shot(page, 'system_admin', 'Back-office landing',
    'Signed in as the bootstrap administrator — the only account that exists on a fresh database. It required a six-digit authenticator code: system_admin is the one role for which MFA is mandatory.',
    'Open Staff to create the people who actually run a tender.')
}

const ORG_NAME = 'Directorate of School Services'

async function act2StaffAccounts(page, ctx) {
  console.log('\nAct 2 — a buying body, then the staff who work in it')

  // The organization comes FIRST, and it has to: BRULE-029 scopes every tender query by the caller's
  // buying body, so an officer invited without one signs in, opens the tender list, presses New RFQ
  // and gets a bare 404 from a create with nowhere to put the row. Before batch 12 there was no way
  // to set it at all - the invitation did not carry one and nothing else assigned it.
  await page.getByRole('link', { name: /^organizations$/i }).click()
  await settle(page)
  await shot(page, 'system_admin', 'Organizations (empty)',
    'No buying bodies yet. An organization is the unit a tender belongs to and the boundary every query is scoped by, so nothing procurement-shaped can exist before one does.',
    'Create the directorate that will run this tender.')

  await page.getByRole('button', { name: /create organization/i }).first().click()
  const od = page.getByRole('dialog')
  await od.getByLabel(/legal name \(english\)/i).fill(ORG_NAME)
  await od.getByLabel(/legal name \(arabic\)/i).fill('مديرية الخدمات المدرسية')
  await chooseOption(od, /type/i, /.+/)
  await fillIfPresent(od.getByLabel(/email/i), 'services@mots.local')
  await od.getByRole('button', { name: /save|create|add/i }).last().click()
  await settle(page)
  await shot(page, 'system_admin', 'Buying body created',
    'The directorate exists. Staff invited into it inherit its scope, and a tender they raise belongs to it.',
    'Invite the people who will run the procurement.')

  await page.getByRole('link', { name: /^staff$/i }).click()
  await settle(page)
  await shot(page, 'system_admin', 'Staff',
    'The staff list, empty apart from the administrator. There is no other way in: registration only ever creates a supplier, so ministry accounts must be invited from here.',
    'Invite each role the procurement needs — officer, manager, evaluator, reviewer and the Ministry viewer.')

  for (const [key, s] of Object.entries(STAFF)) {
    await inviteStaff(page, s)
    console.log(`      invited ${s.role} <${s.email}>`)
  }

  await page.reload()
  await settle(page)
  await shot(page, 'system_admin', 'Staff after invitations',
    `Six invitations sent. Each recipient gets an email with a single-use link; nobody has a password until they set one, so an unaccepted invitation is not a live account.`,
    'Each person opens their invitation and sets a password.')

  for (const s of Object.values(STAFF)) {
    await acceptStaffInvite(ctx, s)
    console.log(`      accepted   ${s.email}`)
  }
}

async function inviteStaff(page, s) {
  await page.getByRole('button', { name: /invite|دعوة/i }).first().click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel(/email/i).fill(s.email)
  await dialog.getByLabel(/name/i).fill(s.name)
  await chooseOption(dialog, /role/i, new RegExp(s.role.replace(/_/g, '[ _]'), 'i'))
  // ministry_viewer is left unassigned on purpose - BRULE-086 grants the Ministry access ACROSS
  // organizations, so pinning it to one would be a narrower grant wearing the same name.
  if (s.role !== 'ministry_viewer') await chooseOption(dialog, /buying body/i, new RegExp(ORG_NAME.slice(0, 14), 'i'))
  await dialog.getByRole('button', { name: /send|invite|دعوة/i }).click()
  await page.waitForLoadState('networkidle')
}

async function acceptStaffInvite(ctx, s) {
  const m = await mail(({ to, raw }) => to === s.email && /accept-staff-invite/.test(raw))
  const link = firstLink(m.raw, 'accept-staff-invite')

  for (let attempt = 1; attempt <= 2; attempt++) {
    await paceSignIn()
    const p = await ctx.newPage()
    await p.goto(link)

    const password = p.getByLabel(/new password/i)
    await password.first().waitFor({ state: 'visible' })
    await password.first().fill(PW)
    await p.getByRole('button', { name: /set|accept|continue|save|sign/i }).first().click()

    // Accepted when the form is GONE. Waiting on the field to detach is the only signal that does not
    // depend on guessing the success wording, and the form staying put is exactly what a silent
    // refusal looks like - which the first version reported as success for all six accounts.
    const settled = await password.first()
      .waitFor({ state: 'detached', timeout: 15000 })
      .then(() => true)
      .catch(() => false)

    if (settled) { await p.close(); return }

    const alert = await p.locator('[role=alert]').allInnerTexts().catch(() => [])
    await p.close()
    if (attempt === 2) {
      throw new Error(`invitation for ${s.email} was not accepted: ${alert.join(' | ') || 'the form did not clear'}`)
    }
    console.log(`      ${s.email} did not take on attempt ${attempt} - retrying`)
    await new Promise((r) => setTimeout(r, 8000))
  }
}

async function act3SupplierRegisters(page, ctx) {
  console.log('\nAct 3 — a supplier registers')

  await signOut(page)
  await page.goto(`${APP}/register`)
  await settle(page)
  await shot(page, 'anonymous', 'Register',
    'The only self-service account creation in the product, and it always produces a supplier. Ministry staff cannot register; they are invited.',
    'Fill in the company name in both languages and an email, then submit.')

  await page.getByLabel(/company name \(english\)/i).fill(SUPPLIER.nameEn)
  await page.getByLabel(/company name \(arabic\)/i).fill(SUPPLIER.nameAr)
  // The two fields the first run missed. They are required, and the form is right to require them:
  // a supplier with no named contact is one nobody can ask a clarification of.
  await page.getByLabel(/primary representative name/i).fill(SUPPLIER.repName)
  await page.getByLabel(/phone/i).last().fill(SUPPLIER.repPhone)
  await page.getByLabel(/^email/i).fill(SUPPLIER.email)
  await page.getByLabel(/^password/i).first().fill(SUPPLIER.password)
  await page.getByLabel(/confirm password/i).fill(SUPPLIER.password)
  await shot(page, 'anonymous', 'Register — filled in',
    'Every required field answered. The company name is asked in both languages because the tender record it will appear on is bilingual.',
    'Submit. Nothing is trusted until the email address is proven.')
  await page.getByRole('button', { name: /create account/i }).click()
  await settle(page)
  await shot(page, 'supplier_admin', 'Registered — check your email',
    'The account exists but cannot sign in yet. Nothing is trusted until the address is proven.',
    'Open the verification email and follow its link.')

  const m = await mail(({ to, raw }) => to === SUPPLIER.email && /verify-email/.test(raw))
  const link = firstLink(m.raw, 'verify-email')
  await page.goto(link)
  await settle(page)
  await shot(page, 'supplier_admin', 'Email verified',
    'The address is proven and the account is live. The supplier is in Draft: registered, not yet allowed to bid.',
    'Sign in and complete the company profile.')
}


async function act4Onboarding(page) {
  console.log('\nAct 4 — the supplier completes its profile')

  await signIn(page, SUPPLIER.email, SUPPLIER.password)
  await chooseEnglish(page)
  await shot(page, 'supplier_admin', 'Supplier dashboard (new account)',
    'The supplier can sign in now. There is no tender activity yet and the screen says so rather than showing empty widgets: what it offers instead is the completeness of the profile that stands between this account and bidding.',
    'Open Onboarding and fill in the company record.')

  await page.goto(`${APP}/onboarding`)
  await settle(page)
  await shot(page, 'supplier_admin', 'Onboarding — checklist',
    'The checklist is the gate. Each item is a condition the server enforces on submission, so this screen and the refusal cannot disagree.',
    'Fill in the legal information first.')

  await page.getByLabel(/legal name \(english\)/i).fill('Gulf Catering Company LLC')
  await page.getByLabel(/legal name \(arabic\)/i).fill('شركة الخليج للتموين ذ.م.م')
  await page.getByLabel(/registration number/i).first().fill('CR-2026-88431')
  await page.getByLabel(/tax id/i).fill('TAX-5590127')
  await page.getByRole('button', { name: /^save$/i }).first().click()
  await settle(page)
  await shot(page, 'supplier_admin', 'Onboarding — legal information saved',
    'The legal identity is recorded. This is the block a reviewer checks against the registration certificate.',
    'Fill in the company profile below it — currency and the contact phone are both required.')

  // The currency control is a Radix Select, named by its placeholder rather than wired to the visible
  // label, so it answers to getByRole('combobox') and not to getByLabel.
  const currency = page.getByRole('combobox', { name: /^currency$/i })
  await currency.first().click()
  await page.getByRole('option', { name: /syrian pound|SYP/i }).first().click()
  await page.getByLabel(/primary contact.s phone/i).first().fill('944112233')
  await page.getByRole('button', { name: /^save$/i }).nth(1).click()
  await settle(page)
  // Asserted, not assumed. The first version selected a currency, clicked Save, and moved on - and
  // the value never reached the server, which only surfaced four steps later as a submit button that
  // would not enable and no indication of which condition was unmet.
  await assertNotMissing(page, /currency/i, 'currency did not save')
  await shot(page, 'supplier_admin', 'Onboarding — company profile saved',
    'Currency and a reachable contact are recorded. The currency matters later: a proposal is priced in it, and the comparison matrix refuses to convert between currencies it was never told the rate for.',
    'Add a head-office address, a bank account, and the categories this company supplies.')

  await addContact(page)
  await addAddress(page)
  await addBankAccount(page)

  await page.goto(`${APP}/onboarding/offerings`)
  await settle(page)
  await shot(page, 'supplier_admin', 'Onboarding — categories',
    'What this company supplies, chosen from the ministry-maintained category list rather than typed. Invitations are matched against these.',
    'Tick at least one category — a supplier with none cannot be matched to any tender.')
  // Clicked, then confirmed from a RELOAD.
  //
  // Playwright's check() asserts the box is ticked the moment the click returns, and it is not: the
  // link is written server-side (confirmed in the database) but the page does not re-tick until the
  // profile is re-read. Logged as a finding rather than worked around silently - a control that
  // accepts a click and shows nothing is one a user clicks again.
  const catering = page.getByRole('checkbox').nth(1)
  await catering.click()
  await settle(page)
  await page.reload()
  await settle(page)
  await shot(page, 'supplier_admin', 'Onboarding — category chosen',
    'The category link is recorded and the completeness checklist drops one item.',
    'Upload the documents the ministry requires before an application can be submitted.')
}

/**
 * One filler per dialog, because the three are not alike.
 *
 * The first version drove all three through a single generic routine that filled every text box it
 * found. It left the address dialog's region and country and the banking dialog's currency untouched,
 * so both saved nothing - and the failure surfaced two acts later as a submit button that would not
 * enable, with no indication which condition was unmet.
 */
async function addContact(page) {
  await page.goto(`${APP}/onboarding/contacts`)
  await settle(page)
  await page.getByRole('button', { name: /add representative/i }).first().click()
  const d = page.getByRole('dialog')
  await d.getByLabel(/full name/i).fill('Yara Mansour')
  await d.getByLabel(/^email/i).fill('yara.mansour@gulfcatering.example')
  await fillIfPresent(d.getByLabel(/^phone/i), '944112233')
  await fillIfPresent(d.getByLabel(/position/i), 'Commercial Director')
  await d.getByRole('button', { name: /^save$/i }).click()
  await settle(page)
  // The dialog closing IS the creation. Without this the walk screenshotted an open dialog, captioned
  // it "Tender created", and carried on against a tender that did not exist.
  const stillOpen = await d.count()
  if (stillOpen) {
    const why = await d.locator('[role=alert], p').allInnerTexts().catch(() => [])
    throw new Error(`the tender was not created: ${why.join(' | ').slice(0, 300) || 'the dialog did not close'}`)
  }
  await shot(page, 'supplier_admin', 'Onboarding — contacts',
    'A primary representative is recorded. This is the person a buyer addresses a clarification to, and the completeness rule requires their phone number specifically — a supplier nobody can reach mid-tender is one a buyer cannot include.',
    'Add the head-office address.')
}

async function addAddress(page) {
  await page.goto(`${APP}/onboarding/addresses`)
  await settle(page)
  await page.getByRole('button', { name: /add address/i }).first().click()
  const d = page.getByRole('dialog')
  await chooseOption(d, /kind/i, /head office/i)
  // The trailing " *" is part of the accessible name on every required field, so an end-anchored
  // regex misses it - and "Address" has to stay distinguishable from "Address line 2", which is why
  // the anchor is there at all.
  await d.getByLabel(/^address( \*)?$/i).fill('12 Al-Thawra Street')
  await fillIfPresent(d.getByLabel(/address line 2/i), 'Mazzeh')
  await d.getByLabel(/^city( \*)?$/i).fill('Damascus')
  await chooseOption(d, /region/i, /.+/)
  await d.getByLabel(/^country( \*)?$/i).fill('Syria')
  await fillIfPresent(d.getByLabel(/postal code/i), '0100')
  await d.getByRole('button', { name: /^save$/i }).click()
  await settle(page)
  await shot(page, 'supplier_admin', 'Onboarding — addresses',
    'A head-office address, which is one of the completeness conditions: an approved supplier with no registered address is one nobody can serve notice on.',
    'Add the bank account an award would be paid into.')
}

async function addBankAccount(page) {
  await page.goto(`${APP}/onboarding/banking`)
  await settle(page)
  await page.getByRole('button', { name: /add account/i }).first().click()
  const d = page.getByRole('dialog')
  await d.getByLabel(/account holder name/i).fill('Gulf Catering Company LLC')
  await d.getByLabel(/bank name/i).fill('Commercial Bank of Syria')
  await fillIfPresent(d.getByLabel(/branch name/i), 'Damascus Main')
  await d.getByLabel(/account number/i).fill('SY8600000000000012345678901')
  await fillIfPresent(d.getByLabel(/swift/i), 'CBSYSYDA')
  await chooseOption(d, /currency/i, /syrian pound|SYP/i)
  await d.getByRole('button', { name: /^save$/i }).click()
  await settle(page)
  await shot(page, 'supplier_admin', 'Onboarding — banking',
    'Where an award would be paid. The account number is masked everywhere it is shown again, including on the reviewer\'s screen.',
    'Choose the categories this company supplies.')
}

/** Radix selects are named by their placeholder, so they answer to role=combobox, not to a label. */
/**
 * Saves a dialog and proves it closed.
 *
 * The dialog going away IS the save. Every `click Save; screenshot` pair in the first draft of this
 * walk was a claim without evidence, and three of them were false - a refused save leaves the form
 * open, the screenshot still gets captioned "created", and the consequence lands acts later.
 */
async function saveDialog(page, dialog, what, buttonRe = /^save$/i) {
  lastApiFailure = null
  await dialog.getByRole('button', { name: buttonRe }).last().click()
  await settle(page)
  if (await dialog.count()) {
    throw new Error(`${what} was refused: ${lastApiFailure ?? 'the dialog did not close and no API call failed'}`)
  }
}


async function chooseOption(scope, nameRe, optionRe) {
  const box = scope.getByRole('combobox', { name: nameRe })
  if (!(await box.count())) return
  await box.first().click()
  // The options render in a PORTAL at the document root, so they are looked up on the page rather
  // than inside the dialog that owns the control. `scope` is sometimes the page itself and sometimes
  // a dialog locator, and only the latter has .page() - hence the check rather than a bare call.
  const root = typeof scope.page === 'function' ? scope.page() : scope
  await root.getByRole('option', { name: optionRe }).first().click()
}

/** `datetime-local` wants local wall-clock time with no zone, which toISOString does not give. */
function localDateTime(offsetMs) {
  const t = new Date(Date.now() + offsetMs)
  const pad = (n) => String(n).padStart(2, '0')
  return `${t.getFullYear()}-${pad(t.getMonth() + 1)}-${pad(t.getDate())}T${pad(t.getHours())}:${pad(t.getMinutes())}`
}

async function fillIfPresent(locator, value) {
  if (await locator.count()) await locator.first().fill(value)
}

/**
 * Proves a write landed, by re-reading the page rather than the API.
 *
 * The access token lives in memory (zustand, with the refresh token in an httpOnly cookie), so there
 * is nothing in localStorage to borrow for a direct API call - the first version of this helper
 * assumed there was and reported "could not read profile" for every check. Reloading and reading the
 * completeness checklist is also the better test: it asserts what the SUPPLIER can see.
 */
async function assertNotMissing(page, labelRe, message) {
  await page.goto(`${APP}/onboarding`)
  await settle(page)
  const row = page.getByRole('listitem').filter({ hasText: labelRe }).first()
  if (!(await row.count())) throw new Error(`${message}: no checklist row matching ${labelRe}`)
  const text = (await row.innerText()).toLowerCase()
  if (text.includes('missing')) throw new Error(`${message} - the checklist still lists it as missing`)
}

async function act5ReviewAndApprove(page) {
  console.log('\nAct 5 — documents, terms, and the reviewer')

  await page.goto(`${APP}/onboarding`)
  await settle(page)
  await uploadRequiredDocuments(page)

  await acceptTerms(page)

  await page.goto(`${APP}/onboarding`)
  await settle(page)
  await shot(page, 'supplier_admin', 'Onboarding — ready to submit',
    'Every required document is uploaded and scanned. The submit button is offered only now: the server enforces the same list, so a submission that looks possible here is one that will be accepted.',
    'Submit the application for review.')

  await page.getByRole('button', { name: /submit application/i }).first().click()
  await settle(page)
  // Asserted, and the assertion reports the SERVER's own list of what is missing. The submit used to
  // be wrapped in a silent `if (count)`: the click happened, the server refused it with a 422 naming
  // the unmet conditions, the screenshot was taken anyway and captioned "submitted", and the walk
  // carried on for four more steps against a supplier that was never submitted.
  const stillOnForm = await page.getByRole('button', { name: /submit application/i }).count()
  if (stillOnForm) {
    const blockers = await page.locator('[role=alert], [role=status], ul li').allInnerTexts().catch(() => [])
    throw new Error(`the application was refused: ${blockers.join(' | ').slice(0, 300) || 'no reason rendered'}`)
  }
  await shot(page, 'supplier_admin', 'Application submitted',
    'The profile is now read-only and sits in the reviewer queue. The supplier cannot edit what is being judged while it is being judged.',
    'Wait for the ministry reviewer. Sign in as the reviewer to see the other side.')

  await signOut(page)
  await signIn(page, STAFF.reviewer.email, PW)
  await chooseEnglish(page)
  await shot(page, 'onboarding_reviewer', 'Reviewer landing',
    'The onboarding reviewer signs in. Their navigation carries exactly two working links — the review queue and its dashboard — because supplier.review is the only permission this role holds.',
    'Open the reviewer dashboard.')

  await page.getByRole('link', { name: /onboarding review/i }).click()
  await settle(page)
  await shot(page, 'onboarding_reviewer', 'Reviewer dashboard',
    'SCR-300, and until batch 12 nothing in the app linked to it. It reports the oldest waiting case and the queue age, which is the question a reviewer actually opens the product to answer.',
    'Open the review queue and take the waiting application.')

  await page.getByRole('link', { name: /supplier application review/i }).first().click()
  await settle(page)
  await shot(page, 'onboarding_reviewer', 'Review queue',
    'One application waiting — the one created in act 3. The queue is row-scoped: a reviewer sees applications, never tender data.',
    'Open the application and check it against its documents.')

  // The row links on the supplier's NAME, not its reference code - and this used to be wrapped in an
  // `if (count)` that skipped silently when it matched nothing, so the walk stayed on the queue and
  // failed one step later looking for an Approve button that was never on screen. No silent skips.
  await page.getByRole('link', { name: new RegExp(SUPPLIER.nameEn, 'i') }).first().click()
  await settle(page)
  await shot(page, 'onboarding_reviewer', 'Application detail',
    'The whole submitted profile in one place, with every uploaded document downloadable. This is the screen the completeness rules exist to make answerable.',
    'Approve, reject, or request more information. Each one demands a written reason.')
}

/**
 * The last completeness condition, and the only one that is a decision rather than a field.
 *
 * Two controls, deliberately: ticking the box is not the acceptance - pressing the button is. The
 * server records who accepted, when, and against which version of the terms, which is the whole point
 * of separating them.
 */
async function acceptTerms(page) {
  await page.goto(`${APP}/onboarding`)
  await settle(page)
  const box = page.locator('input[type=checkbox]').first()
  await box.check()
  await page.getByRole('button', { name: /accept/i }).first().click()
  await settle(page)
  await assertNotMissing(page, /terms/i, 'terms were not accepted')
  await shot(page, 'supplier_admin', 'Onboarding — terms accepted',
    'The terms are accepted, recorded against a named version and a timestamp. The tick alone was not the acceptance: a separate button is, so that what is stored is an action somebody took rather than a box that happened to be ticked.',
    'Every condition is now met. Submit the application.')
}

async function uploadRequiredDocuments(page) {
  // Per ROW, and the expiry date first.
  //
  // The first version set a file on every input[type=file] it could find and reported three uploads.
  // Only one landed: an expiry-tracked type refuses an upload with no expiry date, so tax_certificate
  // - which is REQUIRED - silently never uploaded, and the walk discovered it four steps later as a
  // submission the server would not accept.
  // Only the inputs that accept a DOCUMENT. The supplier logo is a file input on this same page, and
  // an indiscriminate loop posted a PDF to it - answered UNSUPPORTED_FILE_TYPE, which then sat in the
  // failure log and got blamed on the next unrelated step.
  const fileInputs = page.locator('input[type=file][accept*="pdf"], input[type=file]:not([accept*="image"])')
  const count = await fileInputs.count()
  let uploaded = 0

  for (let i = 0; i < count; i++) {
    const input = fileInputs.nth(i)
    // The expiry field that belongs to THIS row, not the first one on the page.
    const row = input.locator('xpath=ancestor::*[self::li or self::tr or self::div][1]')
    const expiry = row.locator('input[type=date]')
    if (await expiry.count()) await expiry.first().fill('2028-06-30')

    await input.setInputFiles({
      name: `document-${i + 1}.pdf`,
      mimeType: 'application/pdf',
      buffer: Buffer.from('%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF'),
    })
    await settle(page)
    uploaded += 1

    if (uploaded === 1) {
      await shot(page, 'supplier_admin', 'Onboarding — document uploaded',
        'The file went to object storage and ClamAV scanned it before it was accepted. That scan is fail-closed by design: with clamd stopped the upload is REFUSED rather than stored unscanned, so a portal that cannot scan does not quietly accept attachments.',
        'Upload the remaining required documents, giving an expiry date where the type tracks one.')
    }
  }
  console.log(`      uploaded ${uploaded} document(s)`)
}

async function act6Approve(page) {
  console.log('\nAct 6 — approved and activated')

  // Picked up FIRST. A reviewer claims the application before deciding on it, so the queue can show
  // who is working what and two reviewers do not both open the same case.
  await page.getByRole('button', { name: /start review|pick up/i }).first().click()
  await settle(page)
  await shot(page, 'onboarding_reviewer', 'Review started',
    'Claimed. The decision controls appear only now, and the queue records who took it - which is what stops two reviewers working the same application without knowing.',
    'Check the documents, then approve, reject, or ask for more information.')

  // Scoped to the DECISION row, not the page. Every uploaded document carries its own Approve and
  // Reject, so an unscoped `Approve` matched a document's button instead - it clicked, nothing
  // happened to the application, and the screenshot still said "Application approved".
  const decisions = page.locator('div').filter({ has: page.getByRole('button', { name: /request info/i }) }).last()
  const approve = decisions.getByRole('button', { name: /^approve$/i })
  await approve.first().click()
  await settle(page)

  if (await approve.count()) {
    throw new Error(`the approval was refused: ${lastApiFailure ?? 'no failing API call was seen'}`)
  }
  await shot(page, 'onboarding_reviewer', 'Application approved',
    'Approved, with a written reason recorded against the decision. The reason is not decoration: an approval nobody can account for later is the thing an audit trail exists to prevent.',
    'The supplier is now Active and can be invited to tenders. Sign in as the procurement officer.')
}

async function act7AuthorRfq(page) {
  console.log('\nAct 7 — the officer writes a tender')

  await signOut(page)
  await signIn(page, STAFF.officer.email, PW)
  await chooseEnglish(page)
  await shot(page, 'procurement_officer', 'Officer landing',
    'The procurement officer signs in. Their navigation carries the procurement dashboard, the tender list, the offering catalogue and search — the RFQ-facing half of the back office.',
    'Open the procurement dashboard, which is this role\'s home screen.')

  await page.getByRole('link', { name: /procurement dashboard/i }).first().click()
  await settle(page)
  await shot(page, 'procurement_officer', 'Procurement dashboard',
    'SCR-400, and until batch 12 nothing in the app linked to it. Tenders by state, approvals waiting, deadlines — the officer\'s actual home screen, reachable at last by clicking.',
    'Open the tender list and create one.')

  await page.getByRole('link', { name: /^rfqs$/i }).first().click()
  await settle(page)
  await shot(page, 'procurement_officer', 'Tender list (empty)',
    'No tenders exist yet: this database started empty and everything in it so far was created through these screens.',
    'Create the first RFQ.')

  await page.getByRole('button', { name: /new rfq/i }).first().click()
  const d = page.getByRole('dialog')
  await d.getByLabel(/title \(english\)/i).fill('School meals catering, 2026-2027')
  await d.getByLabel(/title \(arabic\)/i).fill('تموين وجبات مدرسية ٢٠٢٦-٢٠٢٧')
  await d.getByLabel(/currency/i).fill('SYP')
  // Both dates in the FUTURE, which the domain requires: submit-for-review refuses a window that has
  // already started - "submission dates must be in the future" - and it is right to, because a tender
  // whose bidding opened before anyone approved it was never really reviewed.
  //
  // Two minutes out, not two days, so the walk can watch the window actually open rather than
  // asserting it would. Closing stays far off so the officer closes it deliberately later.
  await d.getByLabel(/opens/i).fill(localDateTime(2 * 60 * 1000))
  await d.getByLabel(/closes/i).fill(localDateTime(3 * 24 * 60 * 60 * 1000))
  await d.getByRole('button', { name: /^save$/i }).click()
  await settle(page)
  await shot(page, 'procurement_officer', 'Tender created (Draft)',
    'The tender exists in Draft, with a reference code allocated by the server. Everything on it is editable while it stays in Draft and nothing is visible to a supplier yet.',
    'Add the line items being bought, and the requirements bidders must answer.')
}


const RFQ_TITLE = 'School meals catering, 2026-2027'

/** The tender's own page. Reached by clicking its row, never by typing the reference code. */
/**
 * Presses a lifecycle button and proves the transition happened.
 *
 * The button disappearing IS the transition: these controls are state-gated, so one that is still on
 * screen afterwards means the server refused. Every one of these used to be a bare click, and each
 * refusal was screenshotted with a caption claiming it had worked - the tender sat in Draft through
 * four more steps before anything noticed.
 */
async function transition(page, buttonRe, what) {
  lastApiFailure = null
  const button = page.getByRole('button', { name: buttonRe })
  await button.first().click()
  await settle(page)
  if (await button.count()) {
    throw new Error(`${what} was refused: ${lastApiFailure ?? 'no failing API call was seen'}`)
  }
}


async function openTender(page) {
  await page.goto(`${APP}/back-office/rfqs`)
  await settle(page)
  // The RFQ list links on the REFERENCE CODE and shows the title in the next cell, so the row is
  // reached by the code the server allocated - which the walk does not know in advance and reads off
  // the page rather than guessing.
  await page.getByRole('link', { name: /^RFQ-\d{4}-\d+$/ }).first().click()
  await settle(page)
}

async function act8ItemsAndReview(page) {
  console.log('\nAct 8 — what is being bought, and internal review')

  await openTender(page)
  await shot(page, 'procurement_officer', 'Tender detail (Draft)',
    'The tender in Draft. Items, requirements, attachments and the evaluation template are all editable here and nowhere else: once it leaves Draft the editing controls disappear rather than failing on use.',
    'Add the line item being bought.')

  await page.getByLabel(/title \(english\)/i).first().fill('Hot lunch, primary school, per pupil per day')
  await page.getByLabel(/title \(arabic\)/i).first().fill('وجبة غداء ساخنة، مرحلة ابتدائية، للتلميذ يومياً')
  await chooseOption(page, /category/i, /catering/i)
  await chooseOption(page, /unit/i, /.+/)
  await page.getByLabel(/quantity/i).first().fill('180000')
  await page.getByRole('button', { name: /add item/i }).click()
  await settle(page)
  await shot(page, 'procurement_officer', 'Line item added',
    'One line, priced per pupil per day, with a quantity for the school year. A bid is priced against these lines, so the comparison matrix later compares like with like.',
    'Add a requirement bidders must answer in words.')

  const textEn = page.getByLabel(/text \(english\)/i).first()
  if (await textEn.count()) {
    await textEn.fill('Describe your cold-chain and delivery plan for twelve sites before 07:30 daily.')
    await page.getByLabel(/text \(arabic\)/i).first().fill('صف خطة سلسلة التبريد والتوصيل لاثني عشر موقعاً قبل الساعة ٧:٣٠ يومياً.')
    await page.getByRole('button', { name: /add requirement/i }).click()
    await settle(page)
  }
  await shot(page, 'procurement_officer', 'Requirement added',
    'A requirement is answered in prose and scored by an evaluator, which is what separates it from a line item. The two halves of a bid — the technical answer and the price — are sealed from each other until consolidation.',
    'Attach the tender documents suppliers need to read.')

  await attachTenderDocument(page)

  // Binding the template is what makes the tender reviewable. It is bound rather than written here:
  // the officer chooses which scoring scheme applies, and the manager owns what the scheme says.
  // Any active template, not one matched by name: the option label is composed from the template's
  // name AND its version, and guessing that composition is how a locator breaks on a rename.
  await chooseOption(page, /evaluation template/i, /.+/)
  const bind = page.getByRole('button', { name: /bind/i })
  if (await bind.count()) { await bind.first().click(); await settle(page) }
  await shot(page, 'procurement_officer', 'Evaluation template bound',
    'The tender now carries the criteria it will be judged by, frozen at the version bound. Bidders can see what they are being scored on before they bid, which is the point of binding it this early.',
    'Submit the tender for internal review.')

  // An approver is named before the tender is sent for review, and the picker offers the managers in
  // this buying body. Naming them here is what makes segregation of duties enforceable later: the
  // person who approves the tender is recorded, not inferred from whoever happens to open it.
  await chooseOption(page, /choose an approver/i, new RegExp(STAFF.manager.name.split(' ')[0], 'i'))
  await settle(page)

  // Invitations come BEFORE review, not after publication. The domain refuses a submit-for-review on
  // a tender with no candidate supplier - "at least one candidate supplier must be invited" - which
  // says something about how this process is meant to run: the approver is shown who the tender is
  // actually going to, rather than approving it into the void and letting the officer choose later.
  // Reloaded first, and this is a real observation rather than a workaround. The suggested-supplier
  // list is fetched when the page loads and matched against the tender's item CATEGORIES - so on a
  // tender whose first item was added moments ago, the suggestion was computed before that item
  // existed and the section renders empty until something re-reads it.
  await page.reload()
  await settle(page)

  await inviteSupplier(page)

  await transition(page, /submit for review/i, 'submit for review')
  await shot(page, 'procurement_officer', 'Submitted for internal review',
    'The tender moves to InternalReview and the officer can no longer edit it. Authorship and approval are separated deliberately: the person who wrote the tender is not the person who lets it out.',
    'Sign in as the procurement manager to approve it.')
}

async function attachTenderDocument(page) {
  const input = page.locator('input[type=file]').first()
  if (!(await input.count())) return
  await input.setInputFiles({
    name: 'tender-specification.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF'),
  })
  await settle(page)
  await shot(page, 'procurement_officer', 'Tender document attached',
    'The specification suppliers will bid against. It is scanned on upload like every other file here, and it becomes readable to a supplier only once the tender is published to them.',
    'Submit the tender for internal review.')
}

async function act9PublishAndInvite(page) {
  console.log('\nAct 9 — approved, published, and the supplier invited')

  await signOut(page)
  await signIn(page, STAFF.manager.email, PW)
  await chooseEnglish(page)
  await openTender(page)
  await shot(page, 'procurement_manager', 'Tender awaiting approval',
    'The manager sees the same tender with a different set of controls: approve or return for edits, and no way to alter its contents. Reviewing something you can silently change is not a review.',
    'Approve it.')

  await transition(page, /^approve$/i, 'approve the tender')
  await shot(page, 'procurement_manager', 'Tender approved',
    'Approved. It is not yet visible to any supplier: approval and publication are separate steps, so a tender can be signed off and released on a chosen date.',
    'Hand back to the officer to publish and invite.')

  await signOut(page)
  await signIn(page, STAFF.officer.email, PW)
  await openTender(page)
  await transition(page, /^publish$/i, 'publish the tender')
  await shot(page, 'procurement_officer', 'Tender published',
    'Published. The submission window opens on its own when the start time passes - a scheduled job moves it, not a person - so the tender becomes biddable without anyone having to be at a desk.',
    'Wait for the window to open, then bid as the supplier.')
}

async function inviteSupplier(page) {
  const invite = page.getByRole('button', { name: /invite/i }).first()
  await invite.click()
  await settle(page)
  const picker = page.getByRole('combobox').last()
  if (await picker.count()) {
    await picker.click()
    const option = page.getByRole('option', { name: new RegExp(SUPPLIER.nameEn.slice(0, 12), 'i') })
    if (await option.count()) await option.first().click()
  }
  const confirm = page.getByRole('button', { name: /invite|add|send/i }).last()
  if (await confirm.count()) await confirm.click()
  await settle(page)
  await shot(page, 'procurement_officer', 'Supplier invited',
    'Invited by name from the approved list — a tender cannot be sent to a company that has not been through onboarding. The invitation is also a precondition of review: an approver is shown who the tender is going to.',
    'Submit the tender for internal review.')
}


async function act7EvaluationTemplate(page) {
  console.log('\nAct 7a — the manager defines how bids will be scored')

  // Before the tender, and that ordering is the product's, not mine: a tender cannot go to internal
  // review without a scoring template bound to it, and templates belong to the manager. The first
  // version of this walk skipped it and the submit-for-review was refused with the reason rendered
  // nowhere I could read - which is itself worth knowing.
  await signOut(page)
  await signIn(page, STAFF.manager.email, PW)
  await chooseEnglish(page)

  await page.getByRole('link', { name: /evaluation templates/i }).click()
  await settle(page)
  await shot(page, 'procurement_manager', 'Evaluation templates (empty)',
    'How bids get scored is decided before any bid exists, and by the manager rather than the officer who writes the tender. A template is reusable and versioned: activating one freezes it, and changing it later forks a new version rather than editing history.',
    'Create the template this tender will be judged against.')

  await page.getByRole('button', { name: /new template/i }).first().click()
  const d = page.getByRole('dialog')
  await d.getByLabel(/name \(english\)/i).fill('Catering services, technical and commercial')
  await d.getByLabel(/name \(arabic\)/i).fill('خدمات التموين، فني وتجاري')
  await d.getByRole('button', { name: /^save$/i }).click()
  await settle(page)
  await shot(page, 'procurement_manager', 'Template created (Draft)',
    'The template exists in draft. It scores nothing yet: criteria carry the weights, and the weights have to total 100 before it can be activated.',
    'Add the criteria bids will be scored on.')

  await addCriterion(page, 'Food safety and cold chain', 'سلامة الغذاء وسلسلة التبريد', 'Technical', '60')
  await addCriterion(page, 'Price', 'السعر', 'Commercial', '40')

  await shot(page, 'procurement_manager', 'Criteria added, weights total 100',
    'Two criteria across two of the four dimensions the product offers - Technical, Commercial, Compliance and Delivery. The weight total is shown because activation refuses anything but 100 — a template that does not add up would produce rankings nobody could defend.',
    'Activate it so a tender can bind it.')

  await transition(page, /^activate$/i, 'activate the template')
  await shot(page, 'procurement_manager', 'Template activated',
    'Activated and now bindable. From here it is frozen: a tender that binds it keeps this exact set of criteria and weights even if a later version is created.',
    'Hand back to the officer to write the tender.')
}

async function addCriterion(page, nameEn, nameAr, dimension, weight) {
  await page.getByLabel(/name \(english\)/i).last().fill(nameEn)
  await page.getByLabel(/name \(arabic\)/i).last().fill(nameAr)
  await chooseOption(page, /dimension/i, new RegExp(dimension, 'i'))
  await page.getByLabel(/^weight$/i).last().fill(weight)
  const maxScore = page.getByLabel(/max score/i).last()
  if (await maxScore.count()) await maxScore.fill('100')
  // Required, and the Add button stays disabled without it - which reads as a button that does not
  // work rather than a field that is not filled.
  await chooseOption(page, /scoring type/i, /.+/)
  await page.getByRole('button', { name: /add criterion/i }).click()
  await settle(page)
}


async function act6bOfferings(page) {
  console.log('\nAct 6b — the supplier lists what it actually sells')

  await signOut(page)
  await signIn(page, SUPPLIER.email, SUPPLIER.password)
  await page.goto(`${APP}/offerings`)
  await settle(page)
  await shot(page, 'supplier_admin', 'Offerings (empty)',
    'Ticking a category during onboarding said what this company does in principle. An offering is the concrete thing it sells, and this is the list a buyer is matched against - the invitation suggestions on a tender are built from OFFERINGS, not from the categories chosen at sign-up.',
    'Add an offering in the catering category.')

  await page.getByRole('button', { name: /add offering/i }).first().click()
  const d = page.getByRole('dialog')
  await d.getByLabel(/name \(english\)/i).fill('Hot school meals, daily delivery')
  await d.getByLabel(/name \(arabic\)/i).fill('وجبات مدرسية ساخنة، توصيل يومي')
  await fillIfPresent(d.getByLabel(/description/i), 'Cooked on site, delivered chilled to twelve schools before 07:30.')
  await chooseOption(d, /category/i, /catering/i)
  await chooseOption(d, /unit of measure/i, /.+/)
  await saveDialog(page, d, 'the offering')
  await shot(page, 'supplier_admin', 'Offering listed',
    'The company is now findable. A tender whose line items carry this category will suggest this supplier to the officer writing it — which is the whole mechanism by which a buyer discovers who can bid.',
    'Back to the officer, who writes the tender.')
}

main().catch((e) => { console.error('\nFAILED:', e.message); process.exitCode = 1 })