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
    memberships = {(r['UsersId'], r['FamiliesId']) for r in rows['FamilyUserList']}
    tenant_ids = {t['Id'] for t in rows['UserFamilies']}
    choices = policy.get('tenantAdminEmails')
    if choices is None:
        choices = {t: policy.get('tenantAdminEmail', '') for t in tenant_ids}
    if set(choices) != tenant_ids:
        raise ValueError('Administrator choices must cover exactly the exported tenants')
    admins = {}
    for tenant, email in choices.items():
        users = [u for u in rows['Users'] if (u['Email'] or '').strip().casefold() == email.strip().casefold()]
        if len(users) != 1 or (users[0]['Id'], tenant) not in memberships:
            raise ValueError('Each administrator must match one existing tenant member')
        admins[tenant] = users[0]['Id']
    assignments = {}
    if policy.get('contractorScopePolicy') == 'assign_unscoped_company_to_owner_single_tenant':
        for company in rows['Companies']:
            if company['FamilyId'] is not None:
                continue
            tenants = {tenant for user, tenant in memberships if user == company['UserId']}
            if len(tenants) != 1:
                raise ValueError('Contractor owner needs exactly one tenant; ambiguous defaults require review')
            assignments[company['Id']] = next(iter(tenants))
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
            uploader, reason = admins[scope[0]['sourceOwner']], 'reviewed_tenant_admin_fallback'
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
    def assignment_resolves(finding):
        if finding['code'] != 'contract_contractor_scope_mismatch':
            return False
        contract = idx['Contracts'][finding['sourceId']]
        return contract['FamilyId'] is not None and assignments.get(contract['CompanyId']) == contract['FamilyId']
    # Assignment must not break a previously valid private or differently scoped contract.
    for contract in rows['Contracts']:
        if contract['CompanyId'] in assignments and contract['FamilyId'] != assignments[contract['CompanyId']]:
            raise ValueError('Default tenant assignment conflicts with a contract scope')
    remaining = [f for f in report['findings'] if f['code'] not in resolved and not assignment_resolves(f)]
    if any(f['severity'] in ('blocker', 'review') for f in remaining):
        raise ValueError('Unresolved findings still block local import')
    return {'format': 'qontract-local-import-plan', 'version': 1,
            'sourceInstance': manifest['sourceInstance'], 'exportId': manifest['exportId'],
            'packagePath': str(package.resolve()), 'manifestSha256': dry_run.digest(package / 'manifest.json'),
            'rawRecords': rows, 'displayNames': names, 'tenantAdminSourceIds': admins,
            'tenantAdminSourceId': next(iter(admins.values())) if len(set(admins.values())) == 1 else None,
            'contractorTenantAssignments': [{'sourceCompanyId': company, 'tenantSourceId': tenant}
                                           for company, tenant in sorted(assignments.items())],
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
