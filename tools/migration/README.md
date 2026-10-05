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

The current real MariaDB fixture exports all 8 in-scope tables and 3 document blobs.
Repeated dry-run reports are identical. Findings: 2 blank/missing user display
names block target compatibility; 1 tenant-admin assignment and 3 missing uploader
provenance decisions require review, plus confirmation of mapping policies. The
single null amount is preserved. No additional manual test data is needed.

The real fixture staged 20 raw entity/relationship records. Replaying added zero
records. All application target-write counts remain zero.

Before importer writes, settle display-name fallback, administrator selection,
unknown document uploader handling, source datetime timezone, unknown currency,
legacy active/status defaults and reused-address expansion. The source remains
unchanged. A future staged importer must compare actual target state, retain raw
source/audit records, record reviewed transformations, persist unique mapping keys,
reject conflicting reruns/target edits and reconcile every entity/link/binary.
Deterministic dry-run output is not proof that imports are idempotent.

Also pending: a protected authenticated export API, operator configuration/trigger,
full target dry-run/import, imported API/app privacy checks, SMTP staging and
backup restore/cutover rehearsal. This tooling does not complete FamilyPlanner #2
or backend #63 and does not authorize a production import.
