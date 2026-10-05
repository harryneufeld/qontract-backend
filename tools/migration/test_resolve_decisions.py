import unittest
import uuid

import resolve_decisions
import test_dry_run


class DecisionTests(unittest.TestCase):
    def setUp(self):
        self.fixture = test_dry_run.DryRunTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.user, self.tenant, self.file = (str(uuid.uuid4()) for _ in range(3))
        f = self.fixture
        f.row('Users', Id=self.user, UserName=None, Email='admin@example.test')
        f.row('UserFamilies', Id=self.tenant, FamilyName='Tenant')
        f.row('FamilyUserList', UsersId=self.user, FamiliesId=self.tenant)
        self.policy = {'sourceInstance': 'fixture', 'displayNamePolicy': 'use_original_email_when_source_name_blank',
                       'tenantAdminEmail': 'admin@example.test', 'documentUploaderPolicy': 'unique_contract_user_else_tenant_admin'}

    def test_blank_name_and_admin_choice_resolve_without_source_changes(self):
        self.fixture.package()
        plan = resolve_decisions.resolve(self.fixture.root, self.policy)
        self.assertEqual(plan['displayNames'][self.user], 'admin@example.test')
        self.assertIsNone(plan['rawRecords']['Users'][0]['UserName'])
        self.assertEqual(plan['tenantAdminSourceId'], self.user)
        self.assertFalse(plan['productionApproved'])

    def test_admin_must_be_an_existing_member_and_policy_must_match_source(self):
        self.fixture.rows['FamilyUserList'] = []
        self.fixture.package()
        with self.assertRaises(ValueError):
            resolve_decisions.resolve(self.fixture.root, self.policy)
        self.policy['sourceInstance'] = 'other'
        with self.assertRaises(ValueError):
            resolve_decisions.resolve(self.fixture.root, self.policy)

    def add_document(self):
        f = self.fixture
        path = f.root / 'file.bin'
        path.write_bytes(b'proof')
        import dry_run
        return f.row('DatabaseFiles', Id=self.file, FileName='proof.pdf', Size=5, UserFamilyId=self.tenant,
                     Binary={'path': path.name, 'sha256': dry_run.digest(path), 'size': 5})

    def test_standalone_tenant_document_uses_admin_but_stays_shared(self):
        self.add_document()
        self.fixture.package()
        plan = resolve_decisions.resolve(self.fixture.root, self.policy)
        self.assertEqual(plan['documentUploaders'][self.file], self.user)
        self.assertEqual(plan['uploaderProvenance'][self.file], 'reviewed_tenant_admin_fallback')
        self.assertEqual(plan['documentScopes'][self.file], {'kind': 'tenant', 'sourceOwner': self.tenant})

    def test_multiple_linked_users_do_not_silently_choose_admin(self):
        self.add_document()
        other = str(uuid.uuid4())
        self.fixture.row('Users', Id=other, UserName='Other', Email='other@example.test')
        self.fixture.row('FamilyUserList', UsersId=other, FamiliesId=self.tenant)
        for owner in (self.user, other):
            contract = str(uuid.uuid4())
            self.fixture.row('Contracts', Id=contract, UserId=owner, FamilyId=self.tenant, ContractName='Shared', Recurring=3, Type=0)
            self.fixture.row('ContractFileList', ContractsId=contract, FilesId=self.file)
        self.fixture.package()
        with self.assertRaisesRegex(ValueError, 'different users'):
            resolve_decisions.resolve(self.fixture.root, self.policy)


if __name__ == '__main__':
    unittest.main()
