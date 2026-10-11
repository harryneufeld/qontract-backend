using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Qontract.Core.Security;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.LocalMigration;

public static class LocalFixtureImporter
{
    private static string? Text(JsonElement r, string key) => r.GetProperty(key).ValueKind == JsonValueKind.Null ? null : r.GetProperty(key).GetString();
    private static string Id(JsonElement r) => Text(r, "Id")!;
    private static string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }
    private static string Member(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Absolute package member");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package escape");
        for (var entry = new FileInfo(path) as FileSystemInfo; entry is not null && entry.FullName != root; entry = entry is FileInfo f ? f.Directory : ((DirectoryInfo)entry).Parent)
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked package member");
        return path;
    }

    public static async Task<object> ImportAsync(string planPath, string directory)
    {
        using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(planPath));
        var plan = parsed.RootElement;
        if (Text(plan, "format") != "qontract-local-import-plan" || plan.GetProperty("version").GetInt32() != 1
            || !plan.GetProperty("readyForLocalImport").GetBoolean() || plan.GetProperty("productionApproved").GetBoolean())
            throw new InvalidDataException("Not a local reviewed plan");
        var package = Path.GetFullPath(Text(plan, "packagePath")!);
        if (Hash(Member(package, "manifest.json")) != Text(plan, "manifestSha256")) throw new InvalidDataException("Changed manifest");
        using var manifestDoc = JsonDocument.Parse(await File.ReadAllTextAsync(Member(package, "manifest.json")));
        var manifest = manifestDoc.RootElement;
        if (Text(manifest, "sourceInstance") != Text(plan, "sourceInstance") || Text(manifest, "exportId") != Text(plan, "exportId") || !manifest.GetProperty("complete").GetBoolean())
            throw new InvalidDataException("Wrong source identity");
        var raw = plan.GetProperty("rawRecords");
        foreach (var entity in manifest.GetProperty("entities").EnumerateObject())
        {
            var path = Member(package, Text(entity.Value, "path")!);
            if (Hash(path) != Text(entity.Value, "sha256")) throw new InvalidDataException("Changed entity file");
            var source = JsonNode.Parse(await File.ReadAllTextAsync(path));
            if (!JsonNode.DeepEquals(source, JsonNode.Parse(raw.GetProperty(entity.Name).GetRawText()))
                || raw.GetProperty(entity.Name).GetArrayLength() != entity.Value.GetProperty("count").GetInt32())
                throw new InvalidDataException("Plan/raw mismatch");
        }
        if (manifest.TryGetProperty("selection", out var selection))
        {
            var evidencePath = Member(package, Text(selection, "path")!);
            if (Text(selection, "policy") != "first_test_member_with_owned_contract_and_email_v1"
                || Hash(evidencePath) != Text(selection, "sha256")) throw new InvalidDataException("Changed scope exclusion evidence");
            using var exclusionDoc = JsonDocument.Parse(await File.ReadAllTextAsync(evidencePath));
            var evidence = exclusionDoc.RootElement;
            var excluded = evidence.GetProperty("excludedRecords").EnumerateArray().ToList();
            var entities = manifest.GetProperty("entities").EnumerateObject().Select(e => e.Name).ToHashSet();
            if (Text(evidence, "policy") != Text(selection, "policy") || evidence.GetProperty("productionApproved").GetBoolean()
                || excluded.Count != selection.GetProperty("count").GetInt32()
                || !evidence.GetProperty("originalCounts").EnumerateObject().Select(e => e.Name).ToHashSet().SetEquals(entities)
                || excluded.Any(e => !entities.Contains(Text(e, "entity")!))) throw new InvalidDataException("Invalid scope exclusion evidence");
            foreach (var entity in entities)
                if (raw.GetProperty(entity).GetArrayLength() + excluded.Count(e => Text(e, "entity") == entity)
                    != evidence.GetProperty("originalCounts").GetProperty(entity).GetInt32()) throw new InvalidDataException("Scope counts disagree");
        }
        if (manifest.TryGetProperty("fileResolutions", out var resolutionEntry))
        {
            var path = Member(package, Text(resolutionEntry, "path")!);
            if (Hash(path) != Text(resolutionEntry, "sha256")) throw new InvalidDataException("Changed file resolution evidence");
            using var resolutionDoc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var evidence = resolutionDoc.RootElement;
            var resolutions = evidence.GetProperty("resolutions").EnumerateArray().ToList();
            if (Text(evidence, "format") != "familyplanner-reviewed-file-resolutions" || evidence.GetProperty("version").GetInt32() != 1
                || Text(evidence, "sourceInstance") != Text(manifest, "sourceInstance") || resolutions.Count != resolutionEntry.GetProperty("count").GetInt32()
                || resolutions.Select(r => Text(r, "sourceId")).Distinct().Count() != resolutions.Count) throw new InvalidDataException("Invalid file resolution evidence");
            foreach (var resolution in resolutions)
            {
                var source = raw.GetProperty("DatabaseFiles").EnumerateArray().Single(r => Id(r) == Text(resolution, "sourceId"));
                var binary = source.GetProperty("Binary");
                if (!resolution.GetProperty("approved").GetBoolean() || Text(resolution, "reason") != "reviewed_legacy_filename_encoding"
                    || Text(resolution, "sourcePath") != Text(source, "Path") || Text(resolution, "sourceFileName") != Text(source, "FileName")
                    || Text(resolution, "sha256") != Text(binary, "sha256") || resolution.GetProperty("size").GetInt64() != binary.GetProperty("size").GetInt64())
                    throw new InvalidDataException("File resolution disagrees with source or bytes");
            }
        }
        var sourceUsers = raw.GetProperty("Users").EnumerateArray().ToDictionary(Id);
        var sourceContracts = raw.GetProperty("Contracts").EnumerateArray().ToDictionary(Id);
        var sourceCompanies = raw.GetProperty("Companies").EnumerateArray().ToDictionary(Id);
        var sourceTenants = raw.GetProperty("UserFamilies").EnumerateArray().ToDictionary(Id);
        var memberships = raw.GetProperty("FamilyUserList").EnumerateArray().Select(r => (Text(r, "UsersId"), Text(r, "FamiliesId"))).ToHashSet();
        var policy = plan.GetProperty("reviewedPolicy");
        var admins = sourceTenants.Keys.ToDictionary(t => t, t => plan.TryGetProperty("tenantAdminSourceIds", out var choices)
            ? Text(choices, t)! : Text(plan, "tenantAdminSourceId")!);
        if (plan.TryGetProperty("tenantAdminSourceIds", out var declaredAdmins) && !declaredAdmins.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(sourceTenants.Keys))
            throw new InvalidDataException("Administrator tenant set mismatch");
        foreach (var (tenant, admin) in admins)
        {
            var email = policy.TryGetProperty("tenantAdminEmails", out var emails) ? Text(emails, tenant) : Text(policy, "tenantAdminEmail");
            if (admin is null || !sourceUsers.ContainsKey(admin) || !memberships.Contains((admin, tenant))
                || !string.Equals(Text(sourceUsers[admin], "Email")?.Trim(), email?.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Admin policy mismatch");
        }
        var assignments = new Dictionary<string, string>();
        if (policy.TryGetProperty("contractorScopePolicy", out var scopePolicy)
            && scopePolicy.GetString() == "assign_unscoped_company_to_owner_single_tenant")
        {
            foreach (var company in sourceCompanies.Values.Where(c => Text(c, "FamilyId") is null))
            {
                var choices = memberships.Where(m => m.Item1 == Text(company, "UserId")).Select(m => m.Item2!).Distinct().ToList();
                if (choices.Count != 1) throw new InvalidDataException("Ambiguous contractor default tenant requires review");
                assignments.Add(Id(company), choices.Single());
            }
        }
        var declaredAssignments = plan.TryGetProperty("contractorTenantAssignments", out var assignmentRows)
            ? assignmentRows.EnumerateArray().Select(r => (Text(r, "sourceCompanyId")!, Text(r, "tenantSourceId")!)).ToList() : [];
        if (declaredAssignments.Count != declaredAssignments.Distinct().Count()
            || !assignments.Select(a => (a.Key, a.Value)).ToHashSet().SetEquals(declaredAssignments))
            throw new InvalidDataException("Contractor default tenant policy mismatch");
        foreach (var contract in sourceContracts.Values)
        {
            if (Text(contract, "CompanyId") is not string companyId) continue;
            var company = sourceCompanies[companyId];
            var companyTenant = assignments.GetValueOrDefault(companyId) ?? Text(company, "FamilyId");
            var contractTenant = Text(contract, "FamilyId");
            if (companyTenant != contractTenant || (contractTenant is null && Text(company, "UserId") != Text(contract, "UserId")))
                throw new InvalidDataException("Contractor assignment conflicts with contract scope");
        }
        foreach (var user in sourceUsers.Values)
        {
            var name = Text(user, "UserName");
            var expected = string.IsNullOrWhiteSpace(name) ? Text(user, "Email") : name;
            if (Text(plan.GetProperty("displayNames"), Id(user)) != expected || string.IsNullOrWhiteSpace(expected) || expected.Length > 100)
                throw new InvalidDataException("Name policy mismatch");
        }
        // Reject plan changes to ownership: recompute scopes from every original association.
        foreach (var file in raw.GetProperty("DatabaseFiles").EnumerateArray())
        {
            var scopes = new HashSet<(string, string)>();
            if (Text(file, "UserId") is string owner) scopes.Add(("private", owner));
            if (Text(file, "UserFamilyId") is string family) scopes.Add(("tenant", family));
            var linked = raw.GetProperty("ContractFileList").EnumerateArray().Where(l => Text(l, "FilesId") == Id(file)).Select(l => sourceContracts[Text(l, "ContractsId")!]).ToList();
            foreach (var contract in linked) scopes.Add(Text(contract, "FamilyId") is string t ? ("tenant", t) : ("private", Text(contract, "UserId")!));
            if (scopes.Count != 1) throw new InvalidDataException("Mixed document access");
            var scope = scopes.Single();
            var declared = plan.GetProperty("documentScopes").GetProperty(Id(file));
            if (Text(declared, "kind") != scope.Item1 || Text(declared, "sourceOwner") != scope.Item2) throw new InvalidDataException("Scope policy mismatch");
            var users = linked.Select(c => Text(c, "UserId")!).Distinct().ToList();
            var expectedUploader = Text(file, "UserId") ?? (users.Count == 1 ? users[0] : users.Count == 0 && scope.Item1 == "tenant" ? admins[scope.Item2] : throw new InvalidDataException("Unknown uploader"));
            var uploader = Text(plan.GetProperty("documentUploaders"), Id(file))!;
            if (uploader != expectedUploader || !sourceUsers.ContainsKey(uploader) || (scope.Item1 == "private" ? uploader != scope.Item2 : !memberships.Contains((uploader, scope.Item2))))
                throw new InvalidDataException("Uploader policy mismatch");
            var binary = file.GetProperty("Binary");
            var path = Member(package, Text(binary, "path")!);
            if (Hash(path) != Text(binary, "sha256") || new FileInfo(path).Length != binary.GetProperty("size").GetInt64()) throw new InvalidDataException("Changed bytes");
        }
        var target = Path.GetFullPath(directory);
        var marker = Path.Combine(target, "local-import-plan.sha256");
        var planHash = Hash(planPath);
        if (Directory.Exists(target))
        {
            if (!File.Exists(marker) || (await File.ReadAllTextAsync(marker)).Trim() != planHash) throw new InvalidDataException("Unrelated target or conflicting plan");
        }
        else
        {
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(marker, planHash);
        }
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = Path.Combine(target, "qontract.sqlite"), Pooling = false }.ToString()).Options);
        await db.Database.OpenConnectionAsync();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='LocalImportRun'";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 1)
            {
                using var prior = connection.CreateCommand(); prior.CommandText = "SELECT Fingerprint FROM LocalImportRun WHERE PlanHash=$hash AND PlanJson=$json";
                prior.Parameters.AddWithValue("$hash", planHash); prior.Parameters.AddWithValue("$json", plan.GetRawText());
                var fingerprint = (string?)await prior.ExecuteScalarAsync();
                if (fingerprint is not null)
                {
                    if (fingerprint != await Fingerprint(connection)) throw new InvalidDataException("Target edits conflict with replay");
                    foreach (var document in await db.Documents.AsNoTracking().ToListAsync())
                    {
                        var file = raw.GetProperty("DatabaseFiles").EnumerateArray().Single(r => Guid.Parse(Id(r)).ToString() + ".bin" == Path.GetFileName(document.FilePath));
                        if (Hash(document.FilePath) != Text(file.GetProperty("Binary"), "sha256")) throw new InvalidDataException("Target bytes changed");
                    }
                    return new { replay = true, inserted = 0, reconciled = true };
                }
            }
        }
        await db.Database.MigrateAsync();
        if (await db.Users.AnyAsync() || await db.Tenants.AnyAsync() || await db.Contracts.AnyAsync()) throw new InvalidDataException("Nonempty target");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tenants = new Dictionary<string, Tenant>(); var usersTarget = new Dictionary<string, User>();
        var companies = new Dictionary<string, Contractor>(); var contracts = new Dictionary<string, Contract>(); var documents = new Dictionary<string, Document>();
        foreach (var row in raw.GetProperty("UserFamilies").EnumerateArray()) { var t = new Tenant { Name = Text(row, "FamilyName")!, Type = TenantType.Family, IsActive = true }; tenants.Add(Id(row), t); db.Tenants.Add(t); }
        await db.SaveChangesAsync();
        foreach (var row in sourceUsers.Values)
        {
            var u = new User { Name = Text(plan.GetProperty("displayNames"), Id(row))!, Email = Text(row, "Email")!, Role = UserRole.User, IsActive = true,
                RequiresPasswordSetup = true, PasswordHash = Passwords.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))) };
            usersTarget.Add(Id(row), u); db.Users.Add(u);
        }
        await db.SaveChangesAsync();
        foreach (var row in raw.GetProperty("FamilyUserList").EnumerateArray()) db.TenantMemberships.Add(new TenantMembership { UserId = usersTarget[Text(row, "UsersId")!].Id,
            TenantId = tenants[Text(row, "FamiliesId")!].Id, Role = Text(row, "UsersId") == admins[Text(row, "FamiliesId")!] ? UserRole.TenantAdmin : UserRole.User, IsActive = true });
        foreach (var row in raw.GetProperty("Companies").EnumerateArray())
        {
            var t = assignments.GetValueOrDefault(Id(row)) ?? Text(row, "FamilyId"); var c = new Contractor { Name = Text(row, "CompanyName")!, Type = ContractorType.Company, IsActive = true,
                TenantId = t is null ? null : tenants[t].Id, OwnerUserId = t is null ? usersTarget[Text(row, "UserId")!].Id : null,
                UserId = usersTarget[Text(row, "UserId")!].Id, Email = Text(row, "Email"), Phone = Text(row, "Phone"), Website = Text(row, "Website"), Description = Text(row, "Description"), Notes = Text(row, "Notes") };
            companies.Add(Id(row), c); db.Contractors.Add(c);
        }
        await db.SaveChangesAsync();
        foreach (var row in sourceContracts.Values)
        {
            var t = Text(row, "FamilyId"); var c = new Contract { Title = Text(row, "ContractName")!, Description = Text(row, "Description"), ContractNumber = Text(row, "ContractNumber"),
                Iban = Text(row, "Iban"), Notes = Text(row, "Notes"), Type = (ContractType)row.GetProperty("Type").GetInt32(), Status = ContractStatus.Active,
                IsActive = row.GetProperty("IsActive").GetInt32() != 0, StartDate = Text(row, "StartDate"), EndDate = Text(row, "EndDate"), MinimumContractDate = Text(row, "MinContractDate"),
                Value = Text(row, "Price"), Currency = null, BillingPeriod = (BillingPeriod)row.GetProperty("Recurring").GetInt32(),
                TenantId = t is null ? null : tenants[t].Id, OwnerUserId = t is null ? usersTarget[Text(row, "UserId")!].Id : null,
                ContractorId = Text(row, "CompanyId") is string p ? companies[p].Id : null };
            contracts.Add(Id(row), c); db.Contracts.Add(c);
        }
        await db.SaveChangesAsync();
        var sourceAddresses = raw.GetProperty("Addresses").EnumerateArray().ToDictionary(Id);
        var addresses = new Dictionary<string, Address>();
        var addressSources = new Dictionary<string, string>();
        foreach (var entity in new[] { "Users", "UserFamilies", "Companies" }) foreach (var row in raw.GetProperty(entity).EnumerateArray())
        {
            if (Text(row, "AddressId") is not string id) continue;
            var source = sourceAddresses[id]; var address = new Address { Street = Text(source, "StreetName"), HouseNumber = Text(source, "HouseNumber"), City = Text(source, "CityName"), Zip = Text(source, "ZipCode"), Country = Text(source, "CountryName"), IsActive = true,
                EntityType = entity == "Users" ? EntityType.Person : entity == "UserFamilies" ? EntityType.Tenant : EntityType.Contractor,
                UserId = entity == "Users" ? usersTarget[Id(row)].Id : null, TenantId = entity == "UserFamilies" ? tenants[Id(row)].Id : null, ContractorId = entity == "Companies" ? companies[Id(row)].Id : null };
            addresses.Add(entity + ":" + Id(row), address); db.Addresses.Add(address);
            addressSources.Add(entity + ":" + Id(row), id);
        }
        var storage = Path.Combine(target, "documents"); Directory.CreateDirectory(storage);
        foreach (var row in raw.GetProperty("DatabaseFiles").EnumerateArray())
        {
            var binary = row.GetProperty("Binary"); var path = Path.Combine(storage, Guid.Parse(Id(row)).ToString() + ".bin");
            if (File.Exists(path)) { if (Hash(path) != Text(binary, "sha256")) throw new InvalidDataException("Conflicting target file"); }
            else File.Copy(Member(package, Text(binary, "path")!), path, false);
            if (Hash(path) != Text(binary, "sha256")) throw new InvalidDataException("Copied bytes mismatch");
            var uploader = usersTarget[Text(plan.GetProperty("documentUploaders"), Id(row))!]; var scope = plan.GetProperty("documentScopes").GetProperty(Id(row));
            var linked = raw.GetProperty("ContractFileList").EnumerateArray().Where(l => Text(l, "FilesId") == Id(row)).Select(l => Text(l, "ContractsId")!).Order().ToList();
            var d = new Document { FileName = Text(row, "FileName")!, FileType = Text(row, "MimeType") ?? "application/octet-stream", FilePath = path,
                FileSize = binary.GetProperty("size").GetInt64(), UploadedBy = uploader.Email, UserId = uploader.Id, Scope = Text(scope, "kind") == "tenant" ? DocumentScope.Tenant : DocumentScope.Personal,
                TenantId = Text(scope, "kind") == "tenant" ? tenants[Text(scope, "sourceOwner")!].Id : null,
                ContractId = linked.Count > 0 ? contracts[linked[0]].Id : null, Version = 1, Description = Text(row, "Description"), Category = Text(row, "Category"),
                DocumentDate = Text(row, "Date") is string date ? DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture) : null };
            documents.Add(Id(row), d); db.Documents.Add(d);
        }
        await db.SaveChangesAsync();
        foreach (var link in raw.GetProperty("ContractFileList").EnumerateArray())
        {
            var d = documents[Text(link, "FilesId")!]; var c = contracts[Text(link, "ContractsId")!];
            if (d.ContractId != c.Id) db.DocumentLinks.Add(new DocumentLink { DocumentId = d.Id, ContractId = c.Id });
        }
        await db.SaveChangesAsync();
        if (await db.Users.CountAsync() != sourceUsers.Count || await db.Tenants.CountAsync() != tenants.Count
            || await db.Contractors.CountAsync() != companies.Count || await db.Contracts.CountAsync() != contracts.Count
            || await db.Addresses.CountAsync() != addresses.Count || await db.Documents.CountAsync() != documents.Count
            || await db.TenantMemberships.CountAsync() != memberships.Count
            || await db.Documents.CountAsync(d => d.ContractId != null) + await db.DocumentLinks.CountAsync() != raw.GetProperty("ContractFileList").GetArrayLength())
            throw new InvalidDataException("Reconciliation counts disagree");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE LocalImportRun (PlanHash TEXT PRIMARY KEY, Fingerprint TEXT NOT NULL, PlanJson TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE LocalSourceLedger (Entity TEXT NOT NULL, SourceId TEXT NOT NULL, TargetReference TEXT, RawJson TEXT NOT NULL, PRIMARY KEY(Entity, SourceId))");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE LocalContractorScopeLedger (SourceCompanyId TEXT PRIMARY KEY, TenantSourceId TEXT NOT NULL, TargetContractorId INTEGER NOT NULL)");
        foreach (var (company, tenant) in assignments)
            await db.Database.ExecuteSqlRawAsync("INSERT INTO LocalContractorScopeLedger VALUES ({0}, {1}, {2})", company, tenant, companies[company].Id);
        foreach (var table in raw.EnumerateObject()) foreach (var row in table.Value.EnumerateArray())
        {
            string key = row.TryGetProperty("Id", out var id) ? id.GetString()! : row.GetRawText();
            string? reference = table.Name switch { "Users" => usersTarget[key].Id.ToString(), "UserFamilies" => tenants[key].Id.ToString(), "Companies" => companies[key].Id.ToString(), "Contracts" => contracts[key].Id.ToString(), "DatabaseFiles" => documents[key].Id.ToString(),
                "Addresses" => JsonSerializer.Serialize(addresses.Where(a => addressSources[a.Key] == key).Select(a => a.Value.Id)), _ => null };
            await db.Database.ExecuteSqlRawAsync("INSERT INTO LocalSourceLedger VALUES ({0}, {1}, {2}, {3})", table.Name, key, reference!, row.GetRawText());
        }
        var fingerprintFinal = await Fingerprint(connection, (SqliteTransaction)transaction.GetDbTransaction());
        await db.Database.ExecuteSqlRawAsync("INSERT INTO LocalImportRun VALUES ({0}, {1}, {2})", planHash, fingerprintFinal, plan.GetRawText());
        await transaction.CommitAsync();
        return new { replay = false, inserted = raw.EnumerateObject().Sum(t => t.Value.GetArrayLength()), users = usersTarget.Count, tenants = tenants.Count, contracts = contracts.Count, addresses = addresses.Count, documents = documents.Count, reconciled = true };
    }

    private static async Task<string> Fingerprint(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        var data = new List<object>(); var tables = new List<string>();
        using (var cmd = connection.CreateCommand()) { cmd.Transaction = transaction; cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"; using var reader = await cmd.ExecuteReaderAsync(); while (await reader.ReadAsync()) { var name = reader.GetString(0); if (!name.StartsWith("__") && !name.StartsWith("sqlite_") && name != "LocalImportRun") tables.Add(name); } }
        foreach (var table in tables) { using var cmd = connection.CreateCommand(); cmd.Transaction = transaction; cmd.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\" ORDER BY rowid"; using var reader = await cmd.ExecuteReaderAsync(); while (await reader.ReadAsync()) { var values = new object?[reader.FieldCount]; for (int i = 0; i < values.Length; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i); data.Add(new { table, values }); } }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
    }
}
