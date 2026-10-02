# The ERP supplier import and push

> For the developer who takes over the ERP supplier integration, which works in both directions. The
> import reads the ERP's suppliers into the portal (§1 to §7). The push creates in the ERP a supplier
> that a reviewer approved in the portal (§8). For each, this page tells you where the code is, what one
> run does, the state it keeps on a supplier, how to add a field, which edit needs the owner's decision,
> and how to test. It was checked against the code on 2026-10-01. The comment above each class holds
> that class's full reasoning, and this page shows you which class to read.

---

## 1. What it does

The ministry's ERP (Seven Gates' ERPNext) is the master for the suppliers it holds. The import reads
every supplier from it and then acts on the portal:

- it creates the suppliers the portal lacks, approved without review, each with a supplier_admin account;
- it updates the suppliers the portal already has;
- it suspends or marks the suppliers the ERP no longer returns, has disabled or has not approved;
- it releases a supplier it had suspended only while that supplier waited for the ERP's approval;
- it leaves alone an ERP supplier that the push in §8 created and has not linked yet, for the push to link.

An administrator starts it from **Supplier import** (`/back-office/erp-import`), and the same import
also runs every hour on its own. The import only reads from the ERP. The one thing that writes supplier
data to the ERP is the push in §8, which creates there a supplier that a reviewer approved in the portal.

Awards are separate code. `AwardErpSyncJob` sends awards as purchase orders through
`IErpPurchaseOrderAdapter`. The only implementation today is `StubErpPurchaseOrderAdapter`, which sends
nothing. The outbox drains to `LoggingOutboxTransport`, so the `SupplierApproved` message that an
approval writes reaches no ERP either. The push does not use the outbox.

## 2. Where things live

Paths are under `src/backend` unless they start with `src/frontend`.

**The rules** are in `Application/Integration`. They are pure, with no database and no HTTP, and most
have a test file of the same name in `Tests/Unit/Erp`.

| File | Its one job |
|---|---|
| `IErpSupplierSource.cs` | The port, and `ErpSupplier`, which holds what the portal reads of an ERP supplier. Its `CreatedByPortal` says the portal's own API user owns the record, which means the push made it (§8.5). |
| `ErpImportAdmission.cs` | Fills every gap so that no supplier is left out, for example with a placeholder email on `erp-import.invalid`. Also works out the ERP's standing (`StandingOf`), words the gap, arrival and standing notes (`ReleasedNote`, `ReleaseWaitsNote`, `HeldBackNote`, `TurnedAwayNote`), and decides the address (`AddressOutcome`). Other notes are worded where they are made: in `RunErpImportHandler`, `ErpRegistrationNumbers`, `ErpAddressMapper`, `ErpFieldLimits` and `ErpImportPreviewBuilder`. |
| `ErpAddressMapping.cs` | `ErpAddressMapper`: the ERP billing address becomes a portal address in one of Syria's fourteen governorates, or a note says why not. |
| `ErpFieldLimits.cs` | Column lengths that ERP values are measured against; `Cut` and `DropIfTooLong`. |
| `ErpRegistrationNumbers.cs` | Which ERP supplier may carry which registration number. The portal allows one supplier per number. |
| `ErpSyncPlan.cs` | Decisions taken before any write: probable renames held for a person, suppliers to suspend or mark as gone, and whether suspensions are held back. It also holds the import's two rules for pushed suppliers (§8.5): `HeldForPush`, the push's own creates that no portal supplier carries yet, and `PushedAwaitingApproval`, a pushed supplier's first sighting in Draft, which the limit does not count. |
| `ErpMissingSupplierPolicy.cs` | Whether a read is believed. An empty list never is. One run may suspend at most a quarter of the active linked suppliers, and never fewer than five. |
| `ErpStandingDecision.cs` | The per-supplier decision for a supplier the portal already holds: what the ERP's standing changes, whether the plan holds it back, and the notes. The preview and the run both call `Decide`. |
| `ErpImportPreviewBuilder.cs` | Builds the preview report from those rules. |
| `ErpImportPreview.cs`, `ErpImportRun.cs` | Report shapes, handler interfaces, `ErpImportTrigger`, `ErpImportBusyException`. |

**The connection and the handlers** are in `Infrastructure/Integration/Erp`.

| File | Its one job |
|---|---|
| `ErpSupplierSource.cs` | Makes three HTTP reads: suppliers with fields `["*"]`, then contacts, then addresses, the last two filtered on their link to a supplier. Then it asks the ERP which user the connection signs in as (`ErpWire.ApiUserAsync`, the push's own read) and marks each supplier that user owns as `CreatedByPortal`. `Map` turns the JSON into `ErpSupplier` using named arguments. `ListSupplierGroupsAsync` is the push's group list (§8.6), which the import never reads. |
| `ErpContactMerge.cs`, `ErpAddressMerge.cs` | Pick each supplier's email, phone and contact person, and its one address. |
| `ErpQuery.cs`, `ErpServerTime.cs`, `ErpFailure.cs`, `ErpNotConfiguredException.cs` | URL encoding, timestamps that arrive without a zone, the ERP's refusal (`ErpRequestException`), and "no connection". |
| `ErpConnection.cs`, `ErpConnectionProvider.cs`, `ErpOptions.cs` | Where the address and credential come from. A row saved on **Connected systems** wins over the `Erp:*` settings. |
| `ErpSupplierSourceProbe.cs` | The **Test connection** button: the import's three record reads, one record each. It does not ask for the API user, which the ERP answers for any credential it lets sign in. |
| `ErpImportOptions.cs` | `ErpImport:InitialPassword`, which imported accounts are created with. |
| `ErpImportLock.cs` | A Postgres advisory lock, so that only one import, or one supplier push (§8), runs at a time. |
| `LinkedSuppliersInPortal.cs` | The shared portal reader. It reads the ERP-linked suppliers once, before the first write, for both the preview and the run. |
| `RegistrationNumbersInPortal.cs` | The registration numbers the portal already holds. |
| `PreviewErpImportHandler.cs`, `RunErpImportHandler.cs` | The two handlers. |
| `ErpSupplierSyncJob.cs` | The hourly job. It calls the run handler, so it is not a second copy of the import. When the lock is taken it tries once more, two minutes later (`RunAgainAsync`). |

**The supplier record** is `Domain/Suppliers/Supplier.cs`. The **THE ERP** section of its header lists
the ERP state and methods: `ImportFromErp`, `ApplyErpSnapshot`, `RecordErpStanding`,
`ErpDisabledChangeFor`, `IsMarkedAsUnwantedByErp`, `SuspendAsRemovedFromErp`, `MarkRemovedFromErp`,
`MarkSynced`, and the private `EndErpPendingHold`. The four ERP enums sit above the class, with one line
for every value. `ErpSupplierDetails.cs` carries the ERP's extra fields into those methods.
`Domain/Suppliers/SupplierAuditActions.cs` holds every audit action the automatic paths write. Read §6
before touching one.

**Around them:**

- `Api/Endpoints/ErpImportEndpoints.cs`: `POST /api/v1/admin/erp-import/preview` and `/run`, both
  behind `supplier.import.run` (system_admin). They answer 503 when something is not configured, 502
  when the ERP refused or could not be reached, and 409 ("An import or a supplier push is running.")
  when another import or a push holds the lock.
- `Api/Startup/RecurringJobRegistration.cs` registers `ErpSupplierSyncJob` as `erp-supplier-sync`,
  `Cron.Hourly`, while `Jobs:EnableRecurring` is on. The `award-erp-sync` beside it is `AwardErpSyncJob`,
  a different integration. The Operations screen's **Run now** refuses `erp-supplier-sync`
  (`Application/Admin/RecurringJobs.StartedFromTheirOwnScreen`), so every manual run names a person.
- `Infrastructure/Suppliers/AutomaticReinstatement.cs` holds the reinstatement after a document renewal,
  which a run asks again once a mark clears. `AwardCriticalRenewal.cs` holds the renewal check that the
  ERP's approval waits for.
- `Domain/Integration/IntegrationConnection.cs`: `RecordSync` stores the last import's outcome and
  summary, and Connected systems shows them.
- **Frontend**: `src/frontend/src/routes/admin/ErpImportPage.tsx` (tested by `ErpImportPage.test.tsx`),
  the typed client `src/frontend/src/api/erpImport.ts`, and `routes/admin/IntegrationsPage.tsx`
  (Connected systems). The wording is under `erpImport` in `src/frontend/src/i18n/config.ts`, in both
  languages.
- **Tests**: `Tests/Unit/Erp` has a file for each rule and each pure connection class, plus
  `ErpDisabledMemoryTests`, `ErpSupplierDetailsTests`, `ErpSupplierSourceTests` (on bytes the real server
  sent) and `ErpSupplierSourceProbeTests`. `Tests/Integration/Integration` has `ErpImportRunTests`,
  `ErpSupplierSyncTests`, `ErpImportPreviewEndpointTests`, `ErpFieldLimitsTests` and
  `ErpImportAuditTrailBackfillTests`. Each project builds ERP suppliers through its own
  `ErpSupplierTestFactory`, with every field named.

## 3. How a run flows

1. **Start.** There are two ways in. A person presses **Run the import** and confirms, and `/run` calls
   `RunErpImportHandler.HandleAsync(Manual)`. Or, on the hour, `ErpSupplierSyncJob` checks for an
   enabled connection and calls `HandleAsync(Scheduled)`; if no connection is enabled, it skips quietly.
   A manual run is attributed to the person, a scheduled one to "system".
2. **Lock.** The handler takes `ErpImportLock`, without waiting. If another run, or the supplier push
   (§8), holds the lock, it throws `ErpImportBusyException`. For the button that is a 409. The job
   schedules one more try two minutes later (`ErpSupplierSyncJob.RetryAfterBusy`), because a push does
   none of the import's work and a lost hour would hold back that hour's releases and suspensions. If the
   lock is still taken then, the second try steps aside, schedules nothing, and the next hour runs as
   usual.
3. **Audit row and password.** The run's own audit row, `ErpImportRun`, is saved before anything else.
   Then `ErpImport:InitialPassword` must be set and pass the identity rules, or the run stops before
   touching a supplier (a 503 for the button).
4. **Reads.** First the portal's registration numbers. Then the ERP, through
   `ErpSupplierSource.ListSuppliersAsync`: suppliers, then contacts (`ErpContactMerge`), then addresses
   (`ErpAddressMerge`), then the portal's own API user (`frappe.auth.get_logged_user`). A refusal from
   the ERP, or no answer at all, is an `ErpRequestException`, which becomes a 502.
5. **Decisions, before any write.** `ErpRegistrationNumbers.Decide`, then `ErpSyncPlan.Build` over
   `LinkedSuppliersInPortal.ReadAsync`. The plan names the renames to hold, the push's own creates to
   leave to the push, and the suppliers to suspend or mark, and says whether suspensions are held back.
6. **Each ERP supplier** (`ImportOneAsync`). An ERP supplier the portal's API user created that no
   portal supplier carries is refused with a note, because it is the push's to link (§8.5). A probable
   rename is refused with a note. Every other supplier goes through `ErpImportAdmission.Admit`, and then
   one of three things happens:
   - **A new supplier** goes through `CreateAsync`, in one transaction per supplier. It is refused if its
     email already belongs to another account. Otherwise the transaction holds four things:
     `Supplier.ImportFromErp` (approved, and active or suspended by its standing), the address, a
     confirmed account with the initial password and the supplier_admin role, and the audit rows. The
     rows are `supplier.imported_from_erp`, plus a `supplier.suspended_*_in_erp` row if the supplier
     arrives suspended. No email is sent.
   - **A supplier already here** goes through `UpdateAsync`, which has no transaction, in this order:
     1. `ApplySnapshotAndAddress` calls `ApplyErpSnapshot`, and adds the ERP's address only if the
        supplier has none and may be edited.
     2. `ApplyErpStandingAsync` calls `ErpStandingDecision.Decide`, then `RecordErpStanding` unless the
        plan holds the change back, then writes one audit row per change.
     3. `MoveLoginOffPlaceholderAsync` moves the login to a real email once the ERP has one. Its
        `UserManager` call saves the shared context part-way through the update.
     4. `MarkSynced`, unless the plan holds this supplier back while it carries a gone mark.
     5. `ReinstateOnceMarkClearsAsync`, when a mark clears.
     6. One save.
   - **Anything that throws** becomes a **Failed** row. The change tracker is cleared and the next
     supplier goes on.
7. **The plan's suspensions and marks**, checked again under the lock. `SuspendPlannedAsync` writes
   `supplier.suspended_missing_from_erp`, and `MarkRemovedAsync` writes
   `supplier.marked_removed_from_erp`.
8. **Outcome.** `RecordSync` on the ERP connection row stores the time, a one-line summary, and one of
   three outcomes: **Succeeded**; **NeedsAttention** if anything failed, suspensions were held back or a
   rename was held; **Failed** if the run threw. Connected systems shows it as the last import. The
   same save writes the run's closing audit row under the actor `ErpImportRun` named: `ErpImportCompleted`
   with the trigger and the counts in `Changes`, or `ErpImportFailed` with the trigger, the failure
   (`Interrupted`, or the exception's type) and its message. A failure to write it is logged and never
   replaces the run's own exception. Then the lock is released. A run refused for the lock writes no row.

**The preview** takes the same decisions and changes no supplier; it saves one audit row, `ErpImportPreviewed`. **Run the preview** posts to `/preview`.
`PreviewErpImportHandler` saves one `ErpImportPreviewed` audit row, naming the person who asked, then reads
`LinkedSuppliersInPortal.ReadForPreviewAsync` (the same query, with what only the preview reports), the
tax numbers of unlinked suppliers, the registration numbers, and the ERP. It then calls
`ErpImportPreviewBuilder.Build`, which uses the same `ErpSyncPlan`, `ErpImportAdmission`,
`ErpRegistrationNumbers` and `ErpStandingDecision` as the run. Change a rule there and both sides
change.

Only the run can check three things, at the moment it writes: whether an email is already taken by
another account, whether a login can move, and whether the automatic reinstatement agrees. The preview
takes no lock.

## 4. The state a run keeps on a supplier

| Field and value | What it means | Who writes it |
|---|---|---|
| **LifecycleState** | Whether an approved supplier may be invited to a tender. | |
| `None` | Not approved yet. | The default. The import never leaves a supplier here. |
| `Active` | The supplier may be invited. | A reviewer's approval, a person's reactivation, `AutomaticReinstatement`; `ImportFromErp` when the ERP lets it be used; `RecordErpStanding` when it releases one. |
| `Suspended` | Visible and reversible, but it cannot be invited. | A person; `DocumentExpiryJob`; `ImportFromErp` for a supplier the ERP turns away; `RecordErpStanding`; `SuspendAsRemovedFromErp`. |
| `Deactivated` | Permanent, and reached only from Suspended. | Only a person. |
| **ErpDisabledState** | The sync's memory of the ERP turning the supplier away. | |
| `NotDisabled` | The ERP lets it be used, or has never turned it away, so the next turn-away counts as new. | The default; `ImportFromErp`, `RecordErpStanding`. |
| `MarkedDisabled` | The ERP turned it away while it was already out of service, so the sync only marked it. This holds the automatic reinstatement back. | `RecordErpStanding`. |
| `SuspendedAsDisabled` | The sync suspended it once for being turned away. A person's reinstatement after that stands. | `ImportFromErp`, `RecordErpStanding`, `EndErpPendingHold`. |
| `SuspendedAsPending` | Suspended only while the ERP approves it, and nobody has touched it since. It is the only value the ERP's approval releases, and only once every award-critical document that expired has an approved renewal. | `ImportFromErp`, `RecordErpStanding`. |
| **SyncStatus**, removal values | The sync's memory of the supplier leaving the ERP. | |
| `RemovedFromErp` | The sync suspended it because the ERP stopped returning it. A person's reinstatement after that stands. | `SuspendAsRemovedFromErp`; `MarkSynced` clears it once the ERP returns the supplier. |
| `MarkedRemovedFromErp` | It left the ERP while already out of service, so the sync only marked it. This holds the automatic reinstatement back. | `MarkRemovedFromErp`; cleared by `MarkSynced`, except on a run whose suspensions are held back by the shared limit (missing and turned-away suppliers together, over a quarter) that finds this supplier turned away. |

`SyncStatus` has three other values. `Pending` means no import has found the supplier in the ERP yet:
one never linked, or one the push linked that no import has seen since, which is how the plan tells a
pushed supplier's first sighting (§8.5). `Synced` is written by `MarkSynced`, and nothing writes
`Failed`. `ErpStanding` (`Usable`, `AwaitingApproval`, `Disabled`) and `ErpDisabledChange` are worked
out on each run and never stored.

**The promise: the hourly job never undoes a person's reinstatement.** The sync suspends a supplier at
most once per absence or turn-away, and it remembers that it did (`RemovedFromErp`,
`SuspendedAsDisabled`, `SuspendedAsPending`). The only suspension of its own that it lifts is the
`SuspendedAsPending` hold. When a mark clears it may ask `AutomaticReinstatement`, but that lifts only a
document-expiry suspension. One case looks like an exception and is not. The sync only marks a
supplier that was already out of service. If a person later reactivates it, the next run suspends it
once, because the sync never suspended it for that cause. The promise is held by `ErpSupplierSyncTests`,
for example `A_supplier_a_person_reinstated_is_not_suspended_again_by_the_next_run`.

**Two naming traps.** First, "Disabled" in `SupplierErpDisabledState`, `ErpDisabledChange` and
`ErpDisabledChangeFor` also means "not yet approved in the ERP". Second, `SuspendedAsDisabled` is also
what `EndErpPendingHold` leaves when a person acts on a `SuspendedAsPending` supplier: approving,
suspending, reactivating, deactivating, or pressing **Keep suspended** on the review page. An active
supplier that the ERP never disabled can therefore carry it.

**Why the push has a status of its own.** `SyncStatus` mixes two things: that the ERP has the
supplier, and the sync's memory of an absence. `MarkSynced` writes `Synced` over whatever the column
held. A push that called `MarkSynced`, or wrote `Failed`, for a supplier still missing from the ERP
would wipe `RemovedFromErp`, and the next run would suspend again a supplier a person had reinstated.
So the push keeps `ErpPushStatus` (§8) and never touches `SyncStatus`. The ERP clients share their
wire code, the request with its lowercase `token` header included, in `ErpWire`.

**Both state columns are stored by name in varchar(20)**
(`Infrastructure/Persistence/Configurations/SupplierConfiguration.cs`), and `MarkedRemovedFromErp` is
already 20 characters long. A longer new value compiles and passes the unit tests, then fails when the
job saves it. Widen the column in a migration first. Renaming a value takes a migration that rewrites
the stored rows.

## 5. How to add an ERP field

These steps are for a field on the ERP's Supplier record that the portal already stores. Do them in
order:

1. **`ErpSupplierRecord`** in `ErpSupplierSource.cs`: add a `[property: JsonPropertyName("erp_field")]
   string?` property. The query needs no change, because the supplier read asks for `"*"` and the
   connection test shares that list. A field on Contact or Address works differently: it goes into
   `ContactFields` or `AddressFields` (also shared with the probe), the record type, and the merge.
2. **`Map`**, in the same file: add `NewField: Trimmed(record.NewField)`, by name.
3. **`ErpSupplier`** in `IErpSupplierSource.cs`: add `string? NewField = null` at the end. The two
   `ErpSupplierTestFactory` files only need it if a test sets the field.
4. **`ErpFieldLimits`**: add a constant equal to the column length. Use `Cut` for text that is still
   true when shortened. Use `DropIfTooLong` for codes, numbers and URLs, which become a different value
   when cut. Add a line to `ErpFieldLimitsTests` (Integration), because it lists the fields by hand.
5. **`AdmittedSupplier` and `Admit`** in `ErpImportAdmission.cs`: carry the value by name, measured
   against its limit. Add a note if there is something to say. The preview and the run both show it.
6. **`ErpSupplierDetails.cs`**: add the field. The tax number and phone go in as direct arguments
   instead, but anything new should go through the details.
7. **`Supplier.cs`**: set the field in `ImportFromErp`. In `ApplyErpSnapshot`, assign it only when it
   is not null, and add it to both change-detection tuples (the one taken before and the one compared
   after). A field left out of the tuples is still saved, but `LastSyncedAt` is not stamped. A
   `LegalInfo` field goes through `LegalInfo.Create`/`Matches` instead, and a representative field
   through the `person` tuple.
8. **`RunErpImportHandler.ImportOneAsync`**: pass the admitted value into `new ErpSupplierDetails(...)`.
9. **Tests.** In `ErpSupplierSourceTests`, check that the JSON reaches the record. In
   `ErpImportAdmissionTests`, check the limit and the note. In `ErpSupplierDetailsTests`, check that a
   value replaces the old one and a null keeps it. Then run the integration suite.

A field the portal does not store yet also needs a `Supplier` property, `SupplierConfiguration`, a
migration (applied by hand locally, see §7), the DTO (`Application/Suppliers/GetSupplierContracts.cs`,
`SupplierDtoMapper.cs`), `src/frontend/src/api/supplier.ts`, a screen, and wording in both languages.

**The product decision the checklist hides.** Following it makes the ERP the master of that field.
Whenever the ERP has a value, it overwrites what the supplier or the ministry typed in the portal, on
every hourly run, and nobody is told. A null from the ERP never blanks a value. Two fields work the other way: the ERP's address is added
only to a supplier that has none, and the ERP's contact name replaces the representative's name only
while that is still the company's name (`ApplyErpSnapshot`). Decide which way a new field should work
before you write it.

**Some values are not measured.** The English name, tax number, email and phone are written without a
length check. One that is too long fails that supplier's row, which is reported as **Failed**, and the
run goes on.

## 6. The edit to avoid without thinking

**Changing what counts as "not approved".** The switch is `ErpImportAdmission.StandingOf`: a workflow
state that is present and is not `Approved` means `AwaitingApproval`.

Make a supplier in some state count as `Usable` instead, and the next run releases every
`SuspendedAsPending` supplier in that state, if its documents allow. That next run is usually the hourly
one, which nobody watches. Each supplier becomes active, with the audit action
`supplier.reactivated_approved_in_erp` and the reason "Approved in the ERP; it had been suspended here
only while it waited for that." That reason is false: the ERP approved nothing, and only the portal's
rule changed. Nothing limits how many are released, because the limit counts suspensions only.

Two places look like the switch but are not. `AdmittedSupplier.Suspended` is read only by tests.
`ErpDisabledChangeFor` changed on its own still lets new imports arrive suspended, because
`ImportFromErp` reads the standing directly.

To make the change safely:

1. Agree with the owner what happens to the suppliers already `SuspendedAsPending` under the old rule.
   They could be released with an audit reason that says the rule changed, kept suspended as a person's
   decision (**Keep suspended** on the review page, one supplier at a time), or left as they are.
2. Do that before the new rule reaches a build that runs the hourly job, because the job runs whatever
   build is deployed.
3. Read the preview on the new build before it imports. Every supplier it would release has a note that
   starts "Approved in the ERP now." That list should be empty, or exactly the agreed one.
4. Update the tests that set up waiting suppliers with a workflow state: `ErpImportAdmissionTests`,
   `ErpSyncPlanTests`, and the approval tests in `ErpSupplierSyncTests`. Then run the integration suite,
   because it is the only suite that runs the release.
5. A new `SupplierErpDisabledState` value must fit in 20 characters (see §4).

**Three smaller traps of the same kind:**

- **Audit action values are read back by code.** `AutomaticReinstatement` lifts a suspension only when
  the latest suspension row says `supplier_auto_suspended`. The `ErpImportAuditTrail` migration reads three
  of them (`ErpImportRun`, `supplier.imported_from_erp`, `supplier.erp_release_withdrawn`), and the audit log's filter matches exact text. A constant's name in
  `SupplierAuditActions` may change; its value may not. The tests keep the literal strings on purpose.
- **"a quarter" is hard-coded as words** in `ErpMissingSupplierPolicy`'s held-back messages. Change the
  constant and the words together. Administrators read `TogetherHeldBack`'s message, not `Decide`'s. The
  constant is a `double`, so write a third as `1.0 / 3`, not `0.33`.
- **`RowScopeGuardTests` reads the handlers' source.** `PreviewErpImportHandler` is on its exemption
  list, and `RunErpImportHandler` passes because it reads the caller's scope to name who ran it. A new
  handler that reads the whole registry needs an exemption there, with a reason.

**Open decisions to leave to the owner:**

- **Suppliers approved before the push existed.** They are never pushed, and nothing in the portal can
  ask for them (§8.1). Whether they should go to the ERP, and how, is the owner's call.
- **https.** The ERP connection still needs to move from http to https.
- **A duplicate check before the push's first create.** Only an attempt that may follow an earlier
  create looks in the ERP for the supplier first (§8.3). A first attempt posts without asking whether
  the ERP already holds a supplier with the same tax number.
- **What counts as "not approved".** See the start of this section.

## 7. Running the tests, and the local stack

From the repository root, with `DOTNET_ROOT=$HOME/.dotnet` and `$HOME/.dotnet` on `PATH`:

```bash
dotnet test src/backend/Tests/Unit
dotnet test src/backend/Tests/Architecture
dotnet test src/backend/Tests/Integration --filter "FullyQualifiedName~.Erp|FullyQualifiedName~SupplierErpPush|FullyQualifiedName~IntegrationConnection"
dotnet test src/backend/Tests/Integration   # the whole suite: about ten minutes
```

- **Run Unit and Architecture after every change, comment rewrites included.** They take seconds, and
  some tests read wording or tokens out of source files.
- **`FullyQualifiedName~Erp` on its own selects every test.** The match ignores case, and every test's
  name starts with `MotsSupplierPortal`, which contains "erP". `.Erp` matches the `Erp` namespaces and
  the classes whose names start with `Erp`. The push's own classes start with `SupplierErpPush`, and
  the switch's route tests are in `IntegrationConnectionTests`, hence the other two terms.
- **Integration needs Docker.** It starts its own Postgres through Testcontainers and never touches your
  local database. It is also the only suite that runs `RunErpImportHandler`, so a green unit run proves
  nothing about the run or the sync.
- **`ErpSupplierSyncTests` resets shared state for you.** Before every test, `InitializeAsync` resets
  the connection row and suspends any imported supplier that other tests left in service.
  `DisposeAsync` resets the row again afterwards. A new test in that class gets both automatically.
  `ErpImportRunTests` deliberately does neither.
- **Build output location.** If you build with `--artifacts-path`, keep that folder under `src/backend`.
  The Architecture tests find the repository from where their binaries sit.

The frontend needs **Node 22**. Node 20 fails with `webidl.util.markAsUncloneable is not a function`.
From `src/frontend`:

```bash
npx vitest run src/routes/admin/ErpImportPage.test.tsx src/routes/admin/IntegrationsPage.test.tsx src/routes/ReviewApplicationPage.lifecycle.test.tsx
npm run typecheck && npm run lint && npm run build
```

**The local stack:**

- **The local database is the real registry.** `mots_supplier_portal` on the development machine holds
  the real Seven Gates suppliers imported from the ERP, not demo data. Never drop or reseed it, and never
  run `RUNBOOK.md` §3's "start over" against it. Back it up before any migration.
- **Migrations are applied by hand.** The API does not run them at startup. The ERP migrations are
  `ErpRegistrationType`, `NightlyErpSync` (the hourly sync; the name is older than the schedule),
  `ErpImportAuditTrail` and `ErpSupplierPush` (§8). Every other environment needs them applied after a
  backup. `ErpSupplierPush` stops, naming them, if two suppliers already share an `ExternalId`.
- **Start the API with the demo seeder off.** In Development the seeder runs at every start unless
  `DevSeed:Enabled` is false, and the demo suppliers, accounts and tenders it writes cannot be fully
  removed, because the audit log is append-only:

  ```bash
  cd src/backend/Api && DOTNET_ROOT=$HOME/.dotnet ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5080" DevSeed__Enabled=false dotnet run --no-launch-profile
  ```

  Then check that the start-up log has no `[dev-seed] demo personas` line.
- **Secrets are not in `appsettings`.** `Erp:ApiSecret` and `ErpImport:InitialPassword` live in
  user-secrets locally. The ERP address and credential are normally saved on Connected systems, and that
  row wins over the settings.

## 8. The push to the ERP

### 8.1 What it does

A supplier that registered in the portal has no `ExternalId`. Once a reviewer approves it, the portal
creates it in the ERP: the Supplier record, its address, its contact, a website user for its
representative, and the links between them. The ERP's name for the new Supplier is saved as the
supplier's `ExternalId`, and from then on the hourly import matches the supplier by it like any other
ERP supplier (§8.5). The push only creates. Nothing it sent is updated afterwards, and a later edit,
suspension or deactivation in the portal is not sent.

The owner decided four things, and the code follows them:

1. **One default ERP supplier group**, stored on the ERP connection, for every supplier the portal
   creates. The portal's categories do not map to the ERP's groups, and the ERP team files the supplier
   properly during their own approval.
2. **The supplier is created however the ERP creates it.** On the real ERP that is Draft. The import's
   existing rule then keeps the portal supplier suspended until the ERP approves it. No import rule was
   changed for the push.
3. **The ERP colleague's six calls, in his order** (§8.3).
4. **A write switch, off by default, in front of every write** (§8.6).

Three more limits are today's rules. Only the primary address and the primary representative are sent,
because those are the colleague's calls. Only Syria has an ERP country name, so a supplier with an
address in any other country is held (§8.7). The first attempt does not ask the ERP whether it already
holds the supplier (§6).

**Only a supplier in service is pushed** (§8.3, step 3). One suspended by a person or by the
document-expiry job, or deactivated, is not created in the ERP, because it would get a website user with
the Supplier role there. Its push waits where it is and goes on once the supplier is back in service; a
deactivated supplier never is. The import's own hold while the ERP approves a pushed supplier
(`SuspendedAsPending`) does not stop the push.

**Suppliers approved before the push existed are not pushed.** The `ErpSupplierPush` migration gave every
supplier that already existed `NotRequested`, and only an approval asks for a push. So a supplier that
registered here and was already approved when the migration ran stays `NotRequested`. The sweep never
picks it up, the waiting count on Connected systems leaves it out, and no screen or route can ask for it:
**Retry** works only on a failed push. A later approval would ask, and an approved supplier comes back to
review only through a compliance-critical edit. Whether these suppliers should go to the ERP is an open
decision (§6).

### 8.2 Where things live

Paths are under `src/backend` unless they start with `src/frontend`.

| File | Its one job |
|---|---|
| `Domain/Suppliers/Supplier.cs` | `SupplierErpPushStatus` above the class, with one line per value. The six `ErpPush*` fields. `Approve` asks for the push through the private `RequestErpPush`. `BeginErpPush`, `RecordErpSupplierCreated`, `CompleteErpPush`, `RecordErpPushAttemptFailed`, `FailErpPush` and `RetryErpPush` sit at the end of the class, under a table of the moves each one allows. `IsInServiceForErpPush` is the in-service rule, which `RetryErpPush` checks. |
| `Domain/Integration/IntegrationConnection.cs` | `CreateSuppliersInErp` and `DefaultSupplierGroup`, set together by `SetSupplierCreation`. |
| `Domain/Suppliers/SupplierAuditActions.cs` | The four audit actions the push job writes. |
| `Application/Integration/IErpSupplierRegistrar.cs` | The port: the six calls, the field-list read, and the reads a push makes so that it never creates twice. |
| `Application/Integration/ErpSupplierPayload.cs` | Pure. Builds the request bodies in the ERP's field names, or says why the push is held. Its header is the field map. |
| `Infrastructure/Integration/Erp/ErpSupplierRegistrar.cs` | The HTTP writer. It refuses every write while the switch is off, or to a server not in `Erp:WriteHosts`. |
| `Infrastructure/Integration/Erp/ErpWire.cs` | The wire code the import, the connection test and the writer share: the request with its lowercase `token` header, no answer reported as a 502, and the ERP's `exc_type` and `_server_messages` read out of a refusal. `ApiUserAsync` asks the ERP which user the connection signs in as, for the push's look and the import's hold alike. |
| `Infrastructure/Integration/Erp/ErpFailure.cs` | `ClassifyPush`: the five kinds a failed call of the push falls into (§8.7). |
| `Infrastructure/Integration/Erp/ErpWritesOffException.cs` | What the writer throws while the switch is off, or for a server not in `Erp:WriteHosts`. |
| `Infrastructure/Integration/Erp/SupplierErpPushJob.cs` | The job. `PushAsync` pushes one supplier after its approval, and `RunAsync` is the sweep. `Pushable` is the rule for the suppliers it works on, in a form the database runs, and the waiting count uses it too. Its header holds the rules. |
| `Infrastructure/Integration/Erp/ErpConnection.cs`, `ErpConnectionProvider.cs` | Carry the switch and the group from the connection's row, even when the address comes from the `Erp:*` settings. Neither has a setting. |
| `Infrastructure/Suppliers/ApproveApplicationHandler.cs` | Enqueues `PushAsync` after the approval's commit, when the approval asked for a push. |
| `Infrastructure/Suppliers/RetryErpPushHandler.cs`, `Application/Suppliers/ErpPushRetryContracts.cs` | A person's retry of a failed push. |
| `Infrastructure/Integration/IntegrationHandlers.cs`, `Application/Integration/IntegrationViews.cs` | Saving the switch and the group, the count of suppliers waiting (`IntegrationMapping.WaitingForErp`, which is `SupplierErpPushJob.Pushable`), and the ERP's supplier groups for the screen. |
| `Infrastructure/Integration/Erp/ErpSupplierSource.cs` | `ListSupplierGroupsAsync`, the one read there that is not about a supplier. It also sets `CreatedByPortal` on each supplier the import reads. |
| `Application/Integration/ErpSyncPlan.cs`, `Infrastructure/Integration/Erp/LinkedSuppliersInPortal.cs` | The import's two rules for pushed suppliers (§8.5). `LinkedSuppliersInPortal` says which linked suppliers were pushed (`PushedByPortal`: a push was asked for) and which an import has seen (`SeenByImport`: `SyncStatus` is not `Pending`). |
| `Infrastructure/Integration/Erp/ErpSupplierSyncJob.cs` | The hourly import's second try when a push holds the lock (§3, step 2). |
| `Infrastructure/Persistence/Migrations/20260930213253_ErpSupplierPush.cs` | The push's columns, the switch (off) and the group on the connection, and a unique index on `ExternalId`. |
| `Infrastructure/Persistence/Configurations/SupplierConfiguration.cs` | `ExternalId` at 140 characters, the longest name the ERP gives a record (§8.4). |

**Around them:**

- `Api/Startup/RecurringJobRegistration.cs` registers the sweep as `supplier-erp-push`, on the cron
  `2-59/5 * * * *` (every five minutes from two past the hour), while `Jobs:EnableRecurring` is on. The
  offset keeps the sweep itself off the hour, when the hourly import starts. A push can still hold the
  lock at the hour: one enqueued by an approval a moment before, or a slow sweep that runs past it. The
  import then tries once more two minutes later (§3, step 2). Unlike the import, the Operations
  screen's **Run now** may start the sweep, which pushes only the suppliers that are due.
- `Api/Startup/ApplicationHandlerRegistration.cs` registers the writer as a typed client with a
  30-second timeout (`ErpSupplierRegistrar.RequestTimeout`).
- `Api/Endpoints/ReviewEndpoints.cs`: `POST /api/v1/review/{referenceCode}/retry-erp-push`
  (`RetryErpSupplierPush`), behind `admin.integrations.manage`, which only `system_admin` holds by
  default. It is not behind `integration.retry`: that permission retries an award's send within the
  caller's organisation when they have one, and across the registry only for the platform administrator,
  who has no organisation and also holds `admin.integrations.manage`. A deployment may grant it to an
  organisation's role, while the push is one queue for the whole registry (§8.7).
- `Api/Endpoints/IntegrationEndpoints.cs`: `GET /api/v1/admin/integrations/{key}/supplier-groups`
  (`ListErpSupplierGroups`), and the switch and the group on `PUT /api/v1/admin/integrations/{key}`,
  both behind `admin.integrations.manage`.
- **Frontend**: `src/frontend/src/routes/admin/IntegrationsPage.tsx` (the group and the switch on the
  ERP's card), `src/frontend/src/routes/ReviewApplicationPage.tsx` (the push's chip, its last error and
  **Retry**), and the clients `api/integrations.ts` and `api/review.ts`. The wording is under
  `integrations` and `review` in `src/frontend/src/i18n/config.ts`, in both languages.
- **Tests**: §8.9.

### 8.3 How a push flows, from approval to Created

1. **Approval asks.** `Supplier.Approve` calls `RequestErpPush`, which does nothing for a supplier that
   has an `ExternalId`: one from the ERP, or one an earlier push linked before a compliance edit sent it
   back to review. Otherwise the status becomes `Requested`, the next attempt is now, and the count
   starts at zero, all in the approval's own commit. The request time is set by the first approval only.
   A later one keeps it, because the look in step 7 reaches back from it to an earlier attempt's create.
   `ApproveApplicationHandler` then enqueues `SupplierErpPushJob.PushAsync` for that supplier. Nothing is
   sent to the ERP inside the approval, so the reviewer's answer never waits on it.
2. **The gate.** A run does nothing unless the ERP connection is enabled and `CreateSuppliersInErp` is
   on. With the switch on and no group, it does nothing either and logs a warning. Neither case records
   anything on a supplier, so the requests wait for the switch.
3. **Due work, then the lock.** A supplier is due when it is approved, in service, its push is
   `Requested` or `Linked`, and `ErpPushNextAttemptAt` has passed. In service means active, or suspended
   only by the import's hold while the ERP approves it (`SuspendedAsPending`). A supplier suspended by a
   person or the document-expiry job, or deactivated, is not due, and its push stays where it is
   (`SupplierErpPushJob.Pushable`, `Supplier.IsInServiceForErpPush`). With nothing due, the run ends
   before it touches the lock. Otherwise it tries `ErpImportLock` without waiting. If an import or
   another push holds it, the run steps aside with a log line and the next sweep picks the suppliers up.
   It holds the lock until its last save. `PushAsync` and `RunAsync` each also run one at a time, and a
   second waits up to five minutes for the first.
4. **The ERP's field lists, once per run.** `ReadFieldsAsync` reads the fields of Supplier, Address and
   Contact from `GET /api/method/frappe.desk.form.load.getdoctype?doctype=<type>`, custom fields
   included, with each Select field's options and each field's length. A length of 0 means the ERP's
   default, which for short fields such as Data, Link and Select is 140 characters.
5. **Each due supplier**, oldest request first, at most five a run (`BatchSize`). It is read again
   without tracking, with its representatives and addresses, and checked again for being due.
6. **The payload.** `ErpSupplierPayload.Build` makes the bodies, or holds the push with every reason at
   once. A held push fails straight away, and nothing is sent (§8.7).
7. **Call 1, the Supplier record**, for a supplier with no `ExternalId` yet:
   - When an earlier attempt may have created it, the job looks in the ERP first. That is a supplier
     whose in-flight marker `ErpPushStartedAt` is still set, or whose `ErpPushLastError` holds an
     earlier failure. It looks by tax number, then for a Supplier with this `supplier_name` that the
     portal's own API user created since fifteen minutes before the first request (or the marker, if
     that is earlier). An ERP supplier that another portal supplier already carries is left out. One
     match is linked instead of created. Several fail the push, naming them. When every match the look
     found is carried by another portal supplier, the push fails too, naming each ERP record and the
     portal supplier that carries it. It may be the same company registered twice, or a create of this
     push that became another portal supplier, and only a person can say which; the push never posts
     past it.
   - Otherwise the marker is saved (`BeginErpPush`), and `POST /api/resource/Supplier` is sent. The
     ERP's name, read from `data.name`, is saved as `ExternalId` at once, in a write of its own and
     before any other call (`RecordErpSupplierCreated`, status `Linked`, audit row
     `supplier.erp_push_created`).
   - A create that got no answer, or that the ERP refused as a duplicate, is followed at once by the
     same look (§8.7).
8. **Calls 2 to 6**, each only for what the ERP does not have yet, because a push that stopped part-way
   runs this step again:
   2. `POST /api/resource/Address`, linked to the Supplier, unless one of the Supplier's addresses
      already has the same first line and city.
   3. `POST /api/resource/Contact`, linked the same way, unless one of the Supplier's contacts has the
      representative's email. Without an email to match, any contact the Supplier has counts.
   4. `POST /api/resource/User`, a website user with the Supplier role, no password and no welcome
      email, unless the ERP already has a user with that email. A representative with no deliverable
      email gets no user, so calls 5 and 6 are skipped too.
   5. `GET /api/resource/Supplier/<name>`, then a `PUT` of it with `portal_users` set to the rows it
      had plus `{user: <email>}`. A PUT replaces the whole child table, so every row goes back as it
      came. A user already on the list is not sent again.
   6. `PUT /api/resource/Contact/<contact name>` with `{user: <email>}`, when this attempt made the
      contact before the user, or when a contact found from an earlier attempt does not point at the
      user yet. When the user existed before the contact was made, the ERP links the two itself.
9. **Created.** `CompleteErpPush` sets `Created` and clears the marker, the next attempt and the last
   error. The audit row `supplier.erp_push_completed` says what this attempt made.

**Every write of the push's state is a targeted update** of the push's own columns and `ExternalId`,
made only while the row still holds the status and `ExternalId` this run found, with its audit row in the
same transaction, **and each one moves the supplier's version by one** (`WriteAsync`). It is targeted
because a save through the record could be refused by a reviewer's or the supplier's own save a moment
earlier, and a refused save after the ERP has created the supplier is the gap the push exists to close.
It moves the version so that a save through the record that read the push before this write, such as a
reviewer approving again on a page opened earlier, is refused as stale. Otherwise that save would write
its old copy of the push back, or its new request would be lost without a word. The cost is that
whoever holds the old version, the reviewer who approved a moment ago or the supplier editing its
profile, is told on the next guarded save to read again (a 412).

A person's **Retry** saves through the record, as every person's change to a supplier does, so it
moves the version too, for the same reason: a save that read the push before the retry is refused
rather than writing the old state back over it. The version is not what makes the page show where the
push went. Every `/api/v1/review` answer carries `Cache-Control: no-store`, so the reviewer's view is
read afresh each time, and the page reads it again after a retry.

### 8.4 The state a push keeps

| Field and value | What it means | Who writes it |
|---|---|---|
| **ErpPushStatus** | How far the push has got. | |
| `NotRequested` | Nothing was asked of the ERP. Every supplier from the ERP keeps it. A supplier that registered here keeps it until a reviewer approves it, and one already approved when the `ErpSupplierPush` migration ran keeps it for good unless it is approved again: those are never pushed (§8.1). | The default, and what the migration gave every existing supplier. |
| `Requested` | Approved with no `ExternalId`; the ERP's Supplier record is still to be made. | `Approve`; `RetryErpPush` for a push that failed before the ERP had the supplier. |
| `Linked` | The ERP has the Supplier record and its name is the `ExternalId`, but the address, the contact and the user are not all there yet. | `RecordErpSupplierCreated`; `RetryErpPush` for a push that failed after that. |
| `Created` | Everything the push makes is in the ERP. Nothing moves it on, and it says nothing about the ERP's approval. | `CompleteErpPush`. |
| `Failed` | The push has stopped and waits for a person. | `FailErpPush`. `RetryErpPush` starts it again. |
| **ErpPushRequestedAt** | When approval first asked. A later approval keeps it, so the look for an earlier create (§8.3, step 7) reaches back to the first attempt. Empty for a supplier never asked for. | `RequestErpPush`, from `Approve`, on the first request only. |
| **ErpPushStartedAt** | The in-flight marker. Set while an attempt is under way, and left behind by one that stopped part-way, perhaps after its request reached the ERP. | Set by `BeginErpPush`. Cleared by `CompleteErpPush`, `RecordErpPushAttemptFailed` and `FailErpPush`. |
| **ErpPushAttempts** | The failed attempts since the last request or retry. The eighth fails the push. | One more from `RecordErpPushAttemptFailed` and `FailErpPush`. Back to zero from `RequestErpPush` and `RetryErpPush`. |
| **ErpPushNextAttemptAt** | When the next attempt is due. Empty once the push is `Created` or `Failed`. | Now from `RequestErpPush`, `RecordErpSupplierCreated` and `RetryErpPush`; later from `RecordErpPushAttemptFailed`. |
| **ErpPushLastError** | What the last failure said, cut to 500 characters. While it is set, the next attempt looks in the ERP before it creates. | `RecordErpPushAttemptFailed`, `FailErpPush`. Only `CompleteErpPush` clears it; a retry and a new approval keep it. |
| **ExternalId** | The ERP's name for the supplier. The push sets it once and refuses a different name. It holds 140 characters, the longest name the ERP gives a record: a server that names suppliers by `supplier_name`, as the test server does, can give a 140-character name, and a shorter column would refuse the save after the ERP already had the supplier. | `RecordErpSupplierCreated` (the import for a supplier from the ERP). |

Each push method refuses a status it does not belong to, so a job that runs twice, or a retry pressed
while an attempt is under way, cannot move the push somewhere it should not go. `RetryErpPush` also
refuses a supplier out of service (§8.3, step 3). `ErpPushStatus` is stored by name in `varchar(20)`,
like `SyncStatus` (§4). The push never writes `SyncStatus`, `LastSyncedAt`, `ErpDisabledState` or the
lifecycle; §4 says why.

### 8.5 How it meets the import

- **`ExternalId` is the meeting point.** Once the push has saved it, the import treats the supplier as
  any ERP supplier it holds: `ApplyErpSnapshot` for the fields the ERP sends, the standing rule, and
  `MarkSynced`, which moves `SyncStatus` from `Pending` to `Synced` on the first run that finds it. The
  unique index on `ExternalId` means no two portal suppliers can carry one ERP supplier.
- **The shared lock.** The push holds `ErpImportLock` for its whole run, and the import takes the same
  lock (§3, step 2). An import that ran between the ERP's create and the saved `ExternalId` would find an
  ERP supplier that no portal supplier carries, and create a second portal supplier with an account of
  its own. Within one run the lock rules that out. `SupplierErpPushJobTests` holds the lock both ways.
- **The import leaves the push's own creates to the push.** A create whose answer was lost, or whose
  save failed, is linked only by a later push run, and the lock is free in between. So the import holds
  back any ERP supplier that the portal's own API user owns and that no portal supplier carries
  (`ErpSyncPlan.HeldForPush`). Each import asks the ERP for that user (`frappe.auth.get_logged_user`,
  through `ErpWire.ApiUserAsync`) and compares it with each record's `owner` (`CreatedByPortal`). The
  held supplier is refused with a note, in the preview and the run alike, and it can be neither side of
  a probable rename. The push's next attempt looks, finds it and links it, and from then on the import
  updates it like any other. The rule goes by owner, so it recognises only records made by the user the
  connection signs in as now.
  `An_import_between_two_attempts_leaves_the_push_its_own_create_and_the_push_links_it` holds it.
- **Draft, suspended, released.** On the real ERP a new Supplier starts in `workflow_state` Draft. The
  next import reads that as `AwaitingApproval` (`ErpImportAdmission.StandingOf`). It suspends the
  active portal supplier once, as `SuspendedAsPending`, with `supplier.suspended_not_approved_in_erp`,
  and the review page says the ERP's approval will lift it. The first run after the ERP approves it
  releases it with `supplier.reactivated_approved_in_erp`, if its award-critical documents allow (§4). A
  person who acts on the supplier meanwhile makes the suspension theirs (`EndErpPendingHold`, §4). The
  test server has no workflow, so a supplier pushed there counts as `Usable` and stays active.
  `The_import_holds_a_pushed_supplier_while_the_erp_has_it_in_draft_and_releases_it_once_approved` in
  `SupplierErpPushJobTests` runs this round trip with the real import.
- **A pushed supplier's first sighting in Draft is not counted against the limit.** One run may suspend
  at most a quarter of the active linked suppliers, and never fewer than five, counting the missing and
  the turned-away together (§2, `ErpMissingSupplierPolicy`). Every pushed supplier arrives in Draft, so
  its first sighting says nothing about a change on Seven Gates' side. Counted, a burst of approvals,
  such as the switch turned on over a backlog, would trip the limit, and then none of them would be
  suspended and every other suspension would wait with them, run after run. So the plan leaves a pushed
  supplier that no import has seen yet, and that the ERP is still approving, out of the count and out
  of the hold (`PushedAwaitingApproval`), and suspends it on its own even in a run that holds the others
  back. The first import marks it synced, and from the next run on it counts like any other supplier.
  One the ERP has disabled (`disabled` = 1) on its first sighting still counts. `ErpSyncPlanTests` and
  `ErpImportPreviewBuilderTests` hold it.
- **What comes back.** From the first import on, the ERP's values overwrite the portal's wherever the
  ERP has one, as for any ERP supplier (§5). For a pushed supplier that means, for example, that its
  display names become the legal names it was created with, and its supplier group becomes the default
  group. Its legal type comes back as it was sent: `LegalTypeOf` reads Company, Individual and
  Partnership as themselves, and anything else as Company. `docs/integration/ERP-SUPPLIER-FIELD-MAP.md`
  §8.7 lists what returns.
- **A push that stopped part-way is still a linked supplier to the import.** An import may run between
  two push runs, and may suspend the supplier while the ERP holds it in Draft. The push carries on
  regardless: it reads only its own status and whether the supplier is in service, and the import's hold
  while the ERP approves the supplier counts as in service (§8.3, step 3).
  `A_linked_push_the_import_holds_for_the_erps_approval_still_completes` holds it. A person who acts on
  the supplier meanwhile ends that hold (§4), so a person's suspension takes it out of service, and the
  rest of the push waits until the supplier is back.
- **A linked supplier sent back to review waits.** A compliance edit moves an approved supplier back to
  review, and only approved suppliers are due. The approval that follows leaves a linked push as it is,
  and the sweep carries on from there.

### 8.6 The switch and the default group

**The switch** is `IntegrationConnection.CreateSuppliersInErp`. It is off on the seeded row and on any
new row, and it has no setting in configuration, so a deployment writes to the ERP only after somebody
turns it on. It is checked twice. The job does nothing while it is off, and `ErpSupplierRegistrar`
refuses every create and every PUT with `ErpWritesOffException` before a request exists, whoever calls
it. Reads run either way, which is why the import and **Test connection** work with it off.

**The server must also be listed.** `Erp:WriteHosts` in the deployment's configuration names the ERP
servers the portal may write to, each as a host (`9.160.105.219`) or a host and port
(`9.160.105.219:8001`). It is empty by default, and empty allows none: `ErpSupplierRegistrar` refuses
every write to a server not listed, with the same `ErpWritesOffException`, whatever the switch says.
The switch is one click on a screen; the list is configuration a screen cannot change, so pointing the
connection at the wrong ERP with the switch on still creates nothing there. On the owner's machine
only the test server is listed until the owner says otherwise.

**The group** is `IntegrationConnection.DefaultSupplierGroup`, at most 140 characters. Every supplier the
push creates is filed under it. `SetSupplierCreation` refuses the switch on without a group. With the
switch off, the group may be set or cleared, and a blank one is stored as none.

**On Connected systems**, the ERP's card has both:

- The group is chosen from a list read from the ERP when the card opens: `Supplier Group` records that
  are not headings (`is_group` = 0), by name. The route answers 503 with no connection enabled, 502 when
  the ERP refuses or does not answer, and 404 for any connection but `erp`. The group already saved
  stays in the list when the ERP cannot answer.
- The switch cannot be ticked until a group is chosen. Ticking it asks first, with the number of
  approved suppliers waiting (`suppliersWaitingForErp`), because all of them go to the ERP within
  minutes. The count is `SupplierErpPushJob.Pushable`, due or not: approved, in service, and
  `Requested` or `Linked`. It leaves out a supplier out of service, which goes once it is back in
  service, and a supplier approved before the push existed, which never goes (§8.1). The list is read
  again as the question opens. **Turn on and save** saves the whole card at once. Turning it off, and
  changing the group, are saved with **Save**.
- **Save** sends the switch only to turn off a switch the server has on, and the group only when it
  differs from the server's. The save carries no version, so a card left open while another
  administrator turned the writes off would otherwise put its old On back with an unrelated change,
  without the question. Whenever the list is read again and the server's switch or group has moved,
  the card shows the new value and drops an unsaved change to it. The question is the only way to turn
  the writes on.
- `PUT /api/v1/admin/integrations/{key}` carries `createSuppliersInErp` and `defaultSupplierGroup`. A
  missing one leaves the stored value alone, so a caller that sends neither cannot turn writes off by
  saving an address. A blank group clears it. A refusal from `SetSupplierCreation` answers 422
  (`integration_settings_refused`) with its sentence, and nothing is saved, the address included. A
  change to either writes `IntegrationSupplierCreationChanged`, with the person as the actor, `Off` or
  `On` as the states, and the group as the reason. Every save also writes `IntegrationConnectionUpdated`,
  and every **Test connection** writes `IntegrationConnectionTested`, both with the person as the actor;
  the test's row has `Succeeded` or `Failed` as its state and the test's detail as its reason.

**The group does not follow the address.** It is the name of a group on the ERP it was chosen from.
Pointed at another ERP, the connection keeps it, and every create fails on a group that ERP may not have.
Turn the switch off before you change the address, and choose a group from the new ERP before you turn
it on again.

### 8.7 Failures and retry

`ErpFailure.ClassifyPush` sorts every failed call of the push, reads included, by its method, its status
and the ERP's `exc_type`:

| Kind | What it is | What the job does |
|---|---|---|
| `Permission` | 401 or 403. | Stops the run. Nothing is recorded on any supplier, because every due supplier would fail the same way. An error is logged, starting "Supplier push to the ERP stopped: the ERP refused the portal's credential". The next run carries on once the rights are fixed. A marker saved before a refused POST stays, so the next attempt looks before it posts. |
| `AlreadyExists` | A create that answered 409, or `DuplicateEntryError` or `UniqueValidationError` under any status. Frappe answers a clash on a unique field with 417. | For the Supplier record: looks, as in §8.3. One found is linked. None found, several, or only ones another portal supplier carries fail the push, for a person to decide which ERP supplier this is. For the other records: a failed attempt, and the next attempt looks before it creates. |
| `Permanent` | 404, or 417 otherwise: a record type or record that is not there, a mandatory field, a link to a value the ERP does not hold. | Fails the push with the ERP's own sentence from `_server_messages`. |
| `OutcomeUnknown` | A create with no answer, a 408, or any 5xx. A 502 is also what no answer at all is reported as, and so is a 200 without a name. | For the Supplier record: looks at once. One found is linked. Several, or only ones another portal supplier carries, fail the push. None found is a failed attempt, and the next attempt looks again before it creates. For the other records: a failed attempt. |
| `Transient` | 429, and everything else, including a 5xx or no answer on a read or a PUT. | A failed attempt. |

**Other ways an attempt ends:**

- **A held payload** fails the push at once, naming every reason: no default group, no address, a
  country other than Syria, no representative, a value longer than the ERP's field holds, or a value
  its Select field does not offer. Nothing is sent. A website or job title that is too long is left out
  with a note instead.
- **Field lists that cannot be read** count one failed attempt for every due supplier in the batch,
  unless the reason is `Permission`.
- **Anything else that throws** counts as a failed attempt ("The push stopped part-way: ..."), and the
  run goes on to the next supplier.
- **The switch turned off, or the connection disabled, during a run** stops the run quietly, with
  nothing recorded.
- **A supplier out of service** is not due, so no attempt is made and nothing is recorded. Its push
  keeps its status and its chip until the supplier is back in service.

**The waits.** After the first failed attempt the next waits one minute, after the second five, after the
third fifteen, and after each one from the fourth an hour (`WaitAfter`). The sweep serves each wait, so
in practice the next attempt comes at the first sweep after it ends. The eighth failure in a row fails
the push ("Stopped after 8 failed attempts. The last: ..."), about four and a half hours after the first.
The scheduler never retries a run (`AutomaticRetry(Attempts = 0)`), because the job keeps its own count.

**A failed push waits for a person.** The job never picks up a `Failed` push by itself. **Retry** on the
review page posts to `/api/v1/review/{referenceCode}/retry-erp-push`, behind `admin.integrations.manage`,
the permission of Connected systems, where the push is switched on. It is not a reviewer's decision, and
not `integration.retry`, which retries an award's send within the caller's organisation when they have one
(across the registry only for the platform administrator, who also holds `admin.integrations.manage`) and
may be granted to an organisation's role, while the push serves the whole registry. `RetryErpPushHandler`
finds the supplier by its code across the registry, not through an organisation. The retry moves the
push back to `Requested`, or to `Linked` when there is an `ExternalId`, restarts the count, and makes the
next attempt due now. It saves through the record, which moves the version (§8.3). It writes
`supplier.erp_push_retried` with the person as the actor and what the push had failed on, and it
enqueues `PushAsync`. A push that has not failed, or a supplier out of service, answers 409 with the
domain's sentence. The last error stays, so the retried attempt looks in the ERP before it creates.

**The trail.** The job writes `supplier.erp_push_created` (the ERP holds the Supplier record and the
portal its name, whether this attempt created it or found it), `supplier.erp_push_completed`,
`supplier.erp_push_attempt_failed` ("Attempt n of 8; the next is due at ... UTC") and
`supplier.erp_push_failed`. Each has "system" as the actor and the push's status before and after as its
states. The marker saved before the POST has no row. When the attempt created the Supplier record, the
`supplier.erp_push_created` reason also carries the payload's notes, such as a value left out because
the ERP has no field for it.

**What a person sees.** The review page shows a chip beside the states: **ERP: waiting** (`Requested`),
**ERP: partly created** (`Linked`), **ERP: created** with the ERP's name, or **ERP: failed**. There is
no chip for `NotRequested`. A failed push also shows its last error, and **Retry** to anyone holding
`admin.integrations.manage`. A push waiting after a passing failure shows only its chip, and the audit
trail says why. A push on a supplier out of service also shows only its chip, beside the supplier's
suspended or deactivated state. The gate, the lock and a refused credential show only in the API log,
in lines that start "Supplier push to the ERP".

### 8.8 How to add a field to the push

1. **Name it as the ERP names it.** For a field the import also reads, use the import's name. The
   custom fields are constants on `ErpSupplierPayload` (`ArabicNameField`, `RegistrationNumberField`,
   `RegistrationTypeField`), and `ErpSupplierRecord` in `ErpSupplierSource.cs` reads through the same
   constants. A new custom field gets a constant there too, used on both sides.
2. **Put it in the body**, in `ErpSupplierPayload.Build`: `supplierBody.Put("field", value)`, or
   `addressBody` or `contactBody`. `Put` handles the rest. It leaves out a blank value. It leaves out,
   with a note, a field this ERP does not have. It holds the push when the value is longer than the ERP's
   field or is not one of a Select field's options. Pass `dropWhenTooLong: true` only for a value that
   is not who the supplier is, as `website` and `designation` do, to leave it out with a note instead.
   Numbers and child tables have their own overloads. Never cut a value.
3. **The User body is not measured.** No field list is read for User, so anything added to it is sent
   whether the ERP has the field or not.
4. **Load what it needs.** `SupplierErpPushJob.LoadAsync` includes only the representatives and the
   addresses. A value from any other collection needs its `Include` there.
5. **Write it down** in the "WHAT GOES WHERE" block at the top of `ErpSupplierPayload.cs`, and in
   `docs/integration/ERP-SUPPLIER-FIELD-MAP.md` §8.
6. **Tests.** In `ErpSupplierPayloadTests` (Unit), check that the value lands in the body under the
   ERP's name, and that it is left out with a note when the ERP's field list lacks the field. Add the
   length and Select cases where they apply. Each test must fail when the `Put` line is removed. The
   writer sends bodies as they are, so its tests need no change.
7. **Decide the way back.** The push only creates, so a later change in the portal never reaches the
   ERP. If the import reads the field back, the ERP's value overwrites the portal's every hour from then
   on (§5's product decision). If it does not, the ERP holds the value from the day it was created, and
   the portal never sees the ERP's changes to it.

### 8.9 How to test it safely

**The automated tests call no real ERP.** Run them with the commands in §7.

| Suite | Test file | What it holds |
|---|---|---|
| Unit | `Tests/Unit/Domain/SupplierErpPushTests.cs` | Each push method, and its refusal from the wrong status; the first request's time kept through a new approval; a retry refused for a supplier out of service. |
| Unit | `Tests/Unit/Domain/IntegrationConnectionSupplierCreationTests.cs` | The switch needs a group; the group's length. |
| Unit | `Tests/Unit/Erp/ErpSupplierPayloadTests.cs` | One test per mapped field and per omission, and the reasons a push is held. |
| Unit | `Tests/Unit/Erp/ErpSupplierRegistrarTests.cs` | Through a `RoutedHandler`: each call's method, path, lowercase `token` header and exact body, the field list, the searches, nothing written while the switch is off, and how each failure is sorted. |
| Unit | `Tests/Unit/Erp/ErpPushFailureTests.cs` | `ClassifyPush` over methods, statuses and `exc_type`. |
| Unit | `Tests/Unit/Erp/ErpSyncPlanTests.cs`, `ErpImportPreviewBuilderTests.cs`, `ErpSupplierSourceTests.cs` | The import's side: the push's own creates held for the push, a pushed supplier's first sighting kept out of the limit, and `CreatedByPortal` read from the record's owner. |
| Integration | `Tests/Integration/Integration/SupplierErpPushJobTests.cs` | The job against a real database and `FakeErp`, a fake of the port that remembers what it was sent: the colleague's order, never a second create, resuming part-way, the lock both ways, the waits, and the round trip with the real import. Also: an import between two attempts, a record another portal supplier carries, the look reaching back past a second approval, suppliers out of service, the version moved by every write, a 140-character ERP name, and a partnership kept. |
| Integration | `SupplierErpPushRetryTests.cs`, `IntegrationConnectionTests.cs`, `ErpSupplierRegistrarRegistrationTests.cs` | The retry route, refused for a supplier out of service; the switch, the group, the waiting count and the group list on the connection's routes; the writer's 30-second client, and the switch and group carried from the row. |
| Integration | `ErpImportRunTests.cs`, `ErpSupplierSyncTests.cs`, `ErpFieldLimitsTests.cs` | The import holding the push's own create; a partnership arriving as one; the hourly job's second try when the lock is taken; `ExternalId` at 140 characters. |
| Frontend | `routes/admin/IntegrationsPage.test.tsx`, `routes/ReviewApplicationPage.lifecycle.test.tsx`, `routes/admin/ErpImportPage.test.tsx` | The group and the switch on the card, and what **Save** sends; the chip, the last error and **Retry**; the busy card that names a push. |

**Then the ERP's test server, on a copy of the registry, with the hourly import off.** Two things make
the test server dangerous to the registry:

- **The hourly import reads whatever ERP the connection points at.** Pointed at the test server, it
  would create the test server's suppliers in the registry, approved and each with an account, and judge
  every real supplier missing.
- **A supplier approved while the connection points at the test server keeps the test server's name as
  its `ExternalId`.** The push never replaces it, and no screen can change it. Back on the real ERP, the
  import finds that supplier missing.

So never point the registry's own connection at the test server. Do the test on a copy:

1. **Make the copy** with the backup scripts (`RUNBOOK.md` §3b), into a database of its own. Check both
   `DATABASE_URL`s before you press Enter: `restore.sh` replaces whatever is in the database it names.

   ```bash
   docker compose exec -T postgres psql -U postgres -c "CREATE DATABASE mots_supplier_portal_pushtest;"
   DATABASE_URL='postgresql://postgres:postgres@localhost:5432/mots_supplier_portal' BACKUP_DESTINATION=./push-test-backup ops/backup/backup.sh
   DATABASE_URL='postgresql://postgres:postgres@localhost:5432/mots_supplier_portal_pushtest' RESTORE_CONFIRM=yes ops/backup/restore.sh ./push-test-backup/<the run directory>
   ```

2. **Stop the API that serves the registry, and start one on the copy** with every schedule off.
   `Jobs:EnableRecurring=false` removes the hourly import and the push's sweep alike. The push still runs
   straight after an approval and after a **Retry**, because the scheduler still runs enqueued work. A
   push waiting after a passing failure then waits until the schedules are back, so read the chip and
   the trail after each approval.

   ```bash
   cd src/backend/Api && DOTNET_ROOT=$HOME/.dotnet ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5080" DevSeed__Enabled=false Jobs__EnableRecurring=false ConnectionStrings__Default="Host=localhost;Port=5432;Database=mots_supplier_portal_pushtest;Username=postgres;Password=postgres" dotnet run --no-launch-profile
   ```

   Check that the start-up log says `Recurring jobs DISABLED` before you go on.
3. **Point the copy at the test server.** On the copy's Connected systems, enter the test server's
   address and a key and secret for it (the ERP colleague has both), press **Save**, then **Test
   connection**. Choose one of the test server's supplier groups and tick **Create approved suppliers
   in the ERP**. The question says how many approved suppliers are waiting, and each of them goes to
   the test server too, so read the number before you press **Turn on and save**.
4. **Approve a supplier.** Register a test supplier on the copy, with a representative email you can
   read in MailHog, and take it through review to approval. The system administrator holds the
   reviewer's permissions, so no demo account is needed.
5. **Read the result.** Within seconds its review page should say **ERP: created** with the test
   server's name for it. The trail should hold `supplier.erp_push_created`, which lists any value the
   test server had no field for (it has none of the custom fields), and `supplier.erp_push_completed`,
   which lists the address, contact and website user made. Check them on the test server with the ERP
   colleague, including the Supplier's portal users.
6. **Clean up.** Stop the API on the copy, drop `mots_supplier_portal_pushtest` (the copy, never
   `mots_supplier_portal`), delete `./push-test-backup`, and start the registry's API as §7 says. What
   the test created on the test server stays there; removing it is the ERP team's work.

**Then the real ERP**, on the registry itself: `RUNBOOK.md` §11.

### 8.10 Edits to avoid without thinking

- **Never let the Supplier record be posted twice.** The ERP makes a second supplier for a second
  request, and only the ERP team can remove one. What prevents it is the marker saved before the POST,
  the name saved straight after it, the last error kept through a retry and a new approval, the first
  request's time kept through a new approval, the look in `FindEarlierCreateAsync`, a match another
  portal supplier carries never posted past, the lock, and the import's hold on the push's own creates.
  Clearing `ErpPushLastError` on a retry, moving `ErpPushRequestedAt` on a new approval, saving the
  marker after the POST instead of before, or dropping `HeldForPush` from the import, reopens the gap.
- **Keep the switch in the writer.** The job's gate alone would let any new caller of the port write
  with the switch off.
- **Keep the push's writes targeted, and keep them moving the version** (`WriteAsync`). A save through
  the tracked record could be refused by a save a moment earlier, after the ERP already had the
  supplier. A targeted update that left the version alone would let a save that read the push earlier
  write its old copy back, or lose a new approval's request without a word.
  `Every_write_the_push_makes_moves_the_version_so_a_save_that_read_it_before_is_refused` holds it.
- **Keep the in-service rule in step.** `SupplierErpPushJob.Pushable` decides what the sweep pushes and
  what the waiting count counts, and `Supplier.IsInServiceForErpPush` is the same rule for
  `RetryErpPush`. Change both together, or the count an administrator confirms is not what the sweep
  pushes.
- **Keep `ExternalId` at 140 characters or more.** A narrower column refuses the link after the ERP has
  created the supplier, and every attempt after that finds the record and fails on the same save.
- **Never write `SyncStatus` from the push** (§4).
- **Stored names and values stay.** `ErpPushStatus` is stored by name in `varchar(20)`. The four push
  audit actions and `supplier.erp_push_retried` are stored and filtered on by their exact text.
- **The ERP's clock matters here too.** The look for a Supplier the portal created filters on the ERP's
  `creation`, which the ERP stores in its local time, so it is written in `Erp:ServerTimeZone`. A wrong
  zone moves the window. The fifteen minutes of `LookBack` allow for the two clocks differing, not for a
  wrong zone.
- **`RowScopeGuardTests` reads these handlers too.** `RetryErpPushHandler` reads the supplier across the
  registry and passes because it reads the caller's scope, to name the actor. `ListIntegrationsHandler`
  is on the exemption list, with its reason, because it counts the suppliers waiting across the registry.
