/**
 * The one place a timestamp becomes text (T2-29).
 *
 * <p><b>Timezone.</b> ASSUMPTIONS.md ASM-004: *"Time zone for business deadlines and SLA clocks is
 * Syria time (Asia/Damascus) ... RFQ submission open/close and document-expiry windows are computed
 * and displayed in `Asia/Damascus`; storage is UTC."* Every formatter here pins `timeZone`, so the
 * viewer's own clock cannot change what a deadline reads. Before this, the app used bare
 * `new Date(x).toLocaleString()`, which rendered in whatever zone the browser happened to be in -
 * a deadline of 23:00 Damascus showed as 20:00 to a viewer on UTC.</p>
 *
 * <p><b>Numerals.</b> Arabic renders Eastern Arabic digits (٠-٩), English renders Western (0-9),
 * pinned explicitly rather than left to the locale default (ICU builds disagree on what bare `ar`
 * resolves to, which would make the digits depend on the browser).</p>
 *
 * <p>This reverses an earlier reading. RESPONSIVE-AND-RTL.md §6.1 states *"Default: Western Arabic
 * digits (0-9) ... configurable to Eastern Arabic (٠-٩) at the tenant/user level"*, which is the
 * only clause stating a rule - but every Arabic example in UX-WRITING.md renders Eastern digits, and
 * those examples are what an Arabic reader was actually shown: §3.1's «حجم هذا الملف ١٤ ميغابايت»
 * and «٣٠ أغسطس ٢٠٢٦، الساعة ١٤:٠٠ (+٣)». Ruled by the product owner in favour of the examples.</p>
 *
 * <p><b>Currency was deliberately out of scope</b> when this module was written: no document ruled on
 * money specifically, and prices are the highest-stakes number in a tender, so it was left as an
 * open question rather than inherited from example copy. <b>The product owner has since ruled that
 * money renders in Eastern Arabic digits like everything else</b>, so {@link formatCurrency} lives
 * here and shares {@link numberingSystemFor} with the dates - the point of one module is that a
 * price and a deadline on the same screen cannot disagree about what a numeral looks like.</p>
 *
 * <p>The configurability half of §6.1 is still not built: nothing in this codebase stores a
 * tenant/user numeral preference, so there is no setting to read. Locale default only.</p>
 *
 * <p><b>Calendar.</b> §6.2: *"Gregorian default"*, and *"optional Hijri display is future/optional -
 * do not invent conversion rules"*. `calendar: 'gregory'` is pinned; no Hijri path exists here.</p>
 *
 * <p><b>Deadlines.</b> §6.2: *"Deadlines always show date + time + timezone to avoid disputes"* -
 * that is {@link formatDeadline}, and it is the formatter dashboards should use for anything a
 * supplier could miss.</p>
 */

/** ASM-004. Exported so tests can assert against the same constant the app formats with. */
export const BUSINESS_TIME_ZONE = 'Asia/Damascus'

type SupportedLocale = 'ar' | 'en-GB'

/**
 * i18next hands us `en` / `ar` (or a regional variant); anything not English falls back to Arabic,
 * the product default.
 *
 * <p><b>Why `en-GB` and not `en`.</b> The docs never name a BCP-47 tag - §6.2 says only
 * "locale-aware formatting via `Intl.DateTimeFormat`" - but its worked example is day-first:
 * *"30 Aug 2026, 14:00 (+03)"*. Bare `en` resolves to US ordering in ICU ("Aug 30, 2026"), which
 * contradicts that example and reads inconsistently beside the Arabic side, which is also day-first.
 * `en-GB` is the closest tag that produces the documented shape. Flagged in the batch report as a
 * choice the documentation does not settle outright.</p>
 */
function resolveLocale(locale: string | undefined): SupportedLocale {
  return locale?.toLowerCase().startsWith('en') ? 'en-GB' : 'ar'
}

/**
 * Everything pinned regardless of locale. `numberingSystem` is NOT here - it is per-locale, see
 * {@link numberingSystemFor}.
 */
const FIXED = {
  timeZone: BUSINESS_TIME_ZONE,
  calendar: 'gregory',
} as const satisfies Intl.DateTimeFormatOptions

/** Arabic → Eastern Arabic digits, English → Western. See the module note on the ruling. */
function numberingSystemFor(locale: SupportedLocale): 'arab' | 'latn' {
  return locale === 'ar' ? 'arab' : 'latn'
}

/** The options every formatter here shares, resolved for one locale. */
function baseOptions(locale: SupportedLocale): Intl.DateTimeFormatOptions {
  return { ...FIXED, numberingSystem: numberingSystemFor(locale) }
}

/**
 * Minutes east of UTC for `timeZone` at `date`, derived from the zone itself.
 *
 * <p>Deliberately not a regex over `Intl`'s formatted offset: that string is locale- and
 * ICU-version-dependent ("GMT+3", "UTC+03:00", "غرينتش+٣"), so parsing it would make the suffix
 * depend on the browser's ICU data. `formatToParts` hands back the zone's wall-clock fields as
 * structured numbers instead; reading them back as if they were UTC and subtracting gives the offset
 * exactly, including any future DST rule change without this code knowing about it.</p>
 */
function zoneOffsetMinutes(date: Date, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone, hour12: false, numberingSystem: 'latn',
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).formatToParts(date)

  const field = (type: Intl.DateTimeFormatPartTypes) =>
    Number(parts.find((p) => p.type === type)?.value ?? '0')

  // `hour` can come back as 24 for midnight in some ICU builds; % 24 normalises it.
  const asIfUtc = Date.UTC(
    field('year'), field('month') - 1, field('day'),
    field('hour') % 24, field('minute'), field('second'),
  )
  return Math.round((asIfUtc - date.getTime()) / 60_000)
}

/**
 * The timezone suffix RESPONSIVE-AND-RTL.md §6.2 and UX-WRITING.md §3.1 both show: `(+03)` in
 * English, `(+٣)` in Arabic.
 *
 * <p><b>The two documents disagree on zero-padding</b> and the disagreement is preserved rather than
 * resolved silently: §6.2's English example is *"30 Aug 2026, 14:00 (+03)"* (padded to two digits),
 * while §3.1's Arabic counterpart is «٣٠ أغسطس ٢٠٢٦، الساعة ١٤:٠٠ (+٣)» (one digit, unpadded). Each
 * locale is rendered as its own document shows it. Reported as a documented conflict.</p>
 *
 * <p>Minutes are appended as `(+05:30)` when the zone has them. Asia/Damascus does not, so no
 * document shows that case - a choice, not a transcription.</p>
 */
function offsetSuffix(date: Date, locale: SupportedLocale): string {
  const minutes = zoneOffsetMinutes(date, BUSINESS_TIME_ZONE)
  const sign = minutes < 0 ? '-' : '+'
  const abs = Math.abs(minutes)
  const hours = Math.trunc(abs / 60)
  const rest = abs % 60

  const digits = new Intl.NumberFormat(locale, {
    numberingSystem: numberingSystemFor(locale),
    useGrouping: false,
    minimumIntegerDigits: locale === 'ar' ? 1 : 2,
  })
  const minuteDigits = new Intl.NumberFormat(locale, {
    numberingSystem: numberingSystemFor(locale),
    useGrouping: false,
    minimumIntegerDigits: 2,
  })

  const body = rest === 0 ? digits.format(hours) : `${digits.format(hours)}:${minuteDigits.format(rest)}`
  return `(${sign}${body})`
}

function toDate(value: Date | string | number): Date | null {
  const d = value instanceof Date ? value : new Date(value)
  return Number.isNaN(d.getTime()) ? null : d
}

/** "30 Aug 2026" / «٣٠ أغسطس ٢٠٢٦». Empty string for a bad value. */
export function formatDate(value: Date | string | number | null | undefined, locale?: string): string {
  if (value === null || value === undefined) return ''
  const d = toDate(value)
  if (!d) return ''
  const resolved = resolveLocale(locale)
  return new Intl.DateTimeFormat(resolved, {
    ...baseOptions(resolved), day: '2-digit', month: 'short', year: 'numeric',
  }).format(d)
}

/** Date + 24h time, no timezone suffix - for timestamps that are not deadlines (audit rows, "viewed at"). */
export function formatDateTime(value: Date | string | number | null | undefined, locale?: string): string {
  if (value === null || value === undefined) return ''
  const d = toDate(value)
  if (!d) return ''
  const resolved = resolveLocale(locale)
  return new Intl.DateTimeFormat(resolved, {
    ...baseOptions(resolved), day: '2-digit', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit', hour12: false,
  }).format(d)
}

/**
 * Date + time + timezone, per §6.2's anti-dispute rule. Use for submission close, validity end,
 * clarification deadline - anything where being an hour out has consequences.
 */
export function formatDeadline(value: Date | string | number | null | undefined, locale?: string): string {
  if (value === null || value === undefined) return ''
  const d = toDate(value)
  if (!d) return ''
  const resolved = resolveLocale(locale)
  const stamp = new Intl.DateTimeFormat(resolved, {
    ...baseOptions(resolved), day: '2-digit', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit', hour12: false,
  }).format(d)
  // §6.2 governs how the date and time are formatted - Intl does that above. Composing the
  // documented suffix beside it is not a competing formatter; `timeZoneName: 'shortOffset'` was the
  // earlier choice and produces "GMT+3", which is not the form either document shows.
  return `${stamp} ${offsetSuffix(d, resolved)}`
}

const UNITS: ReadonlyArray<[Intl.RelativeTimeFormatUnit, number]> = [
  ['year', 365 * 24 * 60 * 60 * 1000],
  ['month', 30 * 24 * 60 * 60 * 1000],
  ['day', 24 * 60 * 60 * 1000],
  ['hour', 60 * 60 * 1000],
  ['minute', 60 * 1000],
]

/**
 * "in 3 days" / "خلال ٣ أيام" and "2 hours ago" / "قبل ساعتين" - §6.2: *"Relative time ... for
 * feeds/notifications; absolute on hover/detail"*, and UX-WRITING §2: *"Timestamps: relative in
 * feeds, absolute + timezone for deadlines"*. The dashboards' countdowns are the caller this exists
 * for.
 *
 * <p>Deliberately calendar-day-agnostic: it measures elapsed milliseconds, not Damascus midnights,
 * so "in 1 day" means 24 hours rather than "tomorrow". Nothing in the docs asks for calendar-day
 * rounding, and inventing it would make the countdown disagree with the deadline it counts to.</p>
 *
 * @param now injectable so tests are not clock-dependent.
 */
export function formatRelative(
  value: Date | string | number | null | undefined,
  locale?: string,
  now: Date = new Date(),
): string {
  if (value === null || value === undefined) return ''
  const d = toDate(value)
  if (!d) return ''

  const diff = d.getTime() - now.getTime()
  const resolved = resolveLocale(locale)
  // The numbering system travels as a Unicode locale extension rather than an option:
  // `RelativeTimeFormatOptions` has no `numberingSystem` member in the TS lib, and `-u-nu-` is the
  // BCP-47 way of saying the same thing to the same ICU underneath.
  const rtf = new Intl.RelativeTimeFormat(`${resolved}-u-nu-${numberingSystemFor(resolved)}`, {
    numeric: 'auto',
  })

  for (const [unit, ms] of UNITS) {
    if (Math.abs(diff) >= ms) return rtf.format(Math.trunc(diff / ms), unit)
  }
  return rtf.format(Math.trunc(diff / 1000), 'second')
}

/**
 * Money, in the locale's numerals. «١٬٢٥٠٫٠٠ ل.س.» / "SYP 1,250.00".
 *
 * <p>Replaces two hand-rolled `Intl.NumberFormat` calls that pinned `ar-SY-u-nu-latn` - Arabic
 * layout, Western digits - which was correct under the old exclusion and is wrong under the new
 * ruling. Currency codes come from the API and are not validated here: an unknown code makes
 * `Intl.NumberFormat` throw, so it falls back to rendering the amount alone rather than blanking a
 * price or crashing a table.</p>
 */
export function formatCurrency(
  value: number | null | undefined,
  currencyCode: string | null | undefined,
  locale?: string,
): string {
  if (value === null || value === undefined || Number.isNaN(value)) return ''
  const resolved = resolveLocale(locale)
  const numberingSystem = numberingSystemFor(resolved)
  const plain = () => new Intl.NumberFormat(resolved, { numberingSystem }).format(value)

  // No code means the amount genuinely has no currency yet - a proposal before its commercial terms
  // are set, for instance. The two hand-rolled formatters this replaces defaulted to USD, which
  // renders "$50.00" for an amount that is not dollars: a false statement on a commercial document,
  // and a worse one than showing the bare number.
  if (!currencyCode) return plain()

  try {
    return new Intl.NumberFormat(resolved, { style: 'currency', currency: currencyCode, numberingSystem }).format(value)
  } catch {
    return plain()
  }
}
