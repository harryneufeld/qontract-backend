import json
from contextlib import closing, contextmanager
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch

import rehearse_restore as rehearsal


class RestoreTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'source'
        self.source.mkdir()
        (self.source / 'documents').mkdir()
        self.doc = self.source / 'documents' / '00000000-0000-0000-0000-000000000001.bin'
        self.doc.write_bytes(b'private document\x00')
        self.marker = 'a' * 64
        (self.source / 'local-import-plan.sha256').write_text(self.marker)
        self.plan = {'format': 'qontract-local-import-plan', 'version': 1, 'productionApproved': False,
                     'rawRecords': {'DatabaseFiles': [{'Id': self.doc.stem,
                     'Binary': {'sha256': rehearsal.digest(self.doc), 'size': self.doc.stat().st_size}}]}}
        with self.db() as db:
            db.executescript('CREATE TABLE LocalImportRun (PlanHash TEXT, Fingerprint TEXT, PlanJson TEXT);'
                             'CREATE TABLE LocalSourceLedger (RawJson TEXT);'
                             'CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT);'
                             'CREATE TABLE Documents (FilePath TEXT, FileSize INTEGER, UserId INTEGER REFERENCES Users(Id));')
            db.execute('INSERT INTO LocalImportRun VALUES (?, ?, ?)', (self.marker, 'reviewed fingerprint', json.dumps(self.plan)))
            db.execute('INSERT INTO Users VALUES (1, ?)', ('Original Å\ntext',))
            db.execute('INSERT INTO LocalSourceLedger VALUES (?)', (json.dumps({'original': '  untouched  '}),))
            db.execute('INSERT INTO Documents VALUES (?, ?, 1)', (str(self.doc), self.doc.stat().st_size))
        self.backup = self.root / 'backup'
        self.restored = self.root / 'restored'

    @contextmanager
    def db(self):
        with closing(sqlite3.connect(self.source / 'qontract.sqlite')) as db, db:
            yield db

    def save(self):
        return rehearsal.backup(self.source, self.backup, True)

    def test_restore_reads_only_copy_even_after_source_is_unavailable(self):
        original = rehearsal.tree(self.source)
        report = self.save()
        self.assertEqual(original, rehearsal.tree(self.source))
        self.source.rename(self.root / 'unavailable')
        restored = rehearsal.restore(self.backup, self.restored)
        self.assertEqual(report, restored)
        self.assertEqual(original, rehearsal.tree(self.restored))
        self.assertFalse(restored['runtimeReady'])

    def test_offline_confirmation_and_new_separate_destinations_required(self):
        with self.assertRaises(ValueError):
            rehearsal.backup(self.source, self.backup)
        with self.assertRaises(ValueError):
            rehearsal.backup(self.source, self.source / 'nested', True)
        self.save()
        with self.assertRaises(FileExistsError):
            self.save()
        with self.assertRaises(FileExistsError):
            rehearsal.restore(self.backup, self.source)
        self.assertFalse((self.source / 'nested').exists())

    def test_missing_changed_and_extra_document_bytes_rejected(self):
        self.save()
        blob = self.backup / 'target' / 'documents' / self.doc.name
        original = blob.read_bytes()
        blob.write_bytes(b'x' * len(original))
        with self.assertRaises(ValueError):
            rehearsal.verify(self.backup)
        blob.unlink()
        with self.assertRaises(ValueError):
            rehearsal.verify(self.backup)
        blob.write_bytes(original)
        (blob.parent / 'unexpected.bin').write_bytes(b'extra')
        with self.assertRaises(ValueError):
            rehearsal.verify(self.backup)

    def test_target_database_edits_and_missing_manifest_rejected(self):
        self.save()
        with closing(sqlite3.connect(self.backup / 'target' / 'qontract.sqlite')) as db, db:
            db.execute("UPDATE Users SET Name='edited'")
        with self.assertRaises(ValueError):
            rehearsal.restore(self.backup, self.restored)
        self.assertFalse(self.restored.exists())
        (self.backup / 'manifest.json').unlink()
        with self.assertRaises(FileNotFoundError):
            rehearsal.verify(self.backup)

    def test_source_document_mapping_and_foreign_key_errors_rejected(self):
        with self.db() as db:
            db.execute('UPDATE Documents SET UserId=99')
        with self.assertRaises(ValueError):
            self.save()
        with self.db() as db:
            db.execute('UPDATE Documents SET UserId=1, FilePath=?', (str(self.root / 'outside.bin'),))
        with self.assertRaises(ValueError):
            self.save()
        self.assertFalse(self.backup.exists())

    def test_unfinished_import_and_sqlite_sidecars_refused(self):
        (self.source / 'qontract.sqlite-wal').write_bytes(b'')
        with self.assertRaises(ValueError):
            self.save()
        (self.source / 'qontract.sqlite-wal').unlink()
        with self.db() as db:
            db.execute('DELETE FROM LocalImportRun')
        with self.assertRaises(ValueError):
            self.save()

    def test_source_changes_during_copy_leave_no_complete_manifest(self):
        original_copy = rehearsal.shutil.copytree

        def changing_copy(*args, **kwargs):
            result = original_copy(*args, **kwargs)
            (self.source / 'extra.txt').write_text('concurrent write')
            return result

        with patch.object(rehearsal.shutil, 'copytree', side_effect=changing_copy):
            with self.assertRaises(ValueError):
                self.save()
        self.assertFalse((self.backup / 'manifest.json').exists())

    def test_reparse_points_refused_without_following_them(self):
        real_stat = Path.lstat

        def linked_stat(path):
            result = real_stat(path)
            if path == self.doc:
                class ReparseStat:
                    st_file_attributes = 0x400
                    st_mode = result.st_mode
                return ReparseStat()
            return result

        with patch.object(Path, 'lstat', linked_stat):
            with self.assertRaises(ValueError):
                self.save()


if __name__ == '__main__':
    unittest.main()
