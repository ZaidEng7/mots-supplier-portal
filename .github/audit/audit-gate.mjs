#!/usr/bin/env node
// The dependency gate. Fails the build on a high or critical PRODUCTION advisory, and fails
// DIFFERENTLY when the registry could not be asked. Those two must not look the same, which is most
// of what this file is about.
//
// THE NPM FLAGS ARE EACH DELIBERATE.
//
// `--package-lock-only`: this job never runs `npm ci`, so there is no installed tree to audit. It
// resolves from the lockfile instead, which is also what makes the check reproducible - the gate
// reads the same file a reviewer reads.
//
// `--audit-level` is deliberately NOT passed. It changes the JSON report's shape and left
// `metadata.vulnerabilities` empty, so the gate counted nothing and passed. The threshold is applied
// here, over the full report, which is also what lets the failure message name the packages.
//
// `--json` is what makes the two failure modes separable. Without it the only signal is an exit
// code, and npm exits non-zero both for "found a vulnerability" and for "the registry did not
// answer". Three CI failures were the second kind, and every one of them looked exactly like the
// first.
//
// A PARSED OBJECT IS NOT YET A REPORT, which is what validate() is for. When the registry fails, npm
// prints an error object as JSON on stdout - `{ "message": "network timeout at
// .../advisories/bulk", "error": {...} }`. That parses perfectly, carries no
// `metadata.vulnerabilities`, and so counted as zero advisories: the gate passed a fixture pinned to
// a package with a known high-severity CVE. The fixture caught it on its first run, which is the
// entire reason for having one. A dependency gate that quietly stops checking is indistinguishable
// from a clean repository.
//
// Inside audit(), a non-zero exit with a report on stdout is a SUCCESSFUL audit that found
// something, not a transport failure, so it is parsed rather than thrown.
//
// A registry failure exits 2 and says so in as many words. A gate that reports "registry
// unavailable" as a dependency problem is a gate people learn to re-run without reading.
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'

const run = promisify(execFile)

const dir = process.argv[2] ?? '.'
const attempts = Number(process.env.AUDIT_ATTEMPTS ?? 3)
const blocking = ['high', 'critical']

function validate(parsed) {
  if (parsed?.metadata?.vulnerabilities === undefined) {
    throw new Error(`npm returned no audit report: ${parsed?.message ?? JSON.stringify(parsed).slice(0, 300)}`)
  }
  return parsed
}

async function audit() {
  try {
    const { stdout } = await run('npm', [
      'audit', '--json', '--omit=dev', '--package-lock-only',
    ], { cwd: dir, maxBuffer: 64 * 1024 * 1024 })
    return validate(JSON.parse(stdout))
  } catch (error) {
    if (error.stdout) {
      try { return validate(JSON.parse(error.stdout)) } catch { }
    }
    throw new Error(`${error.shortMessage ?? error.message}\n${(error.stderr ?? '').trim()}`)
  }
}

let report
const problems = []

for (let attempt = 1; attempt <= attempts; attempt++) {
  try {
    report = await audit()
    break
  } catch (error) {
    problems.push(`attempt ${attempt}: ${error.message}`)
    if (attempt < attempts) await new Promise(r => setTimeout(r, attempt * 5000))
  }
}

if (!report) {
  console.error('::error::npm audit could not reach the registry. This is NOT a vulnerability finding.')
  for (const problem of problems) console.error(`::error::${problem}`)
  process.exit(2)
}

const counts = report.metadata?.vulnerabilities ?? {}
const total = blocking.reduce((sum, level) => sum + (counts[level] ?? 0), 0)

console.log(`production advisories: ${JSON.stringify(counts)}`)

if (total > 0) {
  for (const [name, detail] of Object.entries(report.vulnerabilities ?? {})) {
    if (blocking.includes(detail.severity)) {
      console.error(`::error::${name} (${detail.severity}) - ${detail.via?.map?.(v => v.title ?? v).join(', ')}`)
    }
  }
  console.error(`::error::${total} high/critical advisory in production dependencies`)
  process.exit(1)
}

console.log('No high or critical advisories in production dependencies.')
