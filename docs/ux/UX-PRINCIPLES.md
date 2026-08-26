# UX Principles — MOTS Supplier Portal

> **Status:** Baseline v1 · **Owner:** Design Lead · **Date:** 2026-08-26
> Derived from and 100% consistent with [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md)
> and the [Discovery Report](../product/DISCOVERY-REPORT.md). Companion docs:
> [`DESIGN-SYSTEM.md`](./DESIGN-SYSTEM.md) · [`RESPONSIVE-AND-RTL.md`](./RESPONSIVE-AND-RTL.md) ·
> [`ACCESSIBILITY.md`](./ACCESSIBILITY.md) · [`UX-WRITING.md`](./UX-WRITING.md)

---

## 0. Why UX is the #1 requirement here

This is an **Arabic-first, enterprise procurement platform** for the Syrian tourism sector. Its users
are not consumers idly browsing — they are suppliers whose livelihood depends on winning tenders,
procurement officers accountable for public spend, evaluation committees making defensible decisions,
and Ministry supervisors watching governance. Every one of them arrives with **money, deadlines, and
accountability on the line**. Poor UX here does not merely annoy; it causes missed submission
windows, rejected onboarding, disputed awards, and eroded institutional trust.

The Discovery Report is explicit: **the ERP already exists** and even ships a rudimentary Frappe
supplier portal. Our reason to exist is a **premium, modern, Arabic-first experience** the ERP cannot
provide. If we build another admin-template CRUD app, the project has no justification. UX quality —
not feature count — is what gates "done."

---

## 1. The quality bar: Stripe / Linear / Notion-grade

We benchmark against the products that define modern enterprise UX. Concretely, that means:

| Reference | What we borrow | What it means *here* |
|---|---|---|
| **Stripe** | Trust through clarity; dense data that never feels heavy; impeccable forms; documentation-grade error messages | RFQ comparison tables, proposal pricing, and onboarding forms read as calm and authoritative even with many fields. Money is always formatted, aligned, and unambiguous. |
| **Linear** | Speed as a feature; keyboard-first; optimistic UI; zero spinner fatigue; opinionated defaults | Procurement officers move through RFQ authoring and evaluation with keyboard shortcuts, instant transitions, optimistic state changes, and command palette navigation. |
| **Notion** | Progressive disclosure; content that breathes; forgiving editing; nothing feels locked | Onboarding and proposal drafting autosave, never punish a wrong turn, and reveal complexity only as needed. |

**The test:** open any screen next to a Stripe dashboard. If ours looks like a Bootstrap admin
template, an AntD demo, or a Frappe form, it fails — regardless of whether the feature "works."

---

## 2. The eight core principles

Each principle below has a **definition**, a **why-here**, and **concrete rules** the design system and
components must honor.

### 2.1 Clarity — *one obvious thing to do, one obvious thing being said*

- **Why here:** Users make high-stakes, unfamiliar decisions (which supplier to shortlist, whether a
  document is compliant). Ambiguity is expensive.
- **Rules:**
  - Every screen has a single **primary action** rendered as the one filled brand button; everything
    else is secondary/tertiary/ghost.
  - State is always visible and named in plain bilingual language — never a raw enum. A proposal is
    "Submitted · بانتظار المراجعة", never `state=2`.
  - Numbers that mean money use **tabular figures**, right-aligned in tables, with currency and
    thousands separators (see [DESIGN-SYSTEM §3](./DESIGN-SYSTEM.md)).
  - No more than one visual "loudest" element per view. If two things shout, nothing is heard.

### 2.2 Trust — *this is a system of record for public procurement*

- **Why here:** Awards can be audited and disputed. The Ministry persona exists purely to supervise
  governance. Suppliers must believe the process is fair; buyers must believe it is defensible.
- **Rules:**
  - Every state-changing action shows **who / what / when** afterward, mirroring the canonical
    `AuditLog` (actor, timestamp, from→to, reason). Audit is a *feature surfaced to users*, not just a
    backend table.
  - Destructive or irreversible actions (cancel RFQ, reject supplier, finalize evaluation) require a
    **typed confirmation with a reason**, and the reason is displayed forever after.
  - We never fake progress or hide failures. If ERP sync is pending, we say so honestly
    ("Award recorded. Purchase order will sync to ERP." ) — the portal never blocks on ERP per the
    foundational ERP-boundary decision.
  - Visual language is **calm, premium, evergreen-teal** — institutional, not startup-playful. The
    restrained gold accent is reserved for genuine moments of significance (an **Award**, a KPI).

### 2.3 Guidance — *the system leads; the user never guesses "what now?"*

- **Why here:** Onboarding, RFQ, evaluation, and award are **multi-step state machines** (see
  foundational §5). Users must always know where they are and what unblocks the next step.
- **Rules:**
  - Long flows use a **stepper / workflow indicator** that mirrors the actual state machine and marks
    completed / current / blocked steps.
  - Every blocked state explains the **specific unblocking action** ("2 required documents pending
    upload" with a direct link), never a generic "incomplete."
  - Empty states are **onboarding surfaces**, not dead ends — they explain the object and offer the
    first action (see [UX-WRITING empty-state formula](./UX-WRITING.md)).
  - Dashboards are **task queues**, not vanity metrics: "5 proposals awaiting your evaluation" leads
    directly to the work.

### 2.4 Progressive disclosure — *simple by default, powerful on demand*

- **Why here:** The domain is genuinely complex (weighted criteria, line-item pricing, multi-company
  suppliers). Showing all of it at once produces the exact "enterprise heaviness" we must avoid.
- **Rules:**
  - Onboarding is **chunked into logical sections** (Legal → Contacts → Categories → Offerings →
    Documents), each independently savable, not one 60-field wall.
  - Advanced options (evaluation formula details, incoterms, validity windows) live behind expandable
    sections or drawers, with sensible defaults pre-filled.
  - Tables show **priority columns** first; the rest are available via row expansion / detail drawer
    (see responsive table patterns).
  - Detail lives in **drawers and detail pages**, keeping list views scannable.

### 2.5 Forgiveness — *mistakes are cheap and recoverable*

- **Why here:** A supplier assembling a proposal near a deadline, or a reviewer mid-assessment, cannot
  afford to lose work or fear a wrong click.
- **Rules:**
  - **Autosave drafts** for onboarding profiles and proposals (the `Draft` state exists precisely for
    this). Show a quiet "Saved · تم الحفظ" indicator, never a blocking save modal.
  - Every irreversible action is **confirmed**; every reversible one is **undoable via toast** where
    feasible ("Invitation removed — Undo").
  - Destructive actions are **visually and physically separated** from primary actions (never adjacent
    same-size buttons).
  - Validation is **inline and non-punitive** — errors appear next to the field, on blur/submit, and
    tell the user how to fix it and whether their data was kept (see error formula).
  - Withdrawal / resubmission paths that the state machines allow (proposal `Withdrawn`, onboarding
    `Resubmitted`) are first-class in the UI, not hidden.

### 2.6 Speed — *fast is the feature; perceived speed is designed*

- **Why here:** NFR targets are explicit: **API p95 < 300ms reads / < 800ms writes**, **LCP < 2.5s**,
  **INP < 200ms** on mid-range mobile (foundational §9). Suppliers are mobile; officers work all day.
- **Rules:**
  - **Optimistic UI** for low-risk mutations (marking read, reordering, toggling); reconcile on
    response, roll back visibly on failure.
  - **Skeleton screens**, not spinners, for initial loads; content-shaped placeholders that match the
    final layout. Reserve spinners for true indeterminate in-place waits under ~1s.
  - **Route-level code splitting** (per foundational §9) and TanStack Query caching so back-navigation
    is instant.
  - Keyboard-first for back-office power users: a **command palette**, focus-visible everywhere,
    logical tab order, and shortcuts for frequent transitions.
  - Never block the whole screen for a partial update; scope loading to the region that changed.

### 2.7 Accessibility — *WCAG 2.2 AA is a floor, not an aspiration*

- **Why here:** Public-sector platform; legally and ethically must serve users with disabilities and
  keyboard-only / screen-reader users. Foundational §9 mandates **WCAG 2.2 AA**.
- **Rules:** Full checklist in [`ACCESSIBILITY.md`](./ACCESSIBILITY.md). At the principle level:
  - Color is **never the sole carrier** of meaning — status chips pair color with icon + text label.
  - All contrast tokens meet AA (4.5:1 text / 3:1 large & UI). Both light and dark themes are audited.
  - Everything operable by mouse is operable by keyboard, with visible focus and correct ARIA roles.
  - Motion respects `prefers-reduced-motion`; nothing essential depends on animation.

### 2.8 Arabic-first — *RTL is the design, not a translation*

- **Why here:** Default locale is **`ar` (RTL)**; English is secondary (foundational §8). The ERP's
  own portal is not Arabic-premium — this is a core differentiator.
- **Rules:** Full detail in [`RESPONSIVE-AND-RTL.md`](./RESPONSIVE-AND-RTL.md). At the principle level:
  - We **design the Arabic screen first**, then verify LTR — not the reverse.
  - All layout uses **CSS logical properties** (`margin-inline-start`, `inset-inline-end`, …) so mirror
    is automatic and correct.
  - Typography pairs **IBM Plex Sans Arabic** with **Inter** for Latin/numerals, harmonized in size and
    weight so mixed strings never look bolted-on.
  - Direction-implying icons mirror under RTL; numerals default to Western Arabic (0–9) and are
    configurable to Eastern Arabic per the foundational assumption.
  - Bidirectional (mixed AR/EN, IDs like `RFQ-2026-000123`) text is isolated so it never scrambles.

---

## 3. Principle trade-off ladder

When two principles conflict, resolve in this order:

1. **Trust & correctness** (never sacrifice auditability, accuracy, or safety for polish)
2. **Accessibility** (never ship an inaccessible pattern for speed or aesthetics)
3. **Clarity** (never add power that muddies the primary path)
4. **Speed / delight**

Example: an optimistic UI (speed) that could show a state change that didn't actually persist to the
audit log **must** roll back visibly and honestly — trust wins over the illusion of speed.

---

## 4. Per-persona experience priorities

Each canonical persona (foundational §3) has a distinct center of gravity. The design must optimize the
right things per surface.

| Persona | Primary device | UX center of gravity |
|---|---|---|
| `supplier_admin` / `supplier_user` | **Mobile + desktop** | **Mobile-first**, guided onboarding, draft safety, deadline awareness, document status clarity. Low domain expertise assumed → maximum guidance. |
| `onboarding_reviewer` | Desktop | Efficient review queue, side-by-side document + form, one-keystroke approve / request-info with reason. |
| `procurement_officer` | Desktop | RFQ authoring speed, invitation management, clarification Q&A, keyboard-heavy. |
| `procurement_manager` | Desktop | Fast, defensible approve/reject with full context and audit visibility. |
| `evaluator` | Desktop / tablet | Focused, distraction-free scoring; blind-until-consolidated (per foundational §5 assumption); comparison tooling. |
| `ministry_viewer` | Desktop | **Read-only** governance dashboards; aggregate/anonymized where commercial values are restricted (open question); export. |
| `system_admin` | Desktop | Powerful but guard-railed configuration; RBAC clarity; dangerous actions gated. |

---

## 5. Anti-patterns — explicitly forbidden

These are the failure modes that would make us "just another admin panel." Reject them in design review.

### 5.1 The admin-template look
- ❌ MUI / AntD / Bootstrap default components used unstyled (foundational §2 forbids these frameworks).
- ❌ Generic left-nav + card-grid dashboard with meaningless stat tiles.
- ❌ Harsh borders and drop shadows; flat gray-on-gray density with no hierarchy.
- ✅ Bespoke Radix-primitive-based components, warm-stone neutrals, evergreen-teal identity, soft
  layered elevation.

### 5.2 The CRUD feel
- ❌ Screens that expose the data model directly ("Create Proposal Item", "Edit Invitation Row").
- ❌ Every entity gets an identical list/table/edit-form trio with no task framing.
- ❌ Raw enum values, database IDs, and boolean flags shown to users.
- ✅ **Task-oriented** flows named for user intent ("Submit your proposal", "Invite suppliers",
  "Score this proposal"). The data model is an implementation detail.

### 5.3 Spinner overload / loading anxiety
- ❌ Full-screen spinner on every navigation.
- ❌ Layout shift when data arrives (content jumps).
- ❌ Blocking modal spinner for background-eligible work.
- ✅ Skeletons matching final layout, optimistic updates, scoped loading, cached back-navigation.

### 5.4 Other prohibited patterns
- ❌ **Color-only status** (a red dot with no label/icon) — fails accessibility and clarity.
- ❌ **Destructive-adjacent-to-primary** buttons of equal weight.
- ❌ **Untranslated / hard-coded strings** — every string is i18next-keyed (foundational §8).
- ❌ **LTR-first layout** later "flipped" with physical-property hacks (`margin-left`) — use logical
  properties from the start.
- ❌ **Silent failures** — every error tells the user what happened, what to do, and whether data was
  saved (see [UX-WRITING error formula](./UX-WRITING.md)).
- ❌ **Vanity dashboards** — metrics with no path to action.
- ❌ **Modal-in-modal** stacking and deep confirmation chains.
- ❌ **Numeral / mixed-script scrambling** in RTL (bidi not isolated).

---

## 6. Definition of Done (UX gate)

A vertical slice is **not done** until, per foundational §11, it passes this UX gate:

- [ ] Arabic (RTL) screen designed and verified **first**; English (LTR) verified as mirror.
- [ ] Single clear primary action; correct button hierarchy.
- [ ] All states designed: **empty, loading (skeleton), error, success, and every domain state** the
      relevant state machine can reach.
- [ ] Bilingual, task-oriented copy following the [UX-Writing patterns](./UX-WRITING.md).
- [ ] Autosave / draft safety where the state machine has a `Draft`.
- [ ] Destructive actions confirmed with reason; audit surfaced.
- [ ] Responsive across breakpoints for the persona's primary device.
- [ ] Passes automated axe checks **and** manual keyboard + screen-reader pass (AA).
- [ ] No forbidden anti-pattern present.
- [ ] Perceived-performance budget met (skeletons, no layout shift, INP < 200ms interactions).

---

## 7. How these principles map to the rest of the system

| Principle | Realized in |
|---|---|
| Clarity, Trust | [`DESIGN-SYSTEM.md`](./DESIGN-SYSTEM.md) tokens, status chips, audit surfacing |
| Guidance, Progressive disclosure | Stepper/workflow components, empty states, drawers |
| Forgiveness, Speed | Autosave, optimistic UI, skeletons, toasts with undo |
| Accessibility | [`ACCESSIBILITY.md`](./ACCESSIBILITY.md) |
| Arabic-first | [`RESPONSIVE-AND-RTL.md`](./RESPONSIVE-AND-RTL.md) |
| Voice consistency | [`UX-WRITING.md`](./UX-WRITING.md) |
