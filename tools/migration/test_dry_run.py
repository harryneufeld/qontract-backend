import json
from pathlib import Path
import tempfile
import unittest
import uuid
import sqlite3
from contextlib import closing

import dry_run


class DryRunTests(unittest.TestCase):
    def test_resolution_evidence_must_match_raw_source_and_exported_bytes(self):
        blob = self.root/'proof.bin';blob.write_bytes(b'proof')
        record = self.row('DatabaseFiles', Id=str(uuid.uuid4()), FileName='raw-name.pdf', Path='files', Size=5,
                          Binary={'path':blob.name, 'sha256':dry_run.digest(blob), 'size':5})
        manifest = self.package()
        resolution = {'sourceId':record['Id'], 'sourcePath':'files', 'sourceFileName':record['FileName'],
                      'storagePath':'files/actual.pdf', 'sha256':record['Binary']['sha256'], 'size':5,
                      'approved':True, 'reason':'reviewed_legacy_filename_encoding'}
        evidence = {'format':'familyplanner-reviewed-file-resolutions', 'version':1, 'sourceInstance':'fixture', 'resolutions':[resolution]}
        path=self.root/'file-resolutions.json'
        def save():
            path.write_text(json.dumps(evidence))
            manifest['fileResolutions']={'path':path.name,'sha256':dry_run.digest(path),'count':1}
            (self.root/'manifest.json').write_text(json.dumps(manifest))
        save();dry_run.validate(self.root)
        resolution['sourceFileName']='renamed.pdf';save()
        with self.assertRaisesRegex(ValueError,'disagrees'):dry_run.validate(self.root)
        path.write_text('{}')
        with self.assertRaisesRegex(ValueError,'Changed'):dry_run.validate(self.root)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.rows = {table: [] for table in dry_run.TABLES}

    def row(self, table, **values):
        row = dict.fromkeys(dry_run.FIELDS[table].split())
        row.update(values)
        self.rows[table].append(row)
        return row

    def package(self):
        entries = {}
        for table, rows in self.rows.items():
            path = self.root / f'{table}.json'
            path.write_text(json.dumps(rows), encoding='utf-8')
            entries[table] = {'path': path.name, 'count': len(rows), 'sha256': dry_run.digest(path)}
        manifest = {'format': 'familyplanner-local-export', 'version': 1, 'complete': True,
                    'credentialsIncluded': False, 'excludedTables': list(dry_run.EXCLUDED),
                    'sourceInstance': 'fixture', 'exportId': str(uuid.uuid4()), 'entities': entries}
        (self.root / 'manifest.json').write_text(json.dumps(manifest), encoding='utf-8')
        return manifest

    def test_repeat_is_deterministic_and_unknown_amount_is_preserved(self):
        user = str(uuid.uuid4())
        self.row('Users', Id=user, UserName='Example', Email='example@example.test')
        self.row('Contracts', Id=str(uuid.uuid4()), UserId=user, ContractName='Test',
                 ContractNumber='', Recurring=3, Type=0, IsActive=1, Price=None)
        self.package()
        a, b = dry_run.plan(self.root), dry_run.plan(self.root)
        self.assertEqual(a, b)
        self.assertEqual(a['targetWrites'], 0)
        self.assertIn('unknown_current_amount_preserved', {f['code'] for f in a['findings']})
        self.assertEqual(a['summary']['blockers'], 0)
        self.assertFalse(a['summary']['readyForImport'])

    def test_scoped_exclusion_evidence_is_hashed_and_counts_reconcile(self):
        manifest = self.package()
        evidence = {'policy': 'first_test_member_with_owned_contract_and_email_v1', 'productionApproved': False,
                    'originalCounts': {t: 0 for t in dry_run.TABLES}, 'excludedRecords': []}
        path = self.root / 'scope-exclusions.json'
        path.write_text(json.dumps(evidence))
        manifest['selection'] = {'policy': evidence['policy'], 'path': path.name, 'sha256': dry_run.digest(path), 'count': 0}
        (self.root / 'manifest.json').write_text(json.dumps(manifest))
        dry_run.validate(self.root)
        evidence['originalCounts']['Users'] = 1
        path.write_text(json.dumps(evidence))
        with self.assertRaisesRegex(ValueError, 'exclusion evidence'):
            dry_run.validate(self.root)
        manifest['selection']['sha256'] = dry_run.digest(path)
        (self.root / 'manifest.json').write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, 'do not reconcile'):
            dry_run.validate(self.root)

    def test_staging_replay_is_idempotent_and_preserves_raw_fields(self):
        self.row('Users', Id=str(uuid.uuid4()), UserName=None, Email=' test@example.test ')
        self.package()
        database = self.root / 'stage.sqlite'
        report = dry_run.plan(self.root)
        self.assertEqual(dry_run.stage(self.root, database, report)['newStagedRecords'], 1)
        replay = dry_run.stage(self.root, database, report)
        self.assertTrue(replay['replay'])
        self.assertEqual(replay['newStagedRecords'], 0)
        with closing(sqlite3.connect(database)) as db, db:
            row = db.execute('SELECT RawJson FROM StagedRecords').fetchone()
        self.assertEqual(json.loads(row[0]), self.rows['Users'][0])
        changed = dict(report, targetWrites=1)
        with self.assertRaises(ValueError):
            dry_run.stage(self.root, database, changed)

    def test_staging_detects_altered_evidence_and_retains_distinct_export_versions(self):
        self.row('Users', Id=str(uuid.uuid4()), UserName='Example', Email='test@example.test')
        self.package()
        database = self.root / 'stage.sqlite'
        dry_run.stage(self.root, database, dry_run.plan(self.root))
        self.rows['Users'][0]['UserName'] = 'Changed'
        self.package()  # A new export identity retains a separate raw version.
        dry_run.stage(self.root, database, dry_run.plan(self.root))
        with closing(sqlite3.connect(database)) as db, db:
            self.assertEqual(db.execute('SELECT COUNT(*) FROM StagedRecords').fetchone()[0], 2)
            db.execute("UPDATE StagedRecords SET RawJson = '{}' ")
        with self.assertRaises(ValueError):
            dry_run.stage(self.root, database, dry_run.plan(self.root))

    def test_existing_application_database_is_refused(self):
        self.package()
        database = self.root / 'application.sqlite'
        with closing(sqlite3.connect(database)) as db, db:
            db.execute('CREATE TABLE Users (Id INTEGER PRIMARY KEY)')
        with self.assertRaisesRegex(ValueError, 'non-staging'):
            dry_run.stage(self.root, database, dry_run.plan(self.root))
        with closing(sqlite3.connect(database)) as db:
            tables = {r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        self.assertEqual(tables, {'Users'})

    def test_tampered_count_checksum_and_incomplete_manifest_fail(self):
        manifest = self.package()
        (self.root / 'Users.json').write_text('[{}]', encoding='utf-8')
        with self.assertRaises(ValueError):
            dry_run.plan(self.root)
        manifest = self.package()
        manifest['entities']['Users']['count'] = 1
        (self.root / 'manifest.json').write_text(json.dumps(manifest), encoding='utf-8')
        with self.assertRaises(ValueError):
            dry_run.plan(self.root)
        manifest['complete'] = False
        (self.root / 'manifest.json').write_text(json.dumps(manifest), encoding='utf-8')
        with self.assertRaises(ValueError):
            dry_run.plan(self.root)

    def test_credentials_duplicate_ids_and_missing_fields_fail(self):
        user = str(uuid.uuid4())
        r = self.row('Users', Id=user, UserName='Test', Email='test@example.test')
        r['PasswordHash'] = 'never exported'
        self.package()
        with self.assertRaises(ValueError):
            dry_run.plan(self.root)
        r.pop('PasswordHash')
        self.rows['Users'].append(dict(r))
        self.package()
        with self.assertRaises(ValueError):
            dry_run.plan(self.root)
        self.rows['Users'].pop()
        r.pop('AddressId')
        self.package()
        with self.assertRaises(ValueError):
            dry_run.plan(self.root)

    def test_missing_relationship_and_long_contact_are_blockers(self):
        self.row('Companies', Id=str(uuid.uuid4()), CompanyName='x' * 101,
                 UserId=str(uuid.uuid4()), FamilyId=None)
        self.package()
        report = dry_run.plan(self.root)
        codes = {f['code'] for f in report['findings']}
        self.assertIn('missing_parent_UserId', codes)
        self.assertIn('target_limit_CompanyName', codes)

    def test_reused_address_gets_distinct_relationship_keys_and_review(self):
        address = str(uuid.uuid4())
        self.row('Addresses', Id=address, StreetName='  Main\n', HouseNumber='', CityName=None)
        for n in range(2):
            self.row('Users', Id=str(uuid.uuid4()), UserName='Test', Email=f'{n}@example.test', AddressId=address)
        self.package()
        report = dry_run.plan(self.root)
        keys = [tuple(r['sourceKey']) for r in report['mappingCandidates'] if r['entity'] == 'AddressReference']
        self.assertEqual(len(set(keys)), 2)
        self.assertIn('reused_address_expansion_requires_review', {f['code'] for f in report['findings']})

    def test_binary_tampering_and_path_escape_fail(self):
        blob = self.root / 'blob.bin'
        blob.write_bytes(b'proof')
        record = self.row('DatabaseFiles', Id=str(uuid.uuid4()), Size=5,
                          Binary={'path': 'blob.bin', 'size': 5, 'sha256': dry_run.digest(blob)})
        self.package()
        blob.write_bytes(b'wrong')
        with self.assertRaises(ValueError):
            dry_run.plan(self.root)
        record['Binary']['path'] = '../outside.bin'
        self.package()
        with self.assertRaises(ValueError):
            dry_run.plan(self.root)

    def test_mixed_private_shared_document_scope_is_not_widened(self):
        user, tenant, file_id = (str(uuid.uuid4()) for _ in range(3))
        self.row('Users', Id=user, UserName='User', Email='user@example.test')
        self.row('UserFamilies', Id=tenant, FamilyName='Tenant')
        blob = self.root / 'blob.bin'
        blob.write_bytes(b'proof')
        self.row('DatabaseFiles', Id=file_id, FileName='proof.pdf', UserId=user,
                 UserFamilyId=tenant, Size=5,
                 Binary={'path': blob.name, 'size': 5, 'sha256': dry_run.digest(blob)})
        self.package()
        report = dry_run.plan(self.root)
        self.assertIn('document_scope_unresolved_or_mixed', {f['code'] for f in report['findings']})
        mapping = next(r for r in report['mappingCandidates'] if r['entity'] == 'DatabaseFiles')
        self.assertEqual(len(mapping['candidateScopes']), 2)
        self.assertGreater(report['summary']['blockers'], 0)

    def test_utf16_limits_and_invalid_decimal_are_blockers(self):
        user = str(uuid.uuid4())
        self.row('Users', Id=user, UserName='😀' * 51, Email='user@example.test')
        self.row('Contracts', Id=str(uuid.uuid4()), UserId=user, ContractName='Contract',
                 Recurring=3, Type=0, Price='NaN')
        self.package()
        codes = {f['code'] for f in dry_run.plan(self.root)['findings']}
        self.assertIn('target_limit_UserName', codes)
        self.assertIn('invalid_exact_current_amount', codes)


if __name__ == '__main__':
    unittest.main()
