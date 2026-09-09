# Plan 6A — the six product fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the six behaviour defects the copy pass surfaced and the product owner approved, so that no screen makes a promise the code does not keep.

**Architecture:** Five of the six are frontend and reuse components that already exist — `ReasonDialog` (mandatory reason, danger variant, warning slot) and `QueryError`. The sixth restores a domain capability the specification already requires and the code half-implemented: `Rfq.PublishClarification` exists and is unreachable because `AnswerClarification` forces `PublishedToAll` unconditionally. Each task ships on its own.

**Tech Stack:** React 19, TanStack Router/Query, Tailwind 4, Radix, i18next (AR/EN, RTL), Vitest, Playwright + axe · ASP.NET Core, EF Core 10, PostgreSQL, xUnit

**Spec:** `DESIGN-IS-2026-09-09/07-copy-pass.md` (the five questions and their answers) and `docs/product/FUNCTIONAL-REQUIREMENTS.md` FR-CLR-002

## Global Constraints

- **The token layer is preserved verbatim.** `src/frontend/src/styles/tokens.css` is not edited, and `tokenConformance.test.ts` enforces it: no literal colours, no Tailwind utility where a token exists, no off-scale z-index.
- **No new dependencies.** Ask before adding one.
- **`docs/` is read-only.**
- **Every user-facing string is authored in both languages** and logged in `ARABIC-REVIEW.md` on D-65's terms: the newer Arabic ships as *accepted for the demonstration build without a line-by-line read* until a native reviewer reads it.
- **Node 22 via nvm.** `.nvmrc` says 20 and breaks the toolchain. `DOTNET_ROOT=$HOME/.dotnet`.
- **Every new test asserts its own denominator** and carries a revert-to-red control, per the repository's testing culture. A test that cannot fail is not evidence.
- Frontend commands run from `src/frontend`. Backend from the repository root.

---

## File structure

| File | Responsibility | Task |
|---|---|---|
| `src/frontend/src/components/ui/ListScreen.tsx` | `QueryError` gains an optional `error`; `ListState`/`ListCard` pass it through | 1 |
| `src/frontend/src/api/problem.ts` | `errorDetail(unknown): string \| null` — the one place that knows how to get a server explanation off an unknown throw | 1 |
| `src/frontend/src/routes/SupplierProposalPage.tsx` | Withdrawal moves behind `ReasonDialog` with a danger variant and a finality warning | 2 |
| `src/frontend/src/routes/back-office/RfqDetailPage.tsx` | The native prompt becomes `ReasonDialog`; the approver field gains its hint; the evaluator field becomes a picker | 3, 4, 5 |
| `src/backend/Domain/Rfqs/Rfq.cs` | `AnswerClarification` takes the visibility the officer chose | 6 |
| `src/backend/Application/Rfqs/RfqHandlers.cs` | The answer command carries `publishToAll` | 6 |
| `src/backend/Api/Endpoints/RfqEndpoints.cs` | The request body carries it | 6 |
| `src/frontend/src/i18n/config.ts` | Every new string, both languages | all |
| `ARABIC-REVIEW.md` | The authored Arabic, logged | all |

---

### Task 1: Read failures show the server's own explanation

The audit's §C5: five of six error strings are interchangeable, so a reader learns *that* it failed and never *why*. The why is already in the building — every API module wraps failures in a typed error whose `message` comes from `problemMessage()`, which prefers RFC 9457 `detail`. Write paths render it. Read paths throw it away and render a static fallback.

**Files:**
- Modify: `src/frontend/src/api/problem.ts`
- Modify: `src/frontend/src/components/ui/ListScreen.tsx:94-140`
- Test: `src/frontend/src/components/ui/ListScreen.test.tsx`

**Interfaces:**
- Produces: `errorDetail(err: unknown): string | null` from `api/problem.ts`; `QueryError({ error?: unknown, onRetry?: () => void })`; `ListState` and `ListCard` both accept an optional `error?: unknown` and forward it.

- [ ] **Step 1: Write the failing test**

```tsx
// src/frontend/src/components/ui/ListScreen.test.tsx
import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryError } from './ListScreen'

describe('QueryError shows the server explanation when there is one', () => {
  it('renders the error message instead of the generic fallback', () => {
    render(<QueryError error={new Error('The clarification window has closed.')} />)
    expect(screen.getByText('The clarification window has closed.')).toBeInTheDocument()
    expect(screen.queryByText('We could not load this. Try again.')).not.toBeInTheDocument()
  })

  it('falls back to the generic string when the throw carries no explanation', () => {
    render(<QueryError error={new TypeError('Failed to fetch')} />)
    expect(screen.getByText('We could not load this. Try again.')).toBeInTheDocument()
  })

  it('falls back when given no error at all', () => {
    render(<QueryError />)
    expect(screen.getByText('We could not load this. Try again.')).toBeInTheDocument()
  })
})
```

The second case is the one that matters: a network failure produces a `TypeError` whose message is
`"Failed to fetch"`, which is developer text and must never reach a reader. `errorDetail` returns a string
only for the app's own typed API errors, which are the ones built from a server `detail`.

- [ ] **Step 2: Run it and watch it fail**

Run: `npx vitest run src/components/ui/ListScreen.test.tsx`
Expected: FAIL — `QueryError` takes no `error` prop, so all three render the fallback and case 1 fails.

- [ ] **Step 3: Add `errorDetail` to `api/problem.ts`**

```ts
/**
 * The server's own explanation for a failure, or null when there is not one worth showing.
 *
 * <p>Every API module in this app throws a typed error whose `message` came from
 * {@link problemMessage} — that is, the RFC 9457 `detail` the server wrote for a human. This reads
 * it back off an unknown throw so a screen can show it.</p>
 *
 * <p><b>Why the marker rather than `instanceof Error`.</b> A dropped connection throws a plain
 * `TypeError` reading "Failed to fetch", and a bug in a component throws whatever it throws. Neither
 * is prose a supplier should be shown. Only errors this app constructed from a problem document
 * carry the marker, so only those are rendered.</p>
 */
export function errorDetail(err: unknown): string | null {
  if (err === null || typeof err !== 'object') return null
  if (!('isProblemError' in err) || (err as { isProblemError?: unknown }).isProblemError !== true) return null
  const message = (err as { message?: unknown }).message
  return typeof message === 'string' && message.trim() !== '' ? message : null
}
```

- [ ] **Step 4: Mark the typed API errors**

Every API module defines its own error class whose constructor calls `problemMessage`. Each one gains the
marker. There are 19; find them with:

```bash
grep -rln "problemMessage(" src/frontend/src/api/
```

For each, add the field to the class. The shape is identical in all of them:

```ts
export class RfqApiError extends Error {
  status: number
  /** Read by `errorDetail`: this message came from a server problem document, not from a bug. */
  readonly isProblemError = true
  constructor(status: number, body: unknown) {
    const b = body as ProblemDetails | null
    super(problemMessage(b, `Request failed: ${status}`))
    this.status = status
  }
}
```

A `Request failed: 503` fallback is developer text and must not reach a reader, so also guard it:

```ts
    super(problemMessage(b, `Request failed: ${status}`))
    this.isProblemError = b?.detail !== undefined || b?.title !== undefined
```

Declare the field as `isProblemError: boolean` where you assign it in the constructor rather than
initialising it inline.

- [ ] **Step 5: Take the prop in `QueryError`, and forward it**

```tsx
export function QueryError({ error, onRetry }: { error?: unknown; onRetry?: () => void }) {
  const { t } = useTranslation()
  const detail = errorDetail(error)
  return (
    <div role="alert" className="flex flex-col items-start gap-2">
      <p style={{ color: 'var(--color-danger-fg)' }}>{detail ?? t('common.loadFailed')}</p>
      {onRetry ? (
        <Button size="sm" variant="secondary" onClick={onRetry}>
          {t('common.retry')}
        </Button>
      ) : null}
    </div>
  )
}
```

`ListState` gains `error?: unknown` and replaces its inline error paragraph with `<QueryError error={error} />`. `ListCard` gains the same prop and passes it down. Both are optional, so no existing call site changes.

- [ ] **Step 6: Run the tests**

Run: `npx vitest run src/components/ui/ListScreen.test.tsx`
Expected: PASS, 3 tests.

- [ ] **Step 7: Prove it can fail**

Temporarily change `detail ?? t('common.loadFailed')` to `t('common.loadFailed')`. Run the tests: case 1 must fail. Restore.

- [ ] **Step 8: Pass the error at the call sites**

```bash
grep -rn "isError\|QueryError\|ListCard\|ListState" src/frontend/src/routes/ | grep -c ""
```

Every screen that already renders `QueryError` or `ListCard` passes its query's `error` through: `<QueryError error={query.error} onRetry={() => void query.refetch()} />`. The screens that render a bare `t('x.loadFailed')` paragraph (`SettingsPage:71`, `ProfilePage:40`, `SupplierDashboardPage:60,238`, `DocumentsPage:91`, `NotificationsPage:48`) become `<QueryError error={query.error} />`, which deletes those six now-unused string keys.

- [ ] **Step 9: Full suite and commit**

```bash
npx tsc --noEmit && npx vitest run && npx playwright test
git add -A && git commit -m "fix(errors): read failures show the server's explanation, not a fallback used as the message"
```

---

### Task 2: Withdrawing a proposal is terminal, and now says so

`Proposal.cs:553-555` makes `Withdrawn` terminal. The control is `variant="ghost"` — the lowest-emphasis variant in the system — and no copy tells the supplier it is final.

**Files:**
- Modify: `src/frontend/src/routes/SupplierProposalPage.tsx:424-434`
- Modify: `src/frontend/src/i18n/config.ts`
- Test: `src/frontend/src/routes/SupplierProposalPage.test.tsx`

**Interfaces:**
- Consumes: `ReasonDialog({ open, onOpenChange, onSubmit, isLoading, title, confirmLabel, variant, warning })` from `components/ReasonDialog`.

- [ ] **Step 1: Write the failing test**

```tsx
it('will not withdraw until the supplier confirms in a dialog that says it is final', async () => {
  const user = userEvent.setup()
  renderProposal({ state: 'Submitted' })

  await user.click(await screen.findByRole('button', { name: 'Withdraw proposal' }))

  // The dialog, not the mutation.
  expect(await screen.findByRole('dialog')).toBeInTheDocument()
  expect(screen.getByText(/cannot be undone/i)).toBeInTheDocument()
  expect(withdrawSpy).not.toHaveBeenCalled()

  await user.type(screen.getByLabelText('Reason'), 'Priced below cost')
  await user.click(screen.getByRole('button', { name: 'Withdraw proposal' , hidden: false }))
  expect(withdrawSpy).toHaveBeenCalledOnce()
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npx vitest run src/routes/SupplierProposalPage.test.tsx -t withdraw`
Expected: FAIL — no dialog opens; the button mutates directly.

- [ ] **Step 3: Add the strings, both languages**

```ts
// en.translation.proposal
withdrawWarning: 'Withdrawing is final. You cannot re-enter this tender, and a withdrawn proposal cannot be restored.',
// ar.translation.proposal
withdrawWarning: 'السحب نهائي. لا يمكنك العودة إلى هذه المناقصة، ولا يمكن استرجاع العرض بعد سحبه.',
```

- [ ] **Step 4: Replace the inline field with the dialog**

```tsx
const [withdrawOpen, setWithdrawOpen] = useState(false)

{canWithdraw ? (
  <Card title={t('proposal.withdrawTitle')}>
    <Button variant="danger" onClick={() => setWithdrawOpen(true)}>
      {t('proposal.withdraw')}
    </Button>
    <ReasonDialog
      open={withdrawOpen}
      onOpenChange={setWithdrawOpen}
      onSubmit={(reason) => { setWithdrawReason(reason); withdrawMutation.mutate(reason) }}
      isLoading={withdrawMutation.isPending}
      title={t('proposal.withdrawTitle')}
      confirmLabel={t('proposal.withdraw')}
      variant="danger"
      warning={t('proposal.withdrawWarning')}
    />
  </Card>
) : null}
```

`withdrawMutation` takes the reason as an argument rather than reading component state, so the value the supplier typed in the dialog is the value that is sent.

- [ ] **Step 5: Run the tests**

Run: `npx vitest run src/routes/SupplierProposalPage.test.tsx`
Expected: PASS.

- [ ] **Step 6: Log the Arabic, run the suite, commit**

Append the new key to `ARABIC-REVIEW.md` under a "Plan 6A" heading, on D-65's terms.

```bash
npx tsc --noEmit && npx vitest run && npx playwright test
git add -A && git commit -m "fix(proposal): withdrawal is terminal, so it now asks and warns"
```

---

### Task 3: The early-close reason leaves `window.prompt`

`RfqDetailPage.tsx:475-476` collects an audit reason through a native prompt in a product where every other reason field is themed. Cancelling or typing whitespace fires nothing and shows no feedback, while the copy promises the reason is recorded. `rfq.manualCloseReason` is a dead key written for this.

**Files:**
- Modify: `src/frontend/src/routes/back-office/RfqDetailPage.tsx:470-481`
- Test: `src/frontend/src/routes/back-office/RfqDetailPage.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
it('closes submissions through a dialog, and cancelling says so by leaving the tender open', async () => {
  const user = userEvent.setup()
  renderRfq({ state: 'SubmissionOpen' })

  await user.click(await screen.findByRole('button', { name: 'Close submission early' }))
  expect(await screen.findByRole('dialog')).toBeInTheDocument()

  await user.click(screen.getByRole('button', { name: 'Cancel' }))
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(closeSpy).not.toHaveBeenCalled()
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npx vitest run src/routes/back-office/RfqDetailPage.test.tsx -t "closes submissions"`
Expected: FAIL — `window.prompt` is not a dialog, and jsdom's implementation is a no-op.

- [ ] **Step 3: Replace the prompt**

```tsx
const [closeOpen, setCloseOpen] = useState(false)

<Button variant="secondary" onClick={() => setCloseOpen(true)}>
  {t('rfq.closeSubmission')}
</Button>
<ReasonDialog
  open={closeOpen}
  onOpenChange={setCloseOpen}
  onSubmit={(reason) => closeMutation.mutate(reason)}
  isLoading={closeMutation.isPending}
  title={t('rfq.closeSubmission')}
  confirmLabel={t('rfq.closeSubmission')}
  variant="danger"
  warning={t('rfq.manualCloseReason')}
/>
```

`rfq.manualCloseReason` stops being a dead key: it is the line explaining that the reason goes to the audit log. Check its current wording renders as a warning and reword it if it reads as a field label.

- [ ] **Step 4: Run the tests, then the suite, then commit**

```bash
npx vitest run src/routes/back-office/RfqDetailPage.test.tsx
npx tsc --noEmit && npx vitest run && npx playwright test
git add -A && git commit -m "fix(rfq): the early-close reason is a themed dialog, and cancelling is visible"
```

---

### Task 4: The approver nomination says which button commits it

Submitting for review also commits the approver chosen in the adjacent select. The empty option is already labelled "Any manager", so that half is not hidden. What is missing is that the *other* button commits the choice.

**Files:**
- Modify: `src/frontend/src/routes/back-office/RfqDetailPage.tsx` (the ownership card's approver field)
- Modify: `src/frontend/src/i18n/config.ts`
- Test: `src/frontend/src/routes/back-office/RfqDetailPage.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
it('says that the approver choice is committed by Submit for review', async () => {
  renderRfq({ state: 'Draft' })
  expect(await screen.findByText('Applied when you submit for review. Leave blank to let any manager approve.'))
    .toBeInTheDocument()
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npx vitest run src/routes/back-office/RfqDetailPage.test.tsx -t "committed by Submit"`
Expected: FAIL — the string does not exist.

- [ ] **Step 3: Add the strings, both languages**

```ts
// en.translation.rfq.ownership
approverHint: 'Applied when you submit for review. Leave blank to let any manager approve.',
// ar.translation.rfq.ownership
approverHint: 'يُطبَّق عند الإرسال للمراجعة. اتركه فارغاً ليتمكن أي مدير من الاعتماد.',
```

- [ ] **Step 4: Render it through the field's existing help slot**

`Field` already renders hint text and wires it up: it builds a `${id}-hint` element and folds it into the control's `aria-describedby` (`Field.tsx:17-18,35-36`). Pass `hint={t('rfq.ownership.approverHint')}` on the approver field rather than adding a bare paragraph, so the hint is associated with the control and `app-error-association.spec.ts` keeps holding.

Note `Field` renders the hint only when there is no error (`hint && !error`), which is correct here: a validation message outranks a standing explanation.

- [ ] **Step 5: Run the tests, log the Arabic, commit**

```bash
npx vitest run src/routes/back-office/RfqDetailPage.test.tsx
git add -A && git commit -m "fix(rfq): the approver nomination says which button commits it"
```

---

### Task 5: The evaluator field becomes a picker

`RfqDetailPage.tsx:103` holds `evaluatorUserId` in a text input an officer types a raw user id into, on a page that already fetches the assignee list for two other pickers (`:249`, "fetched for every buyer viewing the RFQ rather than only when a picker opens").

**Files:**
- Modify: `src/frontend/src/routes/back-office/RfqDetailPage.tsx:103,386` and the assignment card
- Modify: `src/frontend/src/i18n/config.ts`
- Test: `src/frontend/src/routes/back-office/RfqDetailPage.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
it('assigns an evaluator by name, from the list the page already holds', async () => {
  const user = userEvent.setup()
  renderRfq({ state: 'UnderEvaluation', assignees: [{ userId: 'u-1', name: 'Nadia Suleiman' }] })

  // `Select` is Radix, not a native <select>: it renders a combobox that opens a listbox, so
  // `selectOptions` does not apply. This is how every other Select test on this codebase drives one
  // (see ReviewQueuePage.test.tsx:86).
  await user.click(await screen.findByRole('combobox', { name: 'Evaluator' }))
  await user.click(await screen.findByRole('option', { name: 'Nadia Suleiman' }))
  await user.click(screen.getByRole('button', { name: 'Assign' }))

  expect(assignSpy).toHaveBeenCalledWith('RFQ-2026-000001', ['u-1'])
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npx vitest run src/routes/back-office/RfqDetailPage.test.tsx -t "assigns an evaluator by name"`
Expected: FAIL — there is no combobox on the page with that name; the control is a textbox.

- [ ] **Step 3: Swap the control**

Use the existing `Select` with `SelectOption[]` built from `assigneesQuery.data`, keeping `evaluatorUserId` as the state and `assignEvaluators(referenceCode, [evaluatorUserId])` as the call — only the control changes. Label it `t('evaluation.evaluator')`, and drop `evaluation.evaluatorUserId`, which existed to name a field that no longer asks for an id.

Express "nobody chosen yet" through `placeholder`, **not** through an empty option. `Select` ignores empty emissions on purpose — `onValueChange={(v) => v && onValueChange(v)}` (`Select.tsx:36`), guarding against a spurious `""` Radix emits while the portal mounts — so an empty option could never be selected. `placeholder` also supplies the trigger's accessible name, which is what the test above queries by.

Keep `Assign` disabled while `evaluatorUserId` is empty, so there is no default that assigns somebody by accident.

- [ ] **Step 4: Run the tests**

Run: `npx vitest run src/routes/back-office/RfqDetailPage.test.tsx`
Expected: PASS. Note that `app-keyboard.spec.ts` drives a Select on the review queue, not this one, so it is unaffected.

- [ ] **Step 5: Commit**

```bash
npx tsc --noEmit && npx vitest run && npx playwright test
git add -A && git commit -m "fix(rfq): evaluators are assigned by name, from the list the page already had"
```

---

### Task 6: Clarification answers can be private to the asker (FR-CLR-002)

`FR-CLR-002` requires that answers be *"**private** (to the asker) or **published** to all invited suppliers"*, and flags its own default as **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION] default publish-to-all for fairness**. `Rfq.PublishClarification` exists for exactly this and documents itself as *"the explicit publish action for a question answered privately at first"*. It is unreachable, because `AnswerClarification` sets `Visibility = PublishedToAll` unconditionally (`Rfq.cs:530`). That is what makes the "Publish to all" button in the interface describe a state the domain cannot produce.

**Publish-to-all stays the default.** The assumption is unconfirmed, so private must be a deliberate choice, never the fallback.

**Files:**
- Modify: `src/backend/Domain/Rfqs/Rfq.cs:518-531`
- Modify: `src/backend/Application/Rfqs/RfqHandlers.cs` (the answer command and handler)
- Modify: `src/backend/Api/Endpoints/RfqEndpoints.cs` (the request body)
- Modify: `src/frontend/src/api/rfqs.ts`, `src/frontend/src/routes/back-office/RfqDetailPage.tsx`
- Test: `src/backend/Tests/Domain/RfqClarificationTests.cs`, `src/frontend/src/routes/back-office/RfqDetailPage.test.tsx`

**Interfaces:**
- Produces: `Rfq.AnswerClarification(Guid clarificationId, string answer, bool publishToAll)`. Every existing caller passes `true`, which is the current behaviour.

- [ ] **Step 1: Write the failing domain tests**

```csharp
[Fact]
public void AnswerClarification_publishing_makes_the_answer_visible_to_all()
{
    var rfq = PublishedRfqWithQuestion(out var id);
    rfq.AnswerClarification(id, "Delivery is daily.", publishToAll: true);
    Assert.Equal(ClarificationVisibility.PublishedToAll, rfq.Clarifications.Single().Visibility);
}

[Fact]
public void AnswerClarification_privately_leaves_it_private_to_the_asker()
{
    var rfq = PublishedRfqWithQuestion(out var id);
    rfq.AnswerClarification(id, "Your registration is already on file.", publishToAll: false);
    Assert.Equal(ClarificationVisibility.PrivateToAsker, rfq.Clarifications.Single().Visibility);
}

[Fact]
public void A_privately_answered_clarification_can_still_be_published_afterwards()
{
    // This is the path PublishClarification was written for and that nothing could reach.
    var rfq = PublishedRfqWithQuestion(out var id);
    rfq.AnswerClarification(id, "Your registration is already on file.", publishToAll: false);
    rfq.PublishClarification(id);
    Assert.Equal(ClarificationVisibility.PublishedToAll, rfq.Clarifications.Single().Visibility);
}
```

- [ ] **Step 2: Run them and watch them fail**

```bash
DOTNET_ROOT=$HOME/.dotnet dotnet test --filter RfqClarificationTests
```
Expected: two compile errors (no such overload), and once the parameter exists, the private case fails because the method forces `PublishedToAll`.

- [ ] **Step 3: Take the choice in the domain**

```csharp
public void AnswerClarification(Guid clarificationId, string answer, bool publishToAll)
{
    var clarification = _clarifications.FirstOrDefault(c => c.Id == clarificationId)
        ?? throw new DomainException("Clarification not found.");
    if (clarification.Answer is not null)
    {
        throw new DomainException("This clarification has already been answered.");
    }
    if (string.IsNullOrWhiteSpace(answer)) throw new DomainException("An answer is required.");

    clarification.Answer = answer;
    clarification.AnsweredAt = DateTimeOffset.UtcNow;
    // FR-CLR-002. Publishing is the default at every layer above this: the requirement's own
    // fairness assumption is marked as requiring business confirmation, so answering privately is a
    // choice somebody makes, never something that happens because a field was left unset.
    clarification.Visibility = publishToAll
        ? ClarificationVisibility.PublishedToAll
        : ClarificationVisibility.PrivateToAsker;
}
```

- [ ] **Step 4: Carry it through the command, the handler and the endpoint**

The command record gains `bool PublishToAll`. The endpoint's request body gains `bool? PublishToAll`, and the handler reads `request.PublishToAll ?? true` — an omitted field publishes, which keeps every existing client working and keeps the fair default the fallback.

The notification the answer raises must follow the choice: a `PrivateToAsker` answer notifies **only the asking supplier**, never every invitee. Find the fan-out at `RfqHandlers.cs:1210-1222` and branch it. This is the half of the task that carries the real risk — a private answer that still notifies everyone has published it in the only way a supplier can observe.

- [ ] **Step 5: Write the fan-out test**

```csharp
[Fact]
public async Task A_privately_answered_clarification_notifies_only_the_asker()
{
    var recipients = await AnswerAndCaptureRecipients(publishToAll: false);
    Assert.Single(recipients);
    Assert.Equal(AskingSupplierId, recipients.Single());
}

[Fact]
public async Task A_published_clarification_notifies_every_invited_supplier()
{
    var recipients = await AnswerAndCaptureRecipients(publishToAll: true);
    Assert.Equal(InvitedSupplierIds.Count, recipients.Distinct().Count());
}
```

The second test is the denominator: it asserts the fan-out is still the whole invitee list, so a bug that narrows both paths cannot pass.

- [ ] **Step 6: Run the backend tests**

```bash
DOTNET_ROOT=$HOME/.dotnet dotnet test
```
Expected: PASS.

- [ ] **Step 7: The interface — the button stops describing an impossible state**

The answer form gains a two-option choice, publish-to-all selected. `rfq.clarifications.publish` and its "Private to asker" badge now describe a state that can exist, and the existing guard (`answer && visibility === 'PrivateToAsker'`) becomes reachable without changing a line of it.

```ts
// en.translation.rfq.clarifications
answerVisibility: 'Who sees this answer',
answerToAll: 'Every invited supplier',
answerToAsker: 'Only the supplier who asked',
answerToAskerHint: 'Use this only when the answer concerns that supplier alone. Anything that affects the tender must go to everyone.',
```

```ts
// ar.translation.rfq.clarifications
answerVisibility: 'من يرى هذا الرد',
answerToAll: 'جميع الموردين المدعوين',
answerToAsker: 'المورد صاحب السؤال فقط',
answerToAskerHint: 'استخدم هذا الخيار فقط عندما يخص الرد ذلك المورد وحده. وكل ما يؤثر في المناقصة يجب أن يصل إلى الجميع.',
```

- [ ] **Step 8: Write the frontend test**

```tsx
it('publishes to every invited supplier unless the officer chooses otherwise', async () => {
  const user = userEvent.setup()
  renderRfq({ state: 'SubmissionOpen', clarifications: [openQuestion] })

  await user.type(await screen.findByLabelText('Answer'), 'Delivery is daily.')
  await user.click(screen.getByRole('button', { name: 'Save answer' }))

  expect(answerSpy).toHaveBeenCalledWith(expect.objectContaining({ publishToAll: true }))
})
```

- [ ] **Step 9: Full suite, log the Arabic, commit**

```bash
npx tsc --noEmit && npx vitest run && npx playwright test
DOTNET_ROOT=$HOME/.dotnet dotnet test
git add -A && git commit -m "feat(clarifications): answers can be private to the asker, as FR-CLR-002 always required"
```

---

## Migration path

Nothing here migrates data. The one schema-adjacent change is that `ClarificationVisibility.PrivateToAsker` becomes reachable for rows created from now on; the column, the enum and the index already exist, because `PublishClarification` was written against them. Rows already in the database are all `PublishedToAll` and stay that way.

The API change is additive and backward compatible: `PublishToAll` is nullable and an omitted field publishes, so a client that has not been updated behaves exactly as it does today.

Task 1 changes what an error message says on 31 screens. There is no flag and no dual-running: the fallback is still the fallback, and the server's explanation is shown only where the server wrote one.

## Cutover criteria

This plan is done when all of the following hold:

1. `npx tsc --noEmit`, `npx vitest run` and `npx playwright test` are green, and the a11y project still runs both locales — 137 checks, with `dir="rtl"` asserted on the Arabic half.
2. `DOTNET_ROOT=$HOME/.dotnet dotnet test` is green.
3. `tokenConformance.test.ts` passes without a new exemption. A new exemption means the change reached the token layer, which this plan does not do.
4. `grep -rn "window.prompt" src/frontend/src` returns nothing.
5. `grep -rn "problemMessage\|errorDetail" src/frontend/src/routes` shows read paths using the error, and `grep -c "loadFailed" src/frontend/src/i18n/config.ts` has fallen by at least six — the keys the bare paragraphs used.
6. `Rfq.PublishClarification` has a test that reaches it through `AnswerClarification(..., publishToAll: false)`. Until that test exists, the method is still dead code with a passing suite around it.
7. Every string added here appears in `ARABIC-REVIEW.md` under a "Plan 6A" heading, recorded as accepted for the demonstration build without a line-by-line read.
8. `07-copy-pass.md`'s "Raised as questions" section is updated: each of the five carries its answer and the task that closed it.

## What this plan deliberately does not do

- It does not touch composition. The eleven flat cards on the buyer RFQ detail, the twelve flat supplier navigation links and the chart work are plans 6B, 6C and 6D.
- It does not add the "Remind suppliers who have not bid" control the first comp drew. That was an invented capability and has been removed from the comp.
- It does not reword the remaining 25 `loadFailed` strings. They are correct fallbacks; task 1 makes them fall back rather than stand in.
