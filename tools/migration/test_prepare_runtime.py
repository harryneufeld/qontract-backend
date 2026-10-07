import base64
from contextlib import closing
import hashlib
import json
import sqlite3
import unittest

import prepare_runtime
import rehearse_restore
import test_rehearse_restore


class RuntimeTests(unittest.TestCase):
    def setUp(self):
        self.fixture = test_rehearse_restore.RestoreTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        f = self.fixture
        with f.db() as db:
            db.executescript('ALTER TABLE Users ADD COLUMN Email TEXT;'
                             'ALTER TABLE Users ADD COLUMN AuthVersion INTEGER DEFAULT 3;'
                             'ALTER TABLE Users ADD COLUMN PasswordHash TEXT DEFAULT \'original\';'
                             'ALTER TABLE Users ADD COLUMN RequiresPasswordSetup INTEGER DEFAULT 1;'
                             'ALTER TABLE Documents ADD COLUMN Id INTEGER DEFAULT 1;'
                             'CREATE TABLE AuthSessions (RevokedAt TEXT);'
                             'CREATE TABLE PasswordResets (ConsumedAt TEXT);'
                             'INSERT INTO AuthSessions VALUES(NULL); INSERT INTO PasswordResets VALUES(NULL);')
            db.execute('UPDATE Users SET Email=?', ('restore@example.test',))
            f.plan['sourceInstance'] = 'fixture'
            db.execute('UPDATE LocalImportRun SET PlanJson=?', (json.dumps(f.plan),))
        f.save()
        self.runtime = f.root / 'runtime'

    def test_relocates_storage_invalidates_auth_and_keeps_backup_immutable(self):
        f = self.fixture
        before = rehearse_restore.tree(f.backup)
        result = prepare_runtime.prepare(f.backup, self.runtime)
        self.assertTrue(result['prepared'])
        self.assertFalse(result['emailEnabled'])
        with closing(sqlite3.connect(self.runtime / 'qontract.sqlite')) as db:
            self.assertEqual(db.execute('SELECT FilePath FROM Documents').fetchone()[0], str(self.runtime / 'documents' / f.doc.name))
            self.assertEqual(db.execute('SELECT AuthVersion, RequiresPasswordSetup, PasswordHash FROM Users').fetchone(), (4, 1, 'original'))
            self.assertIsNotNone(db.execute('SELECT RevokedAt FROM AuthSessions').fetchone()[0])
            self.assertIsNotNone(db.execute('SELECT ConsumedAt FROM PasswordResets').fetchone()[0])
            self.assertEqual(db.execute('SELECT RawJson FROM LocalSourceLedger').fetchone()[0], json.dumps({'original': '  untouched  '}))
        self.assertEqual(before, rehearse_restore.tree(f.backup))
        self.assertFalse((self.runtime / 'local-test-credentials.json').exists())
        config = json.loads((self.runtime / 'appsettings.Staging.json').read_text())
        self.assertFalse(config['Registration']['AllowPublic'])
        self.assertFalse(config['PasswordReset']['Enabled'])
        self.assertTrue(config['Urls'].startswith('http://127.0.0.1:'))

    def test_synthetic_login_generates_compatible_hash_without_changing_original_setup(self):
        f = self.fixture
        prepare_runtime.prepare(f.backup, self.runtime, synthetic_login=True)
        credentials = json.loads((self.runtime / 'local-test-credentials.json').read_text())[0]
        with closing(sqlite3.connect(self.runtime / 'qontract.sqlite')) as db:
            stored, setup = db.execute('SELECT PasswordHash, RequiresPasswordSetup FROM Users').fetchone()
        scheme, iterations, salt, hashed = stored.split(':')
        self.assertEqual(scheme, 'pbkdf2-sha512')
        self.assertEqual(setup, 0)
        computed = hashlib.pbkdf2_hmac('sha512', credentials['password'].encode(), base64.b64decode(salt), int(iterations), 32)
        self.assertEqual(computed, base64.b64decode(hashed))
        with f.db() as db:
            self.assertEqual(db.execute('SELECT RequiresPasswordSetup, PasswordHash FROM Users').fetchone(), (1, 'original'))

    def test_synthetic_login_refuses_real_recipients_before_creating_runtime(self):
        f = self.fixture
        with f.db() as db:
            db.execute("UPDATE Users SET Email='real@example.com'")
        alternate = f.root / 'alternate-backup'
        rehearse_restore.backup(f.source, alternate, True)
        with self.assertRaises(ValueError):
            prepare_runtime.prepare(alternate, self.runtime, synthetic_login=True)
        self.assertFalse(self.runtime.exists())

    def test_rejects_existing_destination_and_bad_port(self):
        f = self.fixture
        with self.assertRaises(ValueError):
            prepare_runtime.prepare(f.backup, self.runtime, port=80)
        prepare_runtime.prepare(f.backup, self.runtime)
        with self.assertRaises(FileExistsError):
            prepare_runtime.prepare(f.backup, self.runtime)


if __name__ == '__main__':
    unittest.main()
