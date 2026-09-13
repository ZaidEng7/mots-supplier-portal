// The submission window's two controls, and the floor underneath them.
//
// Shared because there are two places a window is set - the Create tender dialog on the list, and Edit details on the tender
// itself - and only the second one had the floor. So a tender created with an opening time in the past was accepted by the
// form and then refused at submit-for-review, with "The submission window has already started" and no way to correct it
// except opening the tender and editing it there. Reported from the create dialog, whose calendar offered this morning.
//
// The value helper formats an ISO instant the way <input type="datetime-local"> wants it: local wall time, no zone, no
// seconds. Slicing the ISO string instead would put UTC into a control the browser reads as local, which shifts every
// displayed deadline by the offset - three hours here, and silently.
//
// The floor is one hour ahead rather than "now". The domain refuses a window that has already started, and a picker offering
// the current minute lets somebody choose a time that lapses while they finish the form - which is how a tender came to be
// refused for a date that had been in the future when it was typed. It is read once per render, which is near enough: this
// is a floor on a control, and the domain is still the rule.

export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

export function earliestSubmissionInput(): string {
  return toLocalInput(new Date(Date.now() + 60 * 60 * 1000).toISOString())
}
