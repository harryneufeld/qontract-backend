"""Offline backup/restore evidence for LocalImport SQLite targets, never deployment."""
import argparse
from contextlib import closing
import hashlib
import json
import os
from pathlib import Path
import shutil
import sqlite3

FORMAT = 'qontract-local-restore-evidence'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def tree(root):
    """Reject links/reparse points, including Windows junctions."""
    result = {}
    for base, directories, files in os.walk(root, followlinks=False):
        for name in directories + files:
            path = Path(base) / name
            if path.is_symlink() or getattr(path.lstat(), 'st_file_attributes', 0) & 0x400:
                raise ValueError('Linked storage is unsupported')
        for name in files:
            path = Path(base) / name
            result[path.relative_to(root).as_posix()] = {'size': path.stat().st_size, 'sha256': digest(path)}
    return dict(sorted(result.items()))


def directory(path):
    path = Path(os.path.abspath(path))
    if path.resolve() != path or not path.is_dir():
        raise ValueError('A real directory is required')
    if getattr(path.lstat(), 'st_file_attributes', 0) & 0x400:
        raise ValueError('Linked directory is unsupported')
    return path


def new_directory(path, source):
    path = Path(os.path.abspath(path))
    if path.resolve() != path or path == source or source in path.parents or path in source.parents:
        raise ValueError('Destination must be separate from the source')
    path.mkdir(parents=True, exist_ok=False)
    return path


def inspect_target(root, original_root):
    """Read copied bytes only; absolute document references map into this copy."""
    files = tree(root)
    if any(name.endswith(('-wal', '-shm', '-journal')) for name in files):
        raise ValueError('SQLite sidecars present; close writers cleanly first')
    if 'qontract.sqlite' not in files or 'local-import-plan.sha256' not in files:
        raise ValueError('Not a LocalImport target')
    marker = (root / 'local-import-plan.sha256').read_text(encoding='utf-8-sig').strip()
    if len(marker) != 64 or any(c not in '0123456789abcdefABCDEF' for c in marker):
        raise ValueError('Invalid plan marker')
    with closing(sqlite3.connect((root / 'qontract.sqlite').as_uri() + '?mode=ro&immutable=1', uri=True)) as db:
        if db.execute('PRAGMA integrity_check').fetchall() != [('ok',)] or db.execute('PRAGMA foreign_key_check').fetchall():
            raise ValueError('Database integrity check failed')
        runs = db.execute('SELECT PlanHash, Fingerprint, PlanJson FROM LocalImportRun').fetchall()
        if len(runs) != 1 or runs[0][0] != marker:
            raise ValueError('Incomplete or mismatched import evidence')
        plan = json.loads(runs[0][2])
        if plan.get('format') != 'qontract-local-import-plan' or plan.get('version') != 1 or plan.get('productionApproved') is not False:
            raise ValueError('Unsupported local plan')
        counts = {}
        for (name,) in db.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"):
            escaped = name.replace('"', '""')
            counts[name] = db.execute(f'SELECT COUNT(*) FROM "{escaped}"').fetchone()[0]
        source_files = {row['Id'].lower(): row for row in plan['rawRecords']['DatabaseFiles']}
        seen = set()
        for path, size in db.execute('SELECT FilePath, FileSize FROM Documents'):
            # Do not resolve the original path: the old machine/storage may no longer exist.
            relative = Path(path).relative_to(Path(original_root))
            if '..' in relative.parts or len(relative.parts) != 2 or relative.parts[0] != 'documents':
                raise ValueError('Document path escapes paired storage')
            key = relative.as_posix()
            source_id = relative.stem.lower()
            binary = source_files[source_id]['Binary']
            if relative.suffix != '.bin' or source_id in seen or files.get(key) != {'size': size, 'sha256': binary['sha256'].lower()} or size != binary['size']:
                raise ValueError('Document bytes or source relationship disagree')
            seen.add(source_id)
        if seen != set(source_files):
            raise ValueError('Missing document mapping')
    return {'files': files, 'counts': counts, 'planHash': marker}


def backup(source, destination, offline_confirmed=False):
    if not offline_confirmed:
        raise ValueError('Stop all writers and confirm offline operation')
    source = directory(source)
    before = inspect_target(source, str(source))
    destination = new_directory(destination, source)
    payload = destination / 'target'
    shutil.copytree(source, payload)
    after = inspect_target(source, str(source))
    copied = inspect_target(payload, str(source))
    if before != after or before != copied:
        raise ValueError('Source changed during backup; incomplete evidence must not be used')
    evidence = {'format': FORMAT, 'version': 1, 'originalRoot': str(source), **copied}
    # Written last: an interrupted copy is never a complete backup.
    with (destination / 'manifest.json').open('x', encoding='utf-8') as stream:
        json.dump(evidence, stream, indent=2, sort_keys=True)
    verify(destination)
    return summary(evidence)


def verify(backup_path):
    root = directory(backup_path)
    tree(root)  # Reject linked manifests and storage before opening either.
    evidence = json.loads((root / 'manifest.json').read_text(encoding='utf-8'))
    if evidence.get('format') != FORMAT or evidence.get('version') != 1:
        raise ValueError('Unsupported backup evidence')
    payload = directory(root / 'target')
    actual = inspect_target(payload, evidence['originalRoot'])
    if any(actual[key] != evidence[key] for key in ('files', 'counts', 'planHash')):
        raise ValueError('Backup contents disagree with manifest')
    return evidence


def restore(backup_path, destination):
    root = directory(backup_path)
    evidence = verify(root)
    destination = new_directory(destination, root)
    shutil.copytree(root / 'target', destination, dirs_exist_ok=True)
    actual = inspect_target(destination, evidence['originalRoot'])
    if any(actual[key] != evidence[key] for key in ('files', 'counts', 'planHash')):
        raise ValueError('Restored contents disagree with backup')
    # Evidence is kept alongside the immutable restored target, never inside it.
    return summary(evidence)


def summary(evidence):
    return {'verified': True, 'files': len(evidence['files']), 'counts': evidence['counts'], 'databaseMutations': 0,
            'runtimeReady': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    save = commands.add_parser('backup')
    save.add_argument('source'); save.add_argument('destination')
    save.add_argument('--offline-confirmed', action='store_true')
    check = commands.add_parser('verify'); check.add_argument('backup')
    load = commands.add_parser('restore'); load.add_argument('backup'); load.add_argument('destination')
    args = parser.parse_args()
    try:
        if args.command == 'backup':
            result = backup(args.source, args.destination, args.offline_confirmed)
        elif args.command == 'verify':
            result = summary(verify(args.backup))
        else:
            result = restore(args.backup, args.destination)
        print(json.dumps(result, sort_keys=True))
        return 0
    except (OSError, ValueError, KeyError, TypeError, sqlite3.Error):
        print('Local restore rehearsal refused or failed; retain private evidence for diagnosis.')
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
