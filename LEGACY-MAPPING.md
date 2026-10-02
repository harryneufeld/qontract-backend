# FamilyPlanner parity and migration mapping

Repository inventory, 2026-10-01. This is a mapping proposal grounded in the
legacy models and `FamilyContext`; it is not a production inventory or a cutover approval.
Productive FamilyPlanner remains supported until all gates below pass.

Legacy `UserFamily` (a family) maps to a refactor tenant, as confirmed by Harry
on 2026-10-02. Family membership maps to tenant membership; family files map to
tenant-shared files. Personal `User.DatabaseFiles` stays account-owned rather than
becoming a separate tenant type or silently becoming tenant-shared.

## Billing implemented in the refactor

`Contract.Value` remains a string for API/data compatibility. With an explicit
`BillingPeriod`, the server validates it using .NET decimal arithmetic and stores
an invariant decimal string. Optional amounts are allowed. The six periods map:

| Legacy RecurringType | Refactor BillingPeriod |
| --- | --- |
| Einmalig | OneTime |
| Täglich | Daily |
| Wöchentlich | Weekly |
| Monatlich | Monthly |
| Quartalsweise | Quarterly |
| Jährlich | Yearly |

`AddBillingPeriodsAndPriceHistory` adds nullable periods and a `ContractPrices`
table. Existing values and audit timestamps remain untouched, with unknown periods;
the migration does not fabricate past prices. New contracts get an initial price
snapshot from a valid start date, or their creation date. The first edit of an older
contract captures its existing price before changing it.

Changes to amount, currency, or period append snapshots; explicitly providing a
price effective date or note also creates a revision even if the amount is unchanged. `PriceEffectiveFrom` is
an ISO date; omitted dates default to the current UTC date, or the current snapshot's
later start date. `PriceNotes` is optional. The previous snapshot closes at this date.
An earlier date than the latest snapshot's start is rejected. Same-day corrections
retain superseded snapshots with zero-length intervals. Unrelated edits add no price
revision. Price history is returned by both contract detail and list APIs, scoped to
the caller's tenant. This is revision creation, not a general historical import API.

New history intervals are start-inclusive/end-exclusive. Future-dated revisions are
supported; the contract's value fields describe the latest revision, while cost
estimates apply each dated snapshot only during its interval. Original recorded
values, currencies, periods, notes, and audit fields remain in the snapshots.

## Cost estimate convention

The dashboard shows calendar estimates, not paid or due invoice totals. Monthly
amounts allocate by days within each calendar month; quarterly and yearly amounts
convert to monthly rates by dividing by 3 and 12. Daily and weekly amounts allocate
per day (weekly divided by 7). One-time amounts occur at their snapshot's effective
date. Contract start/end bound all estimates, with the contract end inclusive;
missing ends do not shorten an ongoing contract to today. UTC calendar dates avoid
daylight-saving drift. The views cover this month, this half-year, this year, or the
next 12 months. Only Active contracts contribute, and currencies remain separate.
Unknown periods and invalid amounts are visibly excluded in whole or part. Zero
amounts add no cost. Real billing dates, payment schedules, and invoices need separate
parity work; the estimates do not claim to reproduce payment timing.

## Source inventory and target gaps

All entities carry GUID `Id`, `Created`, and nullable `Modified`. Preserve these as
source facts and keep durable `(source instance, entity type, GUID)` mappings to new
integer IDs. Refactor write hooks stamp operational timestamps, so the future importer
must explicitly preserve source audit facts without accidentally replacing them.

| Source model/relationship | Required target mapping and remaining work |
| --- | --- |
| User: UserName, Email, PasswordHash, AddressId | Account identity and address. Legacy BCrypt verification is missing; refactor PBKDF2/SHA256 compatibility does not cover it. Duplicate identities need explicit review. |
| User.SessionId / RefreshToken | Do not import live sessions. Reauthentication and revocable sessions are separate work. |
| UserFamily: FamilyName, AddressId | Family tenant and tenant address; current target tenant address support is incomplete. |
| FamilyUserList | Multiple memberships exist. The join has no explicit per-family admin role; do not infer admin from membership alone. Review ownership/admin assignment. |
| Company: CompanyName, Description, Email, Phone, Website, Notes, AddressId, UserId, FamilyId | Legacy counterparties map to Contractors, not automatically to Company tenants. Website, notes, personal/family scope and ownership need complete parity. |
| Contract: ContractName, Description, ContractNumber, dates, CompanyId, UserId, FamilyId, Price, Recurring, Iban, Notes, IsActive, Type | Basic names/dates/link and billing exist. IBAN, notes, type, personal scope and ownership still need target support. Preserve IsActive separately from workflow status; do not guess Terminated from inactive alone. |
| PriceHistory: Price, DateFrom, DateTo, Notes; ContractPriceHistory | Price revisions now exist. Export every raw record and join. Import mapping and reconciliation remain unimplemented. |
| Address: StreetName, HouseNumber, ZipCode, CityName, CountryName; references from users/families/companies | Preserve separate street and house-number source fields and every association. Contractor addresses exist; user/family addresses and complete ownership mapping remain gaps. |
| DatabaseFile: FileName, Description, Date, Path, Category, Content, MimeType, Size | Target supports filename, description, category, document date, MIME type, size, personal and tenant libraries, and versions. Export metadata and actual bytes from both inline and path-backed storage; report missing bytes. Original ownership/provenance and association reconciliation remain import work. |
| ContractFileList, User.DatabaseFiles, UserFamily.Files | Additional contract links share one stored Document; every link carries through re-upload. User files are personal; family files are tenant-shared. Export all original associations. Mixed personal/shared or multiple-tenant associations need explicit access reconciliation; do not drop links or publish personal bytes silently. |
| Invoice: InvoiceDate, DueDate, Amount, InvoiceNumber, Description, FileId | Tenant-scoped invoice records and app forms now exist, with optional contract and exact document-version references. Preserve signed decimal amounts, dates and all text. Currency remains unknown unless supplied explicitly. Import identity/provenance and ambiguous source ownership still need resolution. |

## Invoice and shared-file behavior

Invoices support create/read/edit, paged tenant or contract lists, archive and restore.
Amounts travel as exact invariant decimal strings and support credits. Currency is
optional; FamilyPlanner does not declare it, so no default is inferred. Amounts that
cannot be represented exactly as a .NET decimal are rejected rather than rounded.
Dates are ISO calendar dates. The source model does not enforce due-date ordering;
the target preserves that flexibility. Payment status and invoice totals are separate
follow-ups, not substitutes for the contract cost estimates above.

`Document.ContractId` remains the original storage anchor. Audited `DocumentLinks`
records add other contracts in the same tenant, without copying bytes. Existing
documents keep their paths and primary associations; no legacy data or unused join
table is dropped. New uploads use unique storage keys, preventing same-name files
from overwriting one another. Removing a secondary link retains other links and
bytes. Removing the original anchor is rejected until ownership/storage is redesigned.
Moving a current document to trash hides it from every linked contract; restore
restores those links. Historical versions retain their historical associations.

Re-upload commits the new version, inherited links and supersession pointer in one
database transaction. An invoice keeps its selected version; replacing a file does
not change historical evidence. Active invoices prevent deleting their exact evidence
or removing its selected contract link. Archived invoices also prevent physical purge
of referenced evidence. Trash metadata marks retained invoice files and omits their
purge deadline. Restore the evidence before restoring an archived invoice if the
file itself was moved to trash.

The app exposes invoices from the dashboard menu and contract details, with an
existing-file picker for the selected contract. Standalone invoices can select
tenant-library evidence without a contract. Full historical ownership/provenance,
original-anchor removal, and the staged importer remain gaps. None of this constitutes
a live FamilyPlanner migration or a proof of complete production parity.

Remaining invoice/file parity is tracked in [backend #50](https://github.com/harryneufeld/qontract-backend/issues/50).

## Personal and tenant libraries (2026-10-02)

Documents now have explicit `Scope`, nullable `TenantId`, and explicit nullable
`UserId` using the existing owner column. Contract association is optional. New
authenticated uploads set the owner from the verified user claim; payload owner
and tenant IDs are ignored. Re-upload keeps the original owner's ID, scope,
description/category and tenant, while recording the uploading actor in the new
version's audit fields. Unknown historical owners stay unknown, including when a
different member uploads a replacement. Ownership and creation identity are distinct.

Tenant documents are available to members of the active tenant. Personal documents
are available only to their owner, including across tenant switches; tenant admin
status does not bypass privacy. Access is enforced for metadata, downloads, updates,
versions, trash, restore and links. Private files cannot be linked to shared contracts
or tenant invoices. Changing personal/shared scope is deliberately unavailable;
an explicit sharing/provenance workflow remains future work. Personal invoice scope
and ambiguous legacy invoice ownership still need migration reconciliation.

The **Files** menu offers Tenant files and My files, contract-free uploads, paged
lists, title/date/description/category editing, version replacement/history,
contract links on shared files, and trash/restore. Personal retention uses the global
default; shared files use the tenant setting. Invoice evidence is retained as before.

`AddDocumentLibrariesAndOwnership` preserves existing file paths, original contract
links, owner IDs and audit fields. It backfills tenant ID only from the existing
primary contract. Orphan or unknown associations remain unresolved, and user IDs are
never inferred from email audit text. Existing owner indexes/FKs are updated without
dropping the owner column. Downgrade to mandatory contract ownership is refused;
rollback requires a verified pre-upgrade backup. No production owner counts or source
associations have been inspected; #48 remains open for that audit and staged backfill.

## Price-history import proposal and unresolved cases

Legacy `PriceHistory` is many-to-many with contracts and carries no historical
currency or recurrence. Keep the raw record, GUID, dates, notes, audit facts and
every join in the export and reconciliation manifest. Proposed target expansion is
one revision per contract association, with an import ledger preserving shared
source identity. Do not count shared records as duplicated source records.

The repository declares DateFrom/DateTo but does not establish their inclusivity
through a price-history UI or calculation. Confirm that convention against real
records before transforming it. If DateTo is inclusive, map to the following day
as EffectiveUntil; retain original dates in the ledger. Missing/default dates,
overlaps, gaps, shared rows, and inconsistent current Price must be review findings,
not silently repaired. Current contract recurrence/currency may be proposed for old
prices only with an explicit provenance assumption; history does not prove they were
unchanged. Retain unresolved records in staging until reviewed.

## Export/import gates

1. Produce a complete inventory from a restored production copy: all entity/join
   counts, orphan references, duplicate identities, scope, billing anomalies, and
   actual file counts/sizes/checksums. This repository review cannot supply those counts.
2. Review the authenticated export plan in FamilyPlanner #2. Use a consistent
   snapshot, stable paging, versioned manifests, protected credential handling and
   streamed bytes. Export must not mutate the productive system.
3. Implement all missing target features, including credentials, invoices, scope,
   shared files and ownership. Agree price boundaries and administrative ownership.
4. Build a repeatable staged importer with durable source mappings, validation,
   dry-run reports, atomic batches, retry/cancellation and reconciliation. Reruns
   must not duplicate records or erase prior history.
5. Reconcile every source entity and relationship, amount/period/history, audit
   fact, and file checksum. Compare representative legacy workflows with the app.
6. Rehearse backup/restore, bounded write freeze or verified delta export, cutover
   and rollback. Productive cutover requires a separate concrete review.

Track billing in backend #47 and the complete migration in FamilyPlanner #1/#2.
No production database was read, exported, imported, or migrated by this work.
