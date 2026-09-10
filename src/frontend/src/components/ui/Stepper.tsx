export type StepState = 'done' | 'current' | 'todo'

export interface Step {
  key: string
  label: string
  state: StepState
}

/**
 * Where a thing stands in a journey it cannot go backwards through.
 *
 * <p>The tender workspace used to answer this with a wrapping row of status chips, one per stage, all
 * the same size and all reading as equally live. A reader had to compare tones to find the one the
 * tender is actually at. A list with a single filled mark says it once.</p>
 *
 * <p><b>Not colour alone.</b> The mark is filled, ringed or hollow as well as coloured, the current step
 * is bold and carries `aria-current="step"`, and each step names its own state in text only a screen
 * reader hears. Turn the page greyscale and the answer survives, which is the test the chip row it
 * replaced failed.</p>
 */
export function Stepper({ steps, label, stateLabels }: Readonly<{
  steps: readonly Step[]
  label: string
  /** What each state is called, so the sr-only word is the caller's own translation. */
  stateLabels: Readonly<Record<StepState, string>>
}>) {
  return (
    <ol aria-label={label} className="m-0 flex list-none flex-col gap-1 p-0">
      {steps.map((step) => (
        <li
          key={step.key}
          aria-current={step.state === 'current' ? 'step' : undefined}
          className="flex items-start gap-3 py-0.5 text-[length:var(--text-body-sm)]"
          style={{
            color: step.state === 'current' ? 'var(--color-text-primary)' : 'var(--color-text-secondary)',
            fontWeight: step.state === 'current' ? 'var(--fw-semibold)' : undefined,
          }}
        >
          <span aria-hidden="true" className="mt-1 h-4 w-4 flex-none rounded-full" style={markStyle(step.state)} />
          <span>
            <span className="sr-only">{stateLabels[step.state]}: </span>
            {step.label}
          </span>
        </li>
      ))}
    </ol>
  )
}

function markStyle(state: StepState) {
  if (state === 'done') {
    return { backgroundColor: 'var(--color-success-solid)', border: '2px solid var(--color-success-solid)' }
  }
  if (state === 'current') {
    return {
      backgroundColor: 'var(--color-brand-solid)',
      border: '2px solid var(--color-brand-solid)',
      boxShadow: '0 0 0 3px var(--color-brand-subtle)',
    }
  }
  return { backgroundColor: 'transparent', border: '2px solid var(--color-border-strong)' }
}
