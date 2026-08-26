import { onCLS, onINP, onLCP } from 'web-vitals'

/**
 * Real-user web-vitals collection (docs/architecture/OBSERVABILITY-ARCHITECTURE.md: "Client:
 * web-vitals + FE error boundary"). Budgets are the canonical NFR-PERF-003/004 targets from
 * docs/product/NON-FUNCTIONAL-REQUIREMENTS.md: LCP < 2.5s, INP < 200ms, CLS < 0.1.
 *
 * There is no RUM ingestion endpoint yet, so this reports to the console for now (loud enough
 * to catch regressions during development and in CI's Playwright runs) rather than silently
 * dropping the data. Swap the report() body for a real beacon once EPIC-26's RUM sink exists.
 */
const BUDGETS = {
  LCP: 2500,
  INP: 200,
  CLS: 0.1,
} as const

function report(name: keyof typeof BUDGETS, value: number, id: string) {
  const budget = BUDGETS[name]
  const withinBudget = value <= budget
  const log = withinBudget ? console.info : console.warn
  log(`[web-vitals] ${name}=${value.toFixed(2)} budget=${budget} ${withinBudget ? 'OK' : 'OVER BUDGET'} (id=${id})`)
}

export function initWebVitals() {
  onLCP((metric) => report('LCP', metric.value, metric.id))
  onINP((metric) => report('INP', metric.value, metric.id))
  onCLS((metric) => report('CLS', metric.value, metric.id))
}
