"""Resolve explicitly reviewed choices into a plan for an isolated local import."""
import argparse
import json
from pathlib import Path

import dry_run


def resolve(package, policy):
    manifest, rows, idx = dry_run.validate(package)
    report = dry_run.plan(package)
    if policy.get('sourceInstance') != manifest['sourceInstance']:
        raise ValueError('Decisions belong to a different source')
    if policy.get('displayNamePolicy') != 'use_original_email_when_source_name_blank':
        raise ValueError('Unsupported display-name decision')
    if policy.get('documentUploaderPolicy') != 'unique_contract_user_else_tenant_admin':
        raise ValueError('Uploader decision has not been reviewed')
    admins = [u for u in rows['Users'] if (u['Email'] or '').strip().casefold()
              == policy.get('tenantAdminEmail', '').strip().casefold()]
    if len(admins) != 1:
        raise ValueError('Selected administrator must match exactly one source account')
    admin_id = admins[0]['Id']
    memberships = {(r['UsersId'], r['FamiliesId']) for r in rows['FamilyUserList']}
    if any((admin_id, t['Id']) not in memberships for t in rows['UserFamilies']):
        raise ValueError('Selected administrator is not a member of every exported tenant')
    names = {}
    for user in rows['Users']:
        value = user['UserName']
        names[user['Id']] = value if isinstance(value, str) and value.strip() else user['Email']
        if not isinstance(names[user['Id']], str) or not names[user['Id']].strip() or len(names[user['Id']].encode('utf-16-le')) // 2 > 100:
            raise ValueError('Reviewed display name does not fit the target')
    uploaders, provenance = {}, {}
    for file in rows['DatabaseFiles']:
        file_id = file['Id']
        scope = next(m['candidateScopes'] for m in report['mappingCandidates']
                     if m['entity'] == 'DatabaseFiles' and m['sourceId'] == file_id)
        if len(scope) != 1:
            raise ValueError('Cannot resolve mixed/unknown document scope')
        owners = {idx['Contracts'][link['ContractsId']]['UserId']
                  for link in rows['ContractFileList'] if link['FilesId'] == file_id}
        if file['UserId'] is not None:
            uploader, reason = file['UserId'], 'direct_source_user'
        elif len(owners) == 1:
            uploader, reason = next(iter(owners)), 'reviewed_contract_user_inference'
        elif owners:
            raise ValueError('Linked contracts have different users; no uploader can be inferred')
        elif scope[0]['kind'] == 'tenant':
            uploader, reason = admin_id, 'reviewed_tenant_admin_fallback'
        else:
            raise ValueError('Private standalone document has no reviewed uploader')
        if uploader not in idx['Users']:
            raise ValueError('Uploader account is missing')
        if scope[0]['kind'] == 'private' and scope[0]['sourceOwner'] != uploader:
            raise ValueError('Uploader would change private ownership')
        if scope[0]['kind'] == 'tenant' and (uploader, scope[0]['sourceOwner']) not in memberships:
            raise ValueError('Tenant uploader must be a member')
        uploaders[file_id], provenance[file_id] = uploader, reason
    resolved = {'target_required_UserName', 'tenant_admin_assignment_required',
                'document_uploader_provenance_requires_review', 'mapping_policy_confirmation_required'}
    remaining = [f for f in report['findings'] if f['code'] not in resolved]
    if any(f['severity'] in ('blocker', 'review') for f in remaining):
        raise ValueError('Unresolved findings still block local import')
    return {'format': 'qontract-local-import-plan', 'version': 1,
            'sourceInstance': manifest['sourceInstance'], 'exportId': manifest['exportId'],
            'packagePath': str(package.resolve()), 'manifestSha256': dry_run.digest(package / 'manifest.json'),
            'rawRecords': rows, 'displayNames': names, 'tenantAdminSourceId': admin_id,
            'documentUploaders': uploaders, 'uploaderProvenance': provenance,
            'documentScopes': {m['sourceId']: m['candidateScopes'][0] for m in report['mappingCandidates']
                               if m['entity'] == 'DatabaseFiles'},
            'reviewedPolicy': policy, 'remainingFindings': remaining,
            'localDefaults': {'currency': None, 'contractStatus': 'Active',
                              'retainLegacyIsActive': True, 'audit': 'operational_import_time_raw_source_in_ledger',
                              'tenantType': 'Family', 'contractorType': 'Company'},
            'readyForLocalImport': True, 'productionApproved': False}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('package', type=Path)
    p.add_argument('--decisions', type=Path, required=True)
    p.add_argument('--plan', type=Path, required=True)
    args = p.parse_args()
    try:
        policy = json.loads(args.decisions.read_text(encoding='utf-8-sig'))
        plan = resolve(args.package.resolve(), policy)
        with args.plan.open('x', encoding='utf-8') as stream:
            json.dump(plan, stream, ensure_ascii=False, indent=2)
    except Exception:
        p.exit(1, 'Decision resolution failed; no source or target records changed.\n')
    print(json.dumps({'readyForLocalImport': True, 'productionApproved': False,
                      'users': len(plan['displayNames']), 'documents': len(plan['documentUploaders'])}))


if __name__ == '__main__':
    main()
