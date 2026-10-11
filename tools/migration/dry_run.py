"""Validate a private local export and plan source mappings without target writes."""
import argparse
from collections import Counter
from contextlib import closing
from decimal import Decimal, InvalidOperation
import hashlib
import json
from pathlib import Path
import sqlite3
import uuid

TABLES = {'Users', 'UserFamilies', 'Companies', 'Addresses', 'Contracts',
          'DatabaseFiles', 'FamilyUserList', 'ContractFileList'}
EXCLUDED = {'Invoices', 'PriceHistories', 'ContractPriceHistory'}
SECRET_FIELDS = {'PasswordHash', 'SessionId', 'RefreshToken', '_inlineHex'}
FIELDS = {
    'Users': 'Id UserName Email AddressId Created Modified',
    'UserFamilies': 'Id FamilyName AddressId Created Modified',
    'Companies': 'Id CompanyName Description Email Phone Website Notes AddressId UserId FamilyId Created Modified',
    'Addresses': 'Id StreetName HouseNumber ZipCode CityName CountryName Created Modified',
    'Contracts': 'Id ContractName Description ContractNumber StartDate EndDate MinContractDate FamilyId UserId CompanyId Price Recurring Iban Notes IsActive Type Created Modified',
    'DatabaseFiles': 'Id FileName Description Date Path Category MimeType Size UserId UserFamilyId Created Modified Binary',
    'FamilyUserList': 'UsersId FamiliesId', 'ContractFileList': 'ContractsId FilesId',
}


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()


def member(root, name):
    if not isinstance(name, str) or not name or '\\' in name:
        raise ValueError('Invalid package member')
    path = (root / name).resolve()
    if not path.is_relative_to(root.resolve()) or path == root.resolve() or not path.is_file():
        raise ValueError('Missing or unsafe package member')
    return path


def validate(root):
    manifest = json.loads(member(root, 'manifest.json').read_text(encoding='utf-8'))
    if (manifest.get('format') != 'familyplanner-local-export' or manifest.get('version') != 1
            or manifest.get('complete') is not True or manifest.get('credentialsIncluded') is not False
            or set(manifest.get('excludedTables', [])) != EXCLUDED
            or set(manifest.get('entities', {})) != TABLES
            or not isinstance(manifest.get('sourceInstance'), str) or not manifest['sourceInstance'].strip()):
        raise ValueError('Unsupported or incomplete export manifest')
    uuid.UUID(manifest['exportId'])
    rows, indexes = {}, {}
    for table in sorted(TABLES):
        entry = manifest['entities'][table]
        path = member(root, entry['path'])
        if digest(path) != entry['sha256']:
            raise ValueError('Entity checksum mismatch')
        records = json.loads(path.read_text(encoding='utf-8'))
        if not isinstance(records, list) or len(records) != entry['count']:
            raise ValueError('Entity count mismatch')
        seen = set()
        for record in records:
            if not isinstance(record, dict) or SECRET_FIELDS.intersection(record):
                raise ValueError('Credential/inline payload or invalid record in package')
            if set(record) != set(FIELDS[table].split()):
                raise ValueError('Missing or unexpected source fields')
            for field, value in record.items():
                if (field == 'Id' or field.endswith('Id')) and value is not None:
                    uuid.UUID(value)
            keys = ('UsersId', 'FamiliesId') if table == 'FamilyUserList' else (
                ('ContractsId', 'FilesId') if table == 'ContractFileList' else ('Id',))
            key = tuple(str(uuid.UUID(record[k])) for k in keys)
            if key in seen:
                raise ValueError('Duplicate source entity or relationship identity')
            seen.add(key)
        rows[table] = records
        if table not in {'FamilyUserList', 'ContractFileList'}:
            indexes[table] = {r['Id']: r for r in records}
    for record in rows['DatabaseFiles']:
        binary = record['Binary']
        path = member(root, binary['path'])
        if digest(path) != binary['sha256'] or path.stat().st_size != binary['size']:
            raise ValueError('Document byte checksum/size mismatch')
        if record['Size'] is not None and record['Size'] != binary['size']:
            raise ValueError('Declared document size mismatch')
    if 'selection' in manifest:
        selection = manifest['selection']
        selection_path = member(root, selection['path'])
        if selection.get('policy') != 'first_test_member_with_owned_contract_and_email_v1' or digest(selection_path) != selection['sha256']:
            raise ValueError('Invalid scoped-source exclusion evidence')
        exclusions = json.loads(selection_path.read_text(encoding='utf-8'))
        if (exclusions.get('policy') != selection['policy'] or exclusions.get('productionApproved') is not False
                or len(exclusions['excludedRecords']) != selection['count']
                or set(exclusions['originalCounts']) != TABLES):
            raise ValueError('Invalid scoped-source counts')
        counts = Counter(r['entity'] for r in exclusions['excludedRecords'])
        if set(counts) - TABLES or any(len(rows[t]) + counts[t] != exclusions['originalCounts'][t] for t in TABLES):
            raise ValueError('Scoped-source exclusions do not reconcile')
    return manifest, rows, indexes


def plan(root):
    manifest, rows, idx = validate(root)
    findings, mappings = [], []

    def finding(code, entity, id_, severity='blocker'):
        findings.append({'code': code, 'entity': entity, 'sourceId': id_, 'severity': severity})

    def reference(record, field, parent, entity, required=False):
        value = record[field]
        if value is None:
            if required:
                finding('missing_required_parent', entity, record.get('Id', record), 'blocker')
        elif value not in idx[parent]:
            finding('missing_parent_' + field, entity, record.get('Id', record))

    for table in ('Users', 'UserFamilies', 'Companies'):
        for r in rows[table]:
            reference(r, 'AddressId', 'Addresses', table)
    for table in ('Companies', 'Contracts'):
        for r in rows[table]:
            reference(r, 'UserId', 'Users', table, True)
            reference(r, 'FamilyId', 'UserFamilies', table)
    for r in rows['Contracts']:
        reference(r, 'CompanyId', 'Companies', 'Contracts')
        contractor = idx['Companies'].get(r['CompanyId'])
        if contractor and (contractor['FamilyId'] != r['FamilyId'] or (
                r['FamilyId'] is None and contractor['UserId'] != r['UserId'])):
            finding('contract_contractor_scope_mismatch', 'Contracts', r['Id'])
        if r['Recurring'] not in range(6) or r['Type'] not in range(5):
            finding('unknown_source_enum', 'Contracts', r['Id'])
        if r['Price'] is None:
            finding('unknown_current_amount_preserved', 'Contracts', r['Id'], 'information')
        else:
            try:
                if not isinstance(r['Price'], str) or not Decimal(r['Price']).is_finite():
                    raise InvalidOperation
            except InvalidOperation:
                finding('invalid_exact_current_amount', 'Contracts', r['Id'])
    for r in rows['FamilyUserList']:
        reference(r, 'UsersId', 'Users', 'FamilyUserList', True)
        reference(r, 'FamiliesId', 'UserFamilies', 'FamilyUserList', True)
    for r in rows['ContractFileList']:
        reference(r, 'ContractsId', 'Contracts', 'ContractFileList', True)
        reference(r, 'FilesId', 'DatabaseFiles', 'ContractFileList', True)
    for r in rows['DatabaseFiles']:
        reference(r, 'UserId', 'Users', 'DatabaseFiles')
        reference(r, 'UserFamilyId', 'UserFamilies', 'DatabaseFiles')

    limits = {'Users': {'UserName': 100, 'Email': 100},
              'UserFamilies': {'FamilyName': 100},
              'Companies': {'CompanyName': 100, 'Email': 200, 'Phone': 50},
              'Contracts': {'ContractName': 200, 'Description': 2000, 'ContractNumber': 100},
              'DatabaseFiles': {'FileName': 255, 'Description': 2000, 'Category': 100, 'MimeType': 200}}
    for table, fields in limits.items():
        for r in rows[table]:
            for field, limit in fields.items():
                value = r[field]
                # .NET String.Length counts UTF-16 units, including astral characters.
                if value is not None and (not isinstance(value, str) or len(value.encode('utf-16-le')) // 2 > limit):
                    finding('target_limit_' + field, table, r['Id'])
    for table, field in (('Users', 'UserName'), ('UserFamilies', 'FamilyName'),
                         ('Companies', 'CompanyName'), ('Contracts', 'ContractName')):
        for r in rows[table]:
            if not isinstance(r[field], str) or not r[field].strip():
                finding('target_required_' + field, table, r['Id'])
    emails = Counter((r['Email'] or '').strip().upper() for r in rows['Users'])
    for r in rows['Users']:
        email = (r['Email'] or '').strip().upper()
        if not email or emails[email] > 1:
            finding('blank_or_duplicate_normalized_email', 'Users', r['Id'])
        mappings.append({'entity': 'Users', 'sourceId': r['Id'], 'targetEntity': 'User',
                         'requiresPasswordSetup': True, 'sourceKey': [manifest['sourceInstance'], 'Users', r['Id']]})

    for table, target in (('UserFamilies', 'Tenant'), ('Companies', 'Contractor'), ('Contracts', 'Contract')):
        for r in rows[table]:
            scope = {'kind': 'tenant', 'sourceOwner': r['Id']} if table == 'UserFamilies' else (
                {'kind': 'tenant', 'sourceOwner': r['FamilyId']} if r['FamilyId'] is not None else
                {'kind': 'private', 'sourceOwner': r['UserId']})
            mappings.append({'entity': table, 'sourceId': r['Id'], 'targetEntity': target,
                             'scope': scope, 'sourceKey': [manifest['sourceInstance'], table, r['Id']]})
    used_addresses = Counter()
    for table in ('Users', 'UserFamilies', 'Companies'):
        for r in rows[table]:
            address = r['AddressId']
            if address is not None:
                used_addresses[address] += 1
                mappings.append({'entity': 'AddressReference', 'sourceId': address, 'parentEntity': table,
                                 'parentSourceId': r['Id'], 'targetEntity': 'Address',
                                 'sourceKey': [manifest['sourceInstance'], table, r['Id'], 'AddressId']})
    for r in rows['Addresses']:
        count = used_addresses[r['Id']]
        if count == 0:
            finding('unused_address_disposition_required', 'Addresses', r['Id'], 'review')
        if count > 1:
            finding('reused_address_expansion_requires_review', 'Addresses', r['Id'], 'review')

    for r in rows['UserFamilies']:
        finding('tenant_admin_assignment_required', 'UserFamilies', r['Id'], 'review')
    finding('mapping_policy_confirmation_required', 'Package', manifest['exportId'], 'review')
    for r in rows['DatabaseFiles']:
        scopes = set()
        if r['UserId'] is not None:
            scopes.add(('private', r['UserId']))
        if r['UserFamilyId'] is not None:
            scopes.add(('tenant', r['UserFamilyId']))
        for link in rows['ContractFileList']:
            contract = idx['Contracts'].get(link['ContractsId'])
            if link['FilesId'] == r['Id'] and contract:
                scopes.add(('tenant', contract['FamilyId']) if contract['FamilyId'] is not None
                           else ('private', contract['UserId']))
        if len(scopes) != 1:
            finding('document_scope_unresolved_or_mixed', 'DatabaseFiles', r['Id'])
        if r['UserId'] is None:
            finding('document_uploader_provenance_requires_review', 'DatabaseFiles', r['Id'], 'review')
        mappings.append({'entity': 'DatabaseFiles', 'sourceId': r['Id'], 'targetEntity': 'Document',
                         'candidateScopes': [{'kind': kind, 'sourceOwner': owner} for kind, owner in sorted(scopes)],
                         'sourceKey': [manifest['sourceInstance'], 'DatabaseFiles', r['Id']]})
    counts = Counter(f['severity'] for f in findings)
    return {'format': 'qontract-migration-dry-run', 'version': 1,
            'sourceInstance': manifest['sourceInstance'], 'exportId': manifest['exportId'],
            'targetWrites': 0, 'counts': {t: len(r) for t, r in sorted(rows.items())},
            'summary': {'blockers': counts['blocker'], 'reviews': counts['review'],
                        'information': counts['information'], 'readyForImport': not counts['blocker'] and not counts['review']},
            'findings': findings, 'mappingCandidates': mappings,
            'limitations': ['No target database comparison or importer writes',
                            'No SMTP delivery or imported API/app access verification',
                            'Roles, active/status defaults, source datetime zone and unknown currency need operator policy']}


def stage(root, database, report):
    """Retain raw source evidence in a separate SQLite stage, never application tables."""
    manifest, rows, _ = validate(root)
    if (report.get('sourceInstance') != manifest['sourceInstance']
            or report.get('exportId') != manifest['exportId'] or report.get('targetWrites') != 0):
        raise ValueError('Report does not belong to this package or is not a dry run')
    export_id = manifest['exportId']
    manifest_hash = digest(root / 'manifest.json')
    report_json = json.dumps(report, ensure_ascii=False, sort_keys=True)
    records = []
    for table, values in sorted(rows.items()):
        fields = ('UsersId', 'FamiliesId') if table == 'FamilyUserList' else (
            ('ContractsId', 'FilesId') if table == 'ContractFileList' else ('Id',))
        for row in values:
            key = json.dumps([manifest['sourceInstance'], table, *[row[k] for k in fields]])
            records.append((table, key, json.dumps(row, ensure_ascii=False, sort_keys=True)))
    # Only this distinct staging file is touched; it contains private source records.
    with closing(sqlite3.connect(database)) as db, db:
        existing = {r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")}
        if existing - {'StagedExports', 'StagedRecords'}:
            raise ValueError('Refusing to use a non-staging database')
        db.execute('PRAGMA foreign_keys = ON')
        db.execute('CREATE TABLE IF NOT EXISTS StagedExports ('
                   'ExportId TEXT PRIMARY KEY, SourceInstance TEXT NOT NULL, '
                   'ManifestHash TEXT NOT NULL, PackagePath TEXT NOT NULL, ReportJson TEXT NOT NULL)')
        db.execute('CREATE TABLE IF NOT EXISTS StagedRecords ('
                   'ExportId TEXT NOT NULL REFERENCES StagedExports(ExportId), '
                   'Entity TEXT NOT NULL, SourceKey TEXT NOT NULL, RawJson TEXT NOT NULL, '
                   'PRIMARY KEY (ExportId, Entity, SourceKey))')
        prior = db.execute('SELECT SourceInstance, ManifestHash, ReportJson FROM StagedExports '
                           'WHERE ExportId = ?', (export_id,)).fetchone()
        if prior:
            if prior != (manifest['sourceInstance'], manifest_hash, report_json):
                raise ValueError('Conflicting export identity or changed planning policy')
            stored = db.execute('SELECT Entity, SourceKey, RawJson FROM StagedRecords WHERE ExportId = ? '
                                'ORDER BY Entity, SourceKey', (export_id,)).fetchall()
            if stored != sorted(records):
                raise ValueError('Staged raw evidence does not match the validated package')
            return {'replay': True, 'newStagedRecords': 0, 'stagedRecords': len(records), 'targetWrites': 0}
        db.execute('INSERT INTO StagedExports VALUES (?, ?, ?, ?, ?)',
                   (export_id, manifest['sourceInstance'], manifest_hash, str(root.resolve()), report_json))
        db.executemany('INSERT INTO StagedRecords VALUES (?, ?, ?, ?)',
                       [(export_id, *r) for r in records])
    return {'replay': False, 'newStagedRecords': len(records), 'stagedRecords': len(records), 'targetWrites': 0}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('export', type=Path)
    p.add_argument('--report', type=Path, required=True)
    p.add_argument('--stage-db', type=Path, help='Optional separate private SQLite evidence store; never the app database')
    args = p.parse_args()
    try:
        if args.report.exists():
            raise ValueError('Report already exists')
        report = plan(args.export.resolve())
        staging = stage(args.export.resolve(), args.stage_db, report) if args.stage_db else None
        # Refuse overwriting evidence from a previous run.
        with args.report.open('x', encoding='utf-8') as stream:
            json.dump(report, stream, ensure_ascii=False, indent=2)
    except Exception:
        p.exit(1, 'Dry run failed: invalid package or report path. No target writes occurred.\n')
    print(json.dumps({'counts': report['counts'], **report['summary'], 'targetWrites': 0, 'staging': staging}))


if __name__ == '__main__':
    main()
