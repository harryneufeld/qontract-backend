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

## Contract metadata implemented in the refactor

`Contract.Iban`, `Notes`, and nullable `Type` preserve the corresponding legacy
fields. Type values and numeric codes match FamilyPlanner exactly: `None` (0),
`Insurance` (1), `Loan` (2), `Subscription` (3), and `Other` (4). Null means the
classification is unknown; it is distinct from an explicit legacy `None`.
Business type, workflow status, and billing period are independent fields.

`AddContractLegacyMetadata` adds nullable columns without classifying existing
contracts or changing values, active state, or audit timestamps. IBAN and notes
are stored verbatim, including whitespace, line breaks and long historical text;
no bank-format normalization or artificial length limit is applied. These are
stored metadata, not payment processing or bank-account validation.

Create, detail, list, update and deactivate responses expose all three fields.
On update, omitted/null IBAN or notes leaves the existing value; an empty string
clears it. Omitted/null type leaves classification; `clearType: true` resets it
to null and cannot be combined with a non-null type. The app supports editing,
clearing and viewing the fields. Contract notes do not create a price revision
and remain separate from `PriceNotes`. Personal contract scope is implemented below;
source ownership reconciliation, GUID mappings and historical import remain pending.

## Contractor metadata implemented in the refactor

Legacy companies map to contractors, rather than to company-type tenants.
`Contractor.Website`, `Description`, and `Notes` now preserve source text verbatim,
including whitespace, multiline and long descriptions/notes. The nullable columns
added by `AddContractorLegacyMetadata` leave existing contact fields, ownership,
active state and audit timestamps unchanged, with no fabricated metadata.

Create, detail, list, update, deactivate and reactivate return all three fields.
Omitted/null fields on update preserve existing text; empty strings clear it.
The app creates, edits, clears and displays these fields, retaining original text.
Only valid absolute HTTP/HTTPS websites without embedded credentials enable an
external open action; other legacy references remain selectable plain text. The
backend stores website text without URL normalization or fetching its contents.

Personal contractor scope is implemented below. Historical import/provenance still
requires reconciliation: a source `UserId` must not be treated as proof that a
personal company is safe to publish to a tenant. Address associations, name/contact
length compatibility and complete source ownership remain migration gates.

## Personal contracts and contractors (2026-10-04)

Contracts and contractors now support two exclusive application scopes:

| Workspace | Persisted scope | Access |
| --- | --- | --- |
| Personal | `TenantId = null`, `OwnerUserId = authenticated account ID` | Owner only |
| Tenant | Positive `TenantId`, `OwnerUserId = null` | Active members of that tenant |

The API sets scope at creation from the authenticated workspace. Clients cannot
choose an owner, move a record to another scope, or reference a contractor outside
that workspace. Lists, details, changes, activation, deletion, contract counts,
statistics, documents, and contract/contractor addresses enforce this boundary.
Tenant administrators have no override for another account's personal records.
Unresolved records with neither owner nor tenant, and inconsistent records with
both, stay inaccessible. These checks are application access rules, not an importer.

`PersonalRecordOwnership` adds nullable owner FKs and scope indexes, makes
contractor tenancy and the user's legacy primary tenant nullable, and leaves all
existing rows, IDs, audit fields and associations unchanged. It never infers an
owner from `CreatedBy`, `ModifiedBy`, or the existing contractor `UserId`. Existing
tenant contractors retain that separate association and remain tenant-shared.
Accounts without a primary tenant do not receive an invented tenant or membership.
Schema downgrade is refused; restore a matching pre-upgrade database and document
backup for rollback. Deploy the API before the matching Flutter client.

Authentication selects an existing active tenant on login when available, otherwise
Personal. `POST /api/auth/switch-tenant` accepts `tenantId: 0` to select Personal;
zero is a signed workspace marker and is never stored as a tenant FK. It preserves
session expiry and revocation. Personal tokens carry `access_scope: personal`, an
effective `User` role, and can access account and ownership-aware endpoints, but
not tenant administration or the tenant-only invoice API. Removed membership tokens
still fail immediately; a fresh login can reach Personal if no memberships remain.

Uploads on personal contracts create owner-only documents. Re-upload retains
scope, owner and links. Personal documents can link only to that owner's personal
contracts while Personal is selected; they cannot become tenant-shared or invoice
evidence by linking. Existing tenant document behavior and private library access
across workspace switches remain supported. Invoice scope/redesign is deferred (#53).

The app's Account → Workspaces screen always offers Personal, including accounts
with no memberships. Switching clears scoped providers and navigation; forms and
workspace guidance explain visibility before saving. Dashboard costs cover the
selected workspace. Personal contract details omit the tenant invoice action.

Legacy reconciliation remains explicit: `FamilyId` is a tenant association;
`UserId` can coexist with it (contract holder/responsible person, and company
creator association). Neither a populated `UserId` nor audit text alone implies
private scope. Export must retain both source IDs and a source mapping ledger.
Missing owners, conflicting relationships, and private contractors referenced by
shared contracts must be reported for review, without widening visibility or
silently duplicating records. User/tenant addresses, long legacy values, full
ownership reconciliation and the exporter/importer are still prerequisites.

## Cost estimate convention

The dashboard shows calendar estimates, not paid or due invoice totals. Monthly
amounts allocate by days within each calendar month; quarterly and yearly amounts
convert to monthly rates by dividing by 3 and 12. Daily and weekly amounts allocate
per day (weekly divided by 7). One-time amounts occur at their snapshot's effective
date. Contract start/end bound all estimates, with the contract end inclusive;
missing ends do not shorten an ongoing contract to today. UTC calendar dates avoid
daylight-saving drift. The dashboard shows this calendar month, quarter and year
together from the complete tenant list, independent of list filters and search.
A failed complete load shows a retry state rather than totals from a partial page.
The calculation service also supports half-year and next-12-month windows. Only
Active contracts contribute, and currencies remain separate.
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
| User: UserName, Email, PasswordHash, AddressId | Preserve account identity and address. For the two legacy users, use email password setup rather than import BCrypt hashes (decision 2026-10-04). Reset/setup is implemented, disabled by default; SMTP staging and historical provisioning remain pending in #54. Preserve source IDs and resolve duplicate identities explicitly; see PASSWORD-RESET.md. |
| User.SessionId / RefreshToken | Do not import live sessions. Reauthentication and revocable sessions are separate work. |
| UserFamily: FamilyName, AddressId | Family tenant and tenant address; current target tenant address support is incomplete. |
| FamilyUserList | Multiple memberships exist. The join has no explicit per-family admin role; do not infer admin from membership alone. Review ownership/admin assignment. |
| Company: CompanyName, Description, Email, Phone, Website, Notes, AddressId, UserId, FamilyId | Legacy counterparties map to Contractors, not automatically to Company tenants. Verbatim website, description and notes are supported. Personal/family scope, ownership, address associations and actual import/reconciliation remain pending; review existing name/contact length limits against source records. |
| Contract: ContractName, Description, ContractNumber, dates, CompanyId, UserId, FamilyId, Price, Recurring, Iban, Notes, IsActive, Type | Basic names/dates/link, billing, verbatim IBAN/notes and legacy business types exist. Personal scope, ownership and actual import/reconciliation remain pending. Preserve IsActive separately from workflow status; do not guess Terminated from inactive alone. |
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

## Approved production migration exclusions (2026-10-04)

Harry confirmed that production does not use invoices or price history and
explicitly approved skipping `Invoices`, `PriceHistories` and
`ContractPriceHistory`. Their export/import, source mappings, date-boundary and
ownership reconciliation are outside the current migration scope and do not block
readiness. Existing target invoice/history features remain available; this does
not delete any source data or change target runtime behavior. The prior proposals
below are reference material only, superseded for this production migration.

Keep current contract amounts and billed periods, every `DatabaseFiles` record,
document bytes, and in-scope document associations, including documents categorized
as invoices. Retain excluded tables in the full source backup and declare the
exclusions in export manifests/import reports. This records the operator's decision;
it is not evidence of a production database inspection.

## Price-history import proposal and unresolved cases (deferred, outside scope)

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
3. Implement missing in-scope target features, including email password setup,
   personal/tenant scope, shared files and ownership. Agree administrative ownership.
4. Build a repeatable staged importer with durable source mappings, validation,
   dry-run reports, atomic batches, retry/cancellation and reconciliation. Reruns
   must not duplicate records or erase prior history.
5. Reconcile every in-scope source entity and relationship, current amount/period, audit
   fact, and file checksum. Compare representative legacy workflows with the app.
6. Rehearse backup/restore, bounded write freeze or verified delta export, cutover
   and rollback. Productive cutover requires a separate concrete review.

Track billing in backend #47 and the complete migration in FamilyPlanner #1/#2.
No production database was read, exported, imported, or migrated by this work.
