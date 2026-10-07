"""Prepare an isolated loopback API runtime from verified local restore evidence."""
import argparse
import base64
from contextlib import closing
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import secrets
import shutil
import sqlite3

import rehearse_restore as evidence


def prepare(backup, destination, port=5099, synthetic_login=False):
    if not 1024 <= port <= 65535:
        raise ValueError('Choose an unprivileged loopback port')
    source = evidence.directory(backup)
    manifest = evidence.verify(source)
    with closing(sqlite3.connect((source / 'target' / 'qontract.sqlite').as_uri() + '?mode=ro&immutable=1', uri=True)) as db:
        plan = json.loads(db.execute('SELECT PlanJson FROM LocalImportRun').fetchone()[0])
        emails = [email for (email,) in db.execute('SELECT Email FROM Users')]
        if synthetic_login and (plan['sourceInstance'] != 'fixture' or not emails or
                                any(not email.lower().endswith('@example.test') for email in emails)):
            raise ValueError('Synthetic login is restricted to invented example.test fixture accounts')
    destination = evidence.new_directory(destination, source)
    shutil.copytree(source / 'target', destination, dirs_exist_ok=True)
    actual = evidence.inspect_target(destination, manifest['originalRoot'])
    if any(actual[key] != manifest[key] for key in ('files', 'counts', 'planHash')):
        raise ValueError('Runtime copy does not match verified backup')
    now = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S.%f')
    credentials = []
    with closing(sqlite3.connect(destination / 'qontract.sqlite')) as db, db:
        for ident, path in db.execute('SELECT Id, FilePath FROM Documents').fetchall():
            relative = Path(path).relative_to(Path(manifest['originalRoot']))
            db.execute('UPDATE Documents SET FilePath=? WHERE Id=?', (str(destination / relative), ident))
        db.execute('UPDATE AuthSessions SET RevokedAt=? WHERE RevokedAt IS NULL', (now,))
        db.execute('UPDATE PasswordResets SET ConsumedAt=? WHERE ConsumedAt IS NULL', (now,))
        db.execute('UPDATE Users SET AuthVersion=AuthVersion+1')
        if synthetic_login:
            for ident, email in db.execute('SELECT Id, Email FROM Users').fetchall():
                password = secrets.token_urlsafe(24)
                salt = secrets.token_bytes(16)
                hashed = hashlib.pbkdf2_hmac('sha512', password.encode(), salt, 210000, dklen=32)
                stored = 'pbkdf2-sha512:210000:' + base64.b64encode(salt).decode() + ':' + base64.b64encode(hashed).decode()
                db.execute('UPDATE Users SET PasswordHash=?, RequiresPasswordSetup=0 WHERE Id=?', (stored, ident))
                credentials.append({'email': email, 'password': password})
        if db.execute('PRAGMA integrity_check').fetchall() != [('ok',)] or db.execute('PRAGMA foreign_key_check').fetchall():
            raise ValueError('Runtime database integrity failed')
    settings = {
        'Urls': f'http://127.0.0.1:{port}', 'AllowedHosts': '127.0.0.1;localhost',
        'Database': {'Provider': 'sqlite'},
        'ConnectionStrings': {'DefaultConnection': 'Data Source="' + str(destination / 'qontract.sqlite').replace('"', '""') + '"'},
        'Storage': {'Provider': 'local', 'Path': str(destination / 'documents'), 'MaxFileSizeBytes': 52428800},
        'Jwt': {'Key': secrets.token_urlsafe(48), 'Issuer': 'Qontract', 'Audience': 'QontractClients'},
        'Registration': {'AllowPublic': False}, 'PasswordReset': {'Enabled': False},
        'Cors': {'Origins': ['http://127.0.0.1:5098']},
        'Logging': {'LogLevel': {'Default': 'Warning', 'Microsoft.AspNetCore': 'Warning'}}}
    (destination / 'appsettings.Staging.json').write_text(json.dumps(settings, indent=2), encoding='utf-8')
    if credentials:
        (destination / 'local-test-credentials.json').write_text(json.dumps(credentials, indent=2), encoding='utf-8')
    if evidence.inspect_target(destination, str(destination))['counts'] != manifest['counts']:
        raise ValueError('Runtime row counts changed')
    if evidence.inspect_target(source / 'target', manifest['originalRoot']) != actual:
        raise ValueError('Backup changed during preparation')
    # Written last. Existing importer fingerprints intentionally remain as original evidence;
    # runtime edits cannot pass an immutable import replay.
    (destination / 'runtime-rehearsal.json').write_text(json.dumps({
        'format': 'qontract-local-runtime', 'version': 1, 'complete': True,
        'planHash': manifest['planHash'], 'syntheticLogin': synthetic_login,
        'restoredSessionsRevoked': True, 'restoredResetsConsumed': True,
        'productionApproved': False}, indent=2), encoding='utf-8')
    return {'prepared': True, 'loopbackUrl': settings['Urls'], 'syntheticLogin': synthetic_login,
            'emailEnabled': False, 'accounts': len(emails)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('backup'); parser.add_argument('destination')
    parser.add_argument('--port', type=int, default=5099)
    parser.add_argument('--synthetic-login', action='store_true')
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.backup, args.destination, args.port, args.synthetic_login)))
        return 0
    except (OSError, ValueError, KeyError, TypeError, sqlite3.Error):
        print('Local runtime preparation refused or failed; retain private evidence.')
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
