# Contract lifecycle and responsibility

`IsActive` describes lifecycle separately from deletion. Inactive contracts remain readable and editable, with documents available. Normal lists include them; `status=inactive` selects them explicitly. Default pagination is alphabetical by title, with ID as a stable tie-breaker.

`ResponsibleUserId` identifies the responsible active member of the contract's tenant. It does not transfer private ownership or grant access. New contracts default to the authenticated member. Updates can assign another current member or explicitly clear responsibility using `clearResponsibleUser: true`. Audit fields are not used to infer responsibility. Personal contracts remain private to their owner.

`scope=mine` selects current-member responsibility (or personal ownership); `scope=tenant` selects all accessible records. Statistics default to `mine`; lists default to `tenant`. Unassigned tenant contracts appear only in the tenant overview.

Deleting sets `DeletedAt`, `DeletedBy` and a fixed `RetentionExpiresAt`, without changing lifecycle. `GET /api/contracts?trash=true` lists recoverable records and `POST /api/contracts/{id}/restore` restores them before expiry. Details and documents belonging only to trashed contracts are hidden until restoration. Shared documents remain accessible through surviving contracts.

Tenant admins can set `contractRetentionDays` in the existing settings JSON (integer 1–3650, default 90). The setting applies to future deletions, never shortens existing deadlines, and does not apply to personal records. The retention worker removes expired contracts and price/link rows. Exclusive documents enter the existing document retention queue; shared documents are reanchored. Any invoice dependency holds the contract and its evidence indefinitely; invoice retention requires separate review.

## Upgrade

Back up the database and document storage before applying `ContractResponsibilityAndTrash`. Older qontract deletes were represented by `IsActive=false`; they receive a fresh 90-day trash deadline. In SQLite imports, source-ledger evidence of legacy inactivity keeps those records visible instead. Explicit legacy `UserId` and active tenant membership backfill responsibility; ambiguous records remain unassigned. The local importer creates this provenance ledger only on SQLite. Other providers preserve old inactive refactor records in trash rather than assume migration provenance.

The upgrade does not change password hashes, account setup flags, source-ledger payloads, or stored amounts. Rollback requires restoring the backup: dropping deletion columns alone cannot recover the distinction between inactive and deleted records.
