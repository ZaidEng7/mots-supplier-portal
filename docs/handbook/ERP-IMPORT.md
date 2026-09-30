# The ERP supplier import

> For the developer who takes over the ERP supplier integration. This page tells you where the code is,
> what one run does, the state a run keeps on a supplier, how to add a field, which edit needs the
> owner's decision, and how to test. It was checked against the code on 2026-09-30. The comment above
> each class holds that class's full reasoning, and this page shows you which class to read.

---

## 1. What it does

The ministry's ERP (Seven Gates' ERPNext) is the master for the suppliers it holds. The import reads
every supplier from it and then acts on the portal:

- it creates the suppliers the portal lacks, approved without review, each with a supplier_admin account;
- it updates the suppliers the portal already has;
- it suspends or marks the suppliers the ERP no longer returns, has disabled or has not approved;
- it releases a supplier it had suspended only while that supplier waited for the ERP's approval.

An administrator starts it from **Supplier import** (`/back-office/erp-import`), and the same import
also runs every hour on its own. It only reads from the ERP. Nothing writes supplier data back.

The outbound direction is separate code. `AwardErpSyncJob` sends awards as purchase orders through
`IErpPurchaseOrderAdapter`. The only implementation today is `StubErpPurchaseOrderAdapter`, which sends
nothing. The outbox drains to `LoggingOutboxTransport`.

## 2. Where things live

Paths are under `src/backend` unless they start with `src/frontend`.

**The rules** are in `Application/Integration`. They are pure, with no database and no HTTP, and most
have a test file of the same name in `Tests/Unit/Erp`.

| File | Its one job |
|---|---|
| `IErpSupplierSource.cs` | The port, and `ErpSupplier`, which holds what the portal reads of an ERP supplier. |
| `ErpImportAdmission.cs` | Fills every gap so that no supplier is left out, for example with a placeholder email on `erp-import.invalid`. Also works out the ERP's standing (`StandingOf`), words the gap, arrival and standing notes (`ReleasedNote`, `ReleaseWaitsNote`, `HeldBackNote`, `TurnedAwayNote`), and decides the address (`AddressOutcome`). Other notes are worded where they are made: in `RunErpImportHandler`, `ErpRegistrationNumbers`, `ErpAddressMapper`, `ErpFieldLimits` and `ErpImportPreviewBuilder`. |
| `ErpAddressMapping.cs` | `ErpAddressMapper`: the ERP billing address becomes a portal address in one of Syria's fourteen governorates, or a note says why not. |
| `ErpFieldLimits.cs` | Column lengths that ERP values are measured against; `Cut` and `DropIfTooLong`. |
| `ErpRegistrationNumbers.cs` | Which ERP supplier may carry which registration number. The portal allows one supplier per number. |
| `ErpSyncPlan.cs` | Decisions taken before any write: probable renames held for a person, suppliers to suspend or mark as gone, and whether suspensions are held back. |
| `ErpMissingSupplierPolicy.cs` | Whether a read is believed. An empty list never is. One run may suspend at most a quarter of the active linked suppliers, and never fewer than five. |
| `ErpStandingDecision.cs` | The per-supplier decision for a supplier the portal already holds: what the ERP's standing changes, whether the plan holds it back, and the notes. The preview and the run both call `Decide`. |
| `ErpImportPreviewBuilder.cs` | Builds the preview report from those rules. |
| `ErpImportPreview.cs`, `ErpImportRun.cs` | Report shapes, handler interfaces, `ErpImportTrigger`, `ErpImportBusyException`. |

**The connection and the handlers** are in `Infrastructure/Integration/Erp`.

| File | Its one job |
|---|---|
| `ErpSupplierSource.cs` | Makes three HTTP reads: suppliers with fields `["*"]`, then contacts, then addresses, the last two filtered on their link to a supplier. `Map` turns the JSON into `ErpSupplier` using named arguments. |
| `ErpContactMerge.cs`, `ErpAddressMerge.cs` | Pick each supplier's email, phone and contact person, and its one address. |
| `ErpQuery.cs`, `ErpServerTime.cs`, `ErpFailure.cs`, `ErpNotConfiguredException.cs` | URL encoding, timestamps that arrive without a zone, the ERP's refusal (`ErpRequestException`), and "no connection". |
| `ErpConnection.cs`, `ErpConnectionProvider.cs`, `ErpOptions.cs` | Where the address and credential come from. A row saved on **Connected systems** wins over the `Erp:*` settings. |
| `ErpSupplierSourceProbe.cs` | The **Test connection** button: the import's own three reads, one record each. |
| `ErpImportOptions.cs` | `ErpImport:InitialPassword`, which imported accounts are created with. |
| `ErpImportLock.cs` | A Postgres advisory lock, so that only one import runs at a time. |
| `LinkedSuppliersInPortal.cs` | The shared portal reader. It reads the ERP-linked suppliers once, before the first write, for both the preview and the run. |
| `RegistrationNumbersInPortal.cs` | The registration numbers the portal already holds. |
| `PreviewErpImportHandler.cs`, `RunErpImportHandler.cs` | The two handlers. |
| `ErpSupplierSyncJob.cs` | The hourly job. It calls the run handler, so it is not a second copy of the import. |

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
  when the ERP refused or could not be reached, and 409 when an import is already running.
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
2. **Lock.** The handler takes `ErpImportLock`. If another run holds the lock, it throws
   `ErpImportBusyException`: a 409 for the button, a quiet skip for the job.
3. **Audit row and password.** The run's own audit row, `ErpImportRun`, is saved before anything else.
   Then `ErpImport:InitialPassword` must be set and pass the identity rules, or the run stops before
   touching a supplier (a 503 for the button).
4. **Reads.** First the portal's registration numbers. Then the ERP, through
   `ErpSupplierSource.ListSuppliersAsync`: suppliers, then contacts (`ErpContactMerge`), then addresses
   (`ErpAddressMerge`). A refusal from the ERP, or no answer at all, is an `ErpRequestException`, which
   becomes a 502.
5. **Decisions, before any write.** `ErpRegistrationNumbers.Decide`, then `ErpSyncPlan.Build` over
   `LinkedSuppliersInPortal.ReadAsync`. The plan names the renames to hold and the suppliers to suspend or
   mark, and says whether suspensions are held back.
6. **Each ERP supplier** (`ImportOneAsync`). A probable rename is refused with a note. Every other
   supplier goes through `ErpImportAdmission.Admit`, and then one of three things happens:
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
   rename was held; **Failed** if the run threw. Connected systems shows it as the last import. Then the
   lock is released.

**The preview** takes the same decisions and changes no supplier; it saves one audit row, `ErpImportPreviewed`. **Run the preview** posts to `/preview`.
`PreviewErpImportHandler` saves one `ErpImportPreviewed` audit row, then reads
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

`SyncStatus` has three other values: `Pending` means never linked, `Synced` is written by `MarkSynced`,
and nothing writes `Failed`. `ErpStanding` (`Usable`, `AwaitingApproval`, `Disabled`) and
`ErpDisabledChange` are worked out on each run and never stored.

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

**A warning for whoever builds the push to the ERP.** `SyncStatus` mixes two things: that the ERP has
the supplier, and the sync's memory of an absence. `MarkSynced` writes `Synced` over whatever the column
held. A push that called `MarkSynced`, or wrote `Failed`, for a supplier still missing from the ERP
would wipe `RemovedFromErp`, and the next run would suspend again a supplier a person had reinstated.
Give the push its own status column, which takes a migration, before building it. And extract one
shared ERP client first. The HTTP code (`Request` and the lowercase `token` header) is private to
`ErpSupplierSource` and already copied in `ErpSupplierSourceProbe`.

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

- **Partnership.** `LegalTypeOf` in `RunErpImportHandler` maps every type except `Individual` to
  Company, although the ERP has Partnership too.
- **https.** The ERP connection still needs to move from http to https.
- **The supplier push to the ERP.** It needs the `SyncStatus` split from §4 first.
- **What counts as "not approved".** See the start of this section.

## 7. Running the tests, and the local stack

From the repository root, with `DOTNET_ROOT=$HOME/.dotnet` and `$HOME/.dotnet` on `PATH`:

```bash
dotnet test src/backend/Tests/Unit
dotnet test src/backend/Tests/Architecture
dotnet test src/backend/Tests/Integration --filter "FullyQualifiedName~Erp"   # whole suite: about ten minutes
```

- **Run Unit and Architecture after every change, comment rewrites included.** They take seconds, and
  some tests read wording or tokens out of source files.
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
npx vitest run src/routes/admin/ErpImportPage.test.tsx
npm run typecheck && npm run lint && npm run build
```

**The local stack:**

- **The local database is the real registry.** `mots_supplier_portal` on the development machine holds
  the real Seven Gates suppliers imported from the ERP, not demo data. Never drop or reseed it, and never
  run `RUNBOOK.md` §3's "start over" against it. Back it up before any migration.
- **Migrations are applied by hand.** The API does not run them at startup. The ERP migrations are
  `ErpRegistrationType`, `NightlyErpSync` (the hourly sync; the name is older than the schedule) and
  `ErpImportAuditTrail`. Every other environment needs them applied after a backup.
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
