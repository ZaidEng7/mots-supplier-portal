// The two densities, and the one variable that carries them.
//
// What the audit measured. 13px was the most common size on all twelve screens it looked at - for both audiences at once,
// though docs/handbook/RECONCILIATION.md had already decided they differ: 14px for a back-office officer scanning tables all day, 16px for a
// supplier reading a legal form under deadline. The rules existed and nothing applied them.
//
// Why ONE VARIABLE rather than a sweep of 191 sites. The size is a property of the audience rather than of each place text is
// written: a shell declares which audience it serves, and the shared content components read --density-body. That also means
// this can be CHECKED, which a sweep could not be.
//
// The default is the back-office size, and the default matters: sign in, register and accept-an-invitation render outside both
// shells, and a variable that resolved to nothing there would drop the declaration and fall back to the browser's 16px by
// accident rather than by decision. The supplier gets its own, larger size, and the supplier shell CLAIMS that density while
// the back office does not - which is a check that the default is doing the work rather than both shells declaring and one of
// them drifting later.
//
// The content components that decide what "body text" looks like read the variable. Table is the one that mattered: it is the
// dominant content on 33 of the 65 screens and it set all of them below the body floor in a single line.
//
// 13px is still available for what it is actually FOR - secondary text beside a value, a hint, a caption, a dialog
// description, rather than the value itself. Removing the token would be the opposite mistake, so one test asserts it
// survives.
//
// The last test is the control.

import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const SRC = resolve(process.cwd(), 'src')
const read = (relative: string) => readFileSync(resolve(SRC, relative), 'utf8')

describe('the two densities are declared, and something reads them', () => {
  const css = read('index.css')

  it('defines the variable, with the back-office size as the default', () => {
    expect(css).toMatch(/:root\s*\{[^}]*--density-body:\s*var\(--text-body\)/)
  })

  it('gives the supplier its own, larger size', () => {
    expect(css).toMatch(/\.msp-density-supplier\s*\{[^}]*--density-body:\s*var\(--text-body-lg\)/)
  })

  it('the supplier shell claims that density, and the back office does not', () => {
    expect(read('shells/SupplierShell.tsx')).toContain('msp-density-supplier')
    expect(read('shells/BackOfficeShell.tsx')).not.toContain('msp-density-')
  })

  it('the content components that decide what "body text" looks like read it', () => {
    expect(read('components/ui/Table.tsx')).toContain('text-[length:var(--density-body)]')
    expect(read('components/ui/Field.tsx')).toContain('text-[length:var(--density-body)]')
  })

  it('13px is still available for what it is actually for', () => {
    expect(read('styles/tokens.css')).toContain('--text-body-sm')
    expect(read('components/ui/ListScreen.tsx')).toContain('var(--text-body-sm)')
  })

  it('the check can fail', () => {
    expect(/\.msp-density-supplier\s*\{[^}]*--density-body/.test('.msp-density-supplier { --density-body: var(--text-body-lg); }')).toBe(true)
    expect(/\.msp-density-supplier\s*\{[^}]*--density-body/.test('.msp-density-supplier { color: red; }')).toBe(false)
  })
})
