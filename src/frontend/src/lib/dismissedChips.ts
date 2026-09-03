/**
 * Where SCR-120's dismissible action chips are remembered.
 *
 * <p><b>§1 says the chips are dismissible and does not say where that is remembered</b> - per
 * session, per browser, or server-side per user. This is an INVENTION, and the choice is
 * `sessionStorage`: a chip stays dismissed for as long as this tab is open and comes back on the
 * next visit.</p>
 *
 * <p>The reasoning is about the failure modes, not the storage. A chip dismissed FOREVER while a
 * document is still expiring is the worst outcome available - the supplier silences the one warning
 * that would have kept their profile compliant, and nothing ever raises it again. Per-session
 * dismissal keeps the escape hatch (the strip stops nagging while they deal with something else)
 * without letting a live condition disappear permanently: tomorrow, the document is still expiring
 * and the chip is back.</p>
 *
 * <p>Server-side per-user was the alternative and is heavier than the problem: it needs a table, an
 * endpoint and a rule for when a dismissal expires - which is the same question again, only now with
 * a migration attached.</p>
 */
const KEY = 'mots.dismissedActionChips'

function read(): string[] {
  try {
    const raw = sessionStorage.getItem(KEY)
    return raw ? (JSON.parse(raw) as string[]) : []
  } catch {
    // A browser with storage disabled gets a strip that never remembers a dismissal, which is
    // strictly better than one that throws on render.
    return []
  }
}

export function isDismissed(id: string): boolean {
  return read().includes(id)
}

export function dismiss(id: string): void {
  try {
    sessionStorage.setItem(KEY, JSON.stringify([...new Set([...read(), id])]))
  } catch {
    // Ignored for the same reason as above.
  }
}

export function clearDismissed(): void {
  try {
    sessionStorage.removeItem(KEY)
  } catch {
    // Ignored.
  }
}
