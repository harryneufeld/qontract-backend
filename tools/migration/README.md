# Local migration dry run

`dry_run.py` consumes the FamilyPlanner local export v1 and produces a private JSON
validation/mapping report. It never opens a target database, assigns target IDs,
imports application records, changes permissions or sends credential-setup email.

```powershell
python tools/migration/dry_run.py PRIVATE_EXPORT_DIRECTORY --report NEW_PRIVATE_REPORT.json
python tools/migration/dry_run.py PRIVATE_EXPORT_DIRECTORY --report ANOTHER_PRIVATE_REPORT.json --stage-db PRIVATE_STAGE.sqlite
python -m unittest discover -s tools/migration -p 'test_*.py'
```

The source format, local exporter and future protected API proposal are in
FamilyPlanner's `tools/migration/EXPORT-FORMAT.md`. Store packages/reports outside
Git: they contain private source identities, relationships and mapping candidates.
Only counts and finding categories belong in PRs/issues. A report path must be new.

Optional staging retains all raw records and the review report in a separate SQLite
evidence store, keyed by export and stable source relationship identity. Replaying
the same export verifies stored evidence and inserts zero records; conflicting
export identity, changed report policy or altered stage data is rejected. Distinct
export versions are retained separately, without overwriting earlier evidence.
Existing databases containing unrelated tables are refused. This is source staging,
not approved target-ID mappings or application import. Protect/back up the package
and stage together: binaries stay in the checksum-verified package, not SQLite.

Validation checks manifest version/completion, exact table and row-field allowlists,
entity/binary hashes and sizes, UUID identities, duplicate rows/links, parent
references, contract/contractor scopes, target length/required-field limits,
normalized email collisions and document scope candidates. Unused/reused addresses,
tenant administrators and missing uploader provenance require review. An unknown
amount is preserved as null and is informational, not an invented zero.

Source keys are `(source instance, source entity, source GUID)`. Address target
candidate keys instead identify `(source instance, parent entity, parent GUID,
AddressId relationship)`; this preserves the possibility of multiple target rows
for one reused source address. Those are candidate keys, not an implemented durable
import ledger. Raw source fields, audit facts and associations remain in the package.

The initial real MariaDB fixture exported all 8 in-scope tables and 3 document blobs.
Repeated unreviewed dry-run reports were identical. Initial findings: 2 blank/missing user display
names block target compatibility; 1 tenant-admin assignment and 3 missing uploader
provenance decisions require review, plus confirmation of mapping policies. The
single null amount is preserved. No additional manual test data is needed.

The real fixture staged 20 raw entity/relationship records. Replaying added zero
records. All application target-write counts remain zero.

Before production importer writes, settle source datetime timezone, unknown currency,
legacy active/status defaults and reused-address expansion. The choices for this
local fixture's names/admin/uploaders are recorded below. The source remains
unchanged. A future staged importer must compare actual target state, retain raw
source/audit records, record reviewed transformations, persist unique mapping keys,
reject conflicting reruns/target edits and reconcile every entity/link/binary.
Deterministic dry-run output is not proof that imports are idempotent.

Also pending: a protected authenticated export API, operator configuration/trigger,
production target import, imported API/app privacy checks, SMTP staging and
backup restore/cutover rehearsal. This tooling does not complete FamilyPlanner #2
or backend #63 and does not authorize a production import.

## Reviewed decisions and isolated target rehearsal

`resolve_decisions.py` accepts a private decision file bound to the source instance.
Harry approved email fallback for blank user names, selected his existing account
as tenant admin and approved inferred uploaders from the unique linked contract
UserId, falling back to that tenant admin for standalone tenant documents. The
resolver requires the admin/uploader to be a tenant member, preserves document
scope and rejects conflicting linked users. Raw uploader facts remain unchanged;
inference and fallback reasons are recorded separately. No additional test data
is needed. Do not put the private administrator email or source IDs in Git.

```powershell
python tools/migration/resolve_decisions.py PRIVATE_EXPORT --decisions PRIVATE_DECISIONS.json --plan NEW_PRIVATE_PLAN.json
dotnet run --project tools/migration/LocalImport -- --local-only PRIVATE_PLAN.json NEW_LOCAL_TARGET_DIRECTORY
```

LocalImport supports only a separate file-based SQLite rehearsal directory. It
refuses unrelated existing directories, validates raw records against package
hashes and rechecks inferred uploader/scope/admin/name choices. It applies the real
EF migration chain, then imports in a single database transaction with explicit
source-to-target references and the full original rows in `LocalSourceLedger`.
`LocalImportRun` retains the reviewed plan and reconciliation fingerprint. Target
operational audit times are stamped normally; original source Created/Modified
and unknown timezone remain ledger facts, not invented UTC timestamps.

Accounts require email password setup; random inaccessible credentials are hashed
without being stored elsewhere or displayed. No session, reset token or email is
issued. Admin is a membership role, not a platform-wide grant. Unknown currency
and null amounts stay unknown; contract status uses Active for the local rehearsal
while source IsActive is preserved separately. Current prices/periods are imported
without inventing historical revisions. Tenants use the compatible Family wire
type and source companies map to Company contractors. These are explicit local
defaults, not approval of production mapping policies.

Target document filenames are generated from source GUIDs. All copied bytes are
rehashed; original filename/metadata and access scope remain intact. The primary
contract anchor plus additional DocumentLinks preserve every contract-file link.
Standalone tenant documents keep no contract anchor and remain tenant-shared even
when their fallback uploader is a tenant admin. Address mappings retain separate
parent references; unresolved/reused-address findings still require review before
the resolver produces a local plan.

Replay of the identical plan verifies the application/ledger fingerprint and
target bytes, then writes nothing. Modified plans, edited target records/ledger
or changed binaries are rejected rather than overwritten. Different exports need
separate local targets; this is not a production update/merge policy. Interrupted
database writes roll back; already copied binaries may remain in the private
target and are accepted on retry only if their checksums match. Do not run two
importers or an API writer concurrently against this target. Keep the whole target
directory (database, documents and plan marker) together when backing it up.

The populated fixture imported 2 users, 1 tenant, 3 contractors, 3 contracts,
4 addresses and 3 documents with 2 memberships and 2 contract-file links. Twenty
raw entity/relationship rows are retained in the ledger. Replaying inserted zero.
Source FamilyPlanner is unchanged and running. API/app browsing of the imported
target, password-setup/SMTP and production provider/cutover remain follow-ups.
The existing SQLite dependency warning remains tracked in backend #49.
