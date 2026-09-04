# Decisions taken

Decisions made in the course of building, that were **not** settled by a document and are now
settled here. One entry per decision, numbered, newest last.

This file exists because `docs/` is read-only and externally owned. A ruling that changes what the
product says or does has to live somewhere a reader can find it, and a code comment is not findable
by someone asking "why does it say that". Where a decision contradicts something in `docs/`, the
entry says so plainly rather than quietly diverging.

Numbering continues the `D-` series used in the batch prompts and `BACKLOG-REMEDIATION.md` (D-6
through D-16 and R-9 were answered before this file existed; they are recorded in the plan of
record). D-17 onward are recorded here.

---

## D-17 — One word for clarification: `استيضاح`, never `إيضاح`

**Date:** 2026-09-04
**Decided by:** product owner, on the Arabic review
**Scope:** all Arabic copy, present and future

### The ruling

`UX-WRITING.md` §8's glossary governs. Every Arabic string naming a clarification uses
**`استيضاح`**. `إيضاح` is not an accepted alternative anywhere in the product.

### Why this is a glossary ruling and not a style preference

**The two words are not synonyms.** They are different derived forms of the same root, and Arabic
verb morphology makes them different acts:

| | Form | Meaning | The event it names |
|---|---|---|---|
| `استيضاح` | **X** (استفعال) | seeking clarity | the buyer **asking** |
| `إيضاح` | **IV** (إفعال) | making clear | the supplier **explaining** in reply |

Both transitions in `BUSINESS-PROCESSES.md` §4.1 are named from the *asker's* side — a buyer requests
a clarification, and the proposal enters `ClarificationRequested`. So `استيضاح` is not merely the
glossary's choice, it is the correct one for these events. The drift had two notifications naming
**the answer where they meant the question**. That is a wrong word, not an inconsistent one, which
is why it was worth a ruling rather than a preference.

### What changed

Three strings in `src/backend/Application/Notifications/NotificationCatalogue.jsonc`, all from #109:

| Key | Before | After |
|---|---|---|
| `proposal.clarification_requested` (title) | طلب **إيضاح** على عرضكم | طلب **استيضاح** على عرضكم |
| `proposal.clarification_requested` (body) | طُلب **إيضاح** بشأن العرض … | طُلب **استيضاح** بشأن العرض … |
| `proposal.revised` (body) | … رداً على طلب **الإيضاح** | … رداً على طلب **الاستيضاح** |

The RFQ clarification notifications, `status.rfq.Clarification`, and the SPA's
`clarificationsAnswered` already used `استيضاح` and were not touched. English is unchanged —
"clarification" carries both senses.

### What was deliberately NOT changed

`proposal.clarification_requested`'s body still ends `يُرجى مراجعة الطلب والرد عليه`, where `الطلب`
means the clarification request and not the RFQ named four words earlier. That ambiguity is real and
is recorded in `ARABIC-REVIEW.md` as its own item — but it is a rewording, and the Arabic was
approved as drafted. Fixing it here would have been smuggling a copy change into a glossary fix.

### How it is kept

A note at the head of the proposal-clarification block in the catalogue states the ruling and the
Form X / Form IV distinction, so the next person drafting a clarification string reads it before
choosing a word. **Nothing enforces it** — there is no test that greps for `إيضاح`. That was
considered and not built: a string-blocklist test would fail the build on a legitimate future use of
`إيضاح` for an explanation given in reply, which is the word's correct sense.

---

## D-18 — Screen strings and export strings are identical, not adapted

**Date:** 2026-09-04
**Decided by:** this batch, on the Arabic review's finding
**Scope:** the report screen and its PDF/CSV artefact

### The choice offered

Either make the three divergent strings identical, or keep them divergent under a stated rule —
*screen strings terse, export strings self-describing*, on the reasoning that an exported document is
read without the screen around it and needs more context.

### The ruling: identical. The rule was rejected.

The rule is defensible in the abstract and false here. **Both surfaces render the same table with
the same column headers**, so there is no context the export lacks. Examined one at a time, none of
the three divergences was the adaptation it resembled — each was an error:

| | Screen (before) | Export (before) | Now, both | Why |
|---|---|---|---|---|
| Cycle-time heading | `زمن الدورة` | `زمن الدورة (الوسيط بالساعات)` | `زمن الدورة` | The parenthetical restated that section's own third column header — which the export carries too. Redundancy, not context. |
| Suppliers grouping | `الموردون حسب الحالة` | `الموردون حسب حالة دورة الحياة` | `الموردون حسب حالة دورة الحياة` | The screen was **wrong**. A supplier has an `OnboardingState` as well as a `LifecycleState`; this section groups by `LifecycleState` (`SuppliersByLifecycleState` on the DTO). "By state" named neither, on a report a ministry reader may file. |
| Unmeasured marker | `غير مقيس` | `(غير مقيس)` | `(غير مقيس)` | The same table cell on both surfaces. Parentheses distinguish a marker from a value, which matters more in a CSV opened in a spreadsheet. |

Two of the three moved the **screen** to match the export, and one moved the export to match the
screen. That is the shape of the finding: there was no consistent direction of drift, which is itself
evidence that neither file was the deliberate adaptation of the other.

English moved with Arabic in every case, because the same divergence existed in both languages.

### Files

- `src/frontend/src/i18n/config.ts` — `reports.notMeasured`,
  `reports.compliance.suppliersByState`, both locales
- `src/backend/Application/Reports/ReportViews.cs` — the procurement cycle-time section heading
- `src/routes/back-office/ReportsPage.test.tsx` — the assertion on the not-measured cell

### The real defect this leaves open

The two files are **hand-maintained copies of each other** and nothing enforces that they agree. That
is how these three arose and how the next ones will. Not fixed here, because the fix is a shared
source of report copy across a C# artefact generator and a TypeScript SPA, which is a design change
rather than a consistency pass. Recorded in `ARABIC-REVIEW.md` under Set 3 so it is visible next to
the strings it governs.
