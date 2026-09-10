/**
 * Which languages are written right to left.
 *
 * <p>A one-line constant in its own file, which needs the explanation. It used to live in
 * `i18n/config.ts` beside the 4,000 lines of strings and the `i18n.init()` call, so importing it meant
 * importing the initialisation. Components that only need to know which way the page runs - a chart
 * choosing which end its axis sits at, the toast choosing which edge it enters from - pulled the whole
 * i18next bootstrap in behind it, and any test that mocks `react-i18next` then fails at import with
 * "No initReactI18next export is defined on the mock" before a single assertion runs.</p>
 *
 * <p>`config.ts` re-exports this, so the old import path still resolves.</p>
 */
export const RTL_LANGUAGES = new Set(['ar'])
