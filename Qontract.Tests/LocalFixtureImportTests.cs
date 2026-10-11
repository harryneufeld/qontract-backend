using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Qontract.LocalMigration;

namespace Qontract.Tests;

public sealed class LocalFixtureImportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "qontract-import-" + Guid.NewGuid().ToString("N"));
    private string Target => Path.Combine(root, "target");
    private string Plan => Path.Combine(root, "plan.json");
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static Dictionary<string, object?> Row(string fields, params (string Key, object? Value)[] values)
    {
        var row = fields.Split(' ').ToDictionary(f => f, _ => (object?)null);
        foreach (var (key, value) in values) row[key] = value;
        return row;
    }
    private void Fixture()
    {
        Directory.CreateDirectory(root);
        var package = Path.Combine(root, "package"); Directory.CreateDirectory(package); Directory.CreateDirectory(Path.Combine(package, "files"));
        string u = Guid.NewGuid().ToString(), t = Guid.NewGuid().ToString(), c = Guid.NewGuid().ToString(), a = Guid.NewGuid().ToString();
        string personal = Guid.NewGuid().ToString(), shared = Guid.NewGuid().ToString();
        var data = new Dictionary<string, object[]>
        {
            ["Users"] = [Row("Id UserName Email AddressId Created Modified", ("Id", u), ("Email", "admin@example.test"), ("AddressId", a))],
            ["UserFamilies"] = [Row("Id FamilyName AddressId Created Modified", ("Id", t), ("FamilyName", "Tenant"))],
            ["Companies"] = [],
            ["Addresses"] = [Row("Id StreetName HouseNumber ZipCode CityName CountryName Created Modified", ("Id", a), ("StreetName", "  Straße\n"), ("HouseNumber", "12a"))],
            ["Contracts"] = [Row("Id ContractName Description ContractNumber StartDate EndDate MinContractDate FamilyId UserId CompanyId Price Recurring Iban Notes IsActive Type Created Modified",
                ("Id", c), ("ContractName", "Personal"), ("UserId", u), ("Price", "12.3400"), ("Recurring", 3), ("Type", 0), ("IsActive", 1))],
            ["DatabaseFiles"] = [],
            ["FamilyUserList"] = [Row("UsersId FamiliesId", ("UsersId", u), ("FamiliesId", t))],
            ["ContractFileList"] = [Row("ContractsId FilesId", ("ContractsId", c), ("FilesId", personal))]
        };
        var files = new List<object>();
        foreach (var id in new[] { personal, shared })
        {
            var blob = Path.Combine(package, "files", id + ".bin"); File.WriteAllBytes(blob, [1, 2, 3]);
            files.Add(Row("Id FileName Description Date Path Category MimeType Size UserId UserFamilyId Created Modified Binary",
                ("Id", id), ("FileName", "proof.pdf"), ("MimeType", "application/pdf"), ("Size", 3), ("UserFamilyId", id == shared ? t : null),
                ("Binary", new { path = "files/" + id + ".bin", sha256 = Hash(blob), size = 3 })));
        }
        data["DatabaseFiles"] = files.ToArray();
        var entries = new Dictionary<string, object>();
        foreach (var (name, records) in data)
        {
            var path = Path.Combine(package, name + ".json"); File.WriteAllText(path, JsonSerializer.Serialize(records));
            entries[name] = new { path = name + ".json", count = records.Length, sha256 = Hash(path) };
        }
        var exportId = Guid.NewGuid().ToString();
        File.WriteAllText(Path.Combine(package, "manifest.json"), JsonSerializer.Serialize(new { sourceInstance = "fixture", exportId, complete = true, entities = entries }));
        File.WriteAllText(Plan, JsonSerializer.Serialize(new
        {
            format = "qontract-local-import-plan", version = 1, readyForLocalImport = true, productionApproved = false,
            sourceInstance = "fixture", exportId, packagePath = package, manifestSha256 = Hash(Path.Combine(package, "manifest.json")),
            rawRecords = data, tenantAdminSourceId = u, reviewedPolicy = new { tenantAdminEmail = "admin@example.test" },
            displayNames = new Dictionary<string, string> { [u] = "admin@example.test" },
            documentUploaders = new Dictionary<string, string> { [personal] = u, [shared] = u },
            documentScopes = new Dictionary<string, object> { [personal] = new { kind = "private", sourceOwner = u }, [shared] = new { kind = "tenant", sourceOwner = t } }
        }));
    }
    private ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={Path.Combine(Target, "qontract.sqlite")};Pooling=False").Options);

    private void DefaultTenantFixture(bool ambiguous = false, bool tampered = false)
    {
        Fixture();
        var plan = JsonNode.Parse(File.ReadAllText(Plan))!;
        var raw = plan["rawRecords"]!;
        string user = raw["Users"]![0]!["Id"]!.GetValue<string>();
        string tenant = raw["UserFamilies"]![0]!["Id"]!.GetValue<string>();
        string company = Guid.NewGuid().ToString();
        raw["Companies"]!.AsArray().Add(JsonSerializer.SerializeToNode(Row("Id CompanyName FamilyId UserId AddressId Email Phone Website Description Notes Created Modified",
            ("Id", company), ("CompanyName", "Provider"), ("UserId", user))));
        raw["Contracts"]![0]!["FamilyId"] = tenant;
        raw["Contracts"]![0]!["CompanyId"] = company;
        raw["DatabaseFiles"]![0]!["UserFamilyId"] = tenant;
        var document = raw["DatabaseFiles"]![0]!["Id"]!.GetValue<string>();
        plan["documentScopes"]![document]!["kind"] = "tenant";
        plan["documentScopes"]![document]!["sourceOwner"] = tenant;
        plan["reviewedPolicy"]!["contractorScopePolicy"] = "assign_unscoped_company_to_owner_single_tenant";
        plan["contractorTenantAssignments"] = JsonSerializer.SerializeToNode(new[] { new { sourceCompanyId = company, tenantSourceId = tampered ? Guid.NewGuid().ToString() : tenant } });
        if (ambiguous)
        {
            string other = Guid.NewGuid().ToString();
            raw["UserFamilies"]!.AsArray().Add(JsonSerializer.SerializeToNode(Row("Id FamilyName AddressId Created Modified", ("Id", other), ("FamilyName", "Other"))));
            raw["FamilyUserList"]!.AsArray().Add(JsonSerializer.SerializeToNode(Row("UsersId FamiliesId", ("UsersId", user), ("FamiliesId", other))));
        }
        string package = plan["packagePath"]!.GetValue<string>();
        var manifestPath = Path.Combine(package, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        foreach (var entity in raw.AsObject())
        {
            var path = Path.Combine(package, entity.Key + ".json"); File.WriteAllText(path, entity.Value!.ToJsonString());
            manifest["entities"]![entity.Key]!["sha256"] = Hash(path);
            manifest["entities"]![entity.Key]!["count"] = entity.Value.AsArray().Count;
        }
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        plan["manifestSha256"] = Hash(manifestPath);
        File.WriteAllText(Plan, plan.ToJsonString());
    }

    [Fact]
    public async Task DefaultTenantAssignmentPreservesOneContractorAndSourceOwnership()
    {
        DefaultTenantFixture();
        await LocalFixtureImporter.ImportAsync(Plan, Target);
        await using (var db = Db())
        {
            var contractor = await db.Contractors.SingleAsync();
            contractor.TenantId.Should().Be((await db.Tenants.SingleAsync()).Id);
            contractor.OwnerUserId.Should().BeNull();
            (await db.Contracts.SingleAsync()).ContractorId.Should().Be(contractor.Id);
            (await db.Addresses.CountAsync()).Should().Be(1);
            await db.Database.OpenConnectionAsync();
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT RawJson FROM LocalSourceLedger WHERE Entity='Companies'";
            using var source = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            source.RootElement.GetProperty("FamilyId").ValueKind.Should().Be(JsonValueKind.Null);
        }
        JsonSerializer.Serialize(await LocalFixtureImporter.ImportAsync(Plan, Target)).Should().Contain("\"inserted\":0");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AmbiguousOrTamperedDefaultTenantIsRejectedBeforeCreatingTarget(bool ambiguous, bool tampered)
    {
        DefaultTenantFixture(ambiguous, tampered);
        var act = () => LocalFixtureImporter.ImportAsync(Plan, Target);
        await act.Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task ImportAndReplay_PreserveValuesRolesScopeAndCredentialSetup()
    {
        Fixture();
        await LocalFixtureImporter.ImportAsync(Plan, Target);
        await using (var db = Db())
        {
            var user = await db.Users.SingleAsync(); user.Name.Should().Be(user.Email); user.RequiresPasswordSetup.Should().BeTrue(); user.TenantId.Should().BeNull();
            (await db.TenantMemberships.SingleAsync()).Role.Should().Be(UserRole.TenantAdmin);
            (await db.Contracts.SingleAsync()).Value.Should().Be("12.3400");
            (await db.Addresses.SingleAsync()).Street.Should().Be("  Straße\n");
            var files = await db.Documents.ToListAsync(); files.Should().HaveCount(2);
            files.Single(f => f.Scope == DocumentScope.Tenant).ContractId.Should().BeNull();
            files.Single(f => f.Scope == DocumentScope.Tenant).TenantId.Should().NotBeNull();
            files.Single(f => f.Scope == DocumentScope.Personal).TenantId.Should().BeNull();
            files.Should().OnlyContain(f => f.UserId == user.Id && f.UploadedBy == user.Email);
            (await db.AuthSessions.CountAsync()).Should().Be(0);
        }
        var replay = JsonSerializer.Serialize(await LocalFixtureImporter.ImportAsync(Plan, Target));
        replay.Should().Contain("\"replay\":true").And.Contain("\"inserted\":0");
    }

    [Fact]
    public async Task ReplayRejectsEditedTarget()
    {
        Fixture(); await LocalFixtureImporter.ImportAsync(Plan, Target);
        await using (var db = Db()) { var contract = await db.Contracts.SingleAsync(); contract.Title = "User edit"; await db.SaveChangesAsync(); }
        var act = () => LocalFixtureImporter.ImportAsync(Plan, Target);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ReplayRejectsChangedTargetBytes()
    {
        Fixture(); await LocalFixtureImporter.ImportAsync(Plan, Target);
        await using (var db = Db())
        {
            var file = await db.Documents.FirstAsync(); File.WriteAllBytes(file.FilePath, [9, 9, 9]);
        }
        var act = () => LocalFixtureImporter.ImportAsync(Plan, Target);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task TamperedPackageAndUnrelatedDirectoryAreRejected()
    {
        Fixture(); Directory.CreateDirectory(Target);
        var act = () => LocalFixtureImporter.ImportAsync(Plan, Target);
        await act.Should().ThrowAsync<InvalidDataException>();
        File.WriteAllText(Path.Combine(root, "package", "Users.json"), "[]");
        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Path.Combine(Target, "qontract.sqlite")).Should().BeFalse();
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(root);
        if (!resolved.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(resolved).StartsWith("qontract-import-")) throw new InvalidOperationException("Unexpected fixture cleanup path");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
