using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Qontract.Application.Models;
using Qontract.Application.Notifications;
using Xunit;

namespace Qontract.Tests;

[Collection("QontractApiStorage")]
public class NotificationTests(QontractWebApplicationFactory factory) : IClassFixture<QontractWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    private static string In(int days) => DateTime.UtcNow.Date.AddDays(days).ToString("yyyy-MM-dd");

    private async Task<(HttpClient Client, AuthDTO Auth)> Login(string email = "admin@qontract.com")
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    private static async Task<ContractDTO> Create(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/contracts", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ContractDTO>(Json))!;
    }

    private async Task Run(int tenantId, string? settings = null)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RecommendationEngine>().RunWorkspaceAsync(tenantId, settings, DateTime.UtcNow);
    }

    private static async Task<List<NotificationDTO>> Inbox(HttpClient client) =>
        (await client.GetFromJsonAsync<List<NotificationDTO>>("/api/notifications", Json))!;

    [Fact]
    public async Task NoticePeriodRoundTripsAndIsValidated()
    {
        var (client, _) = await Login();
        var created = await Create(client, new
        {
            title = "Gym " + Guid.NewGuid(), status = "Active", endDate = In(400),
            noticePeriodValue = 3, noticePeriodUnit = "Months", autoRenews = true, renewalMonths = 12
        });
        created.NoticePeriodValue.Should().Be(3);
        created.NoticePeriodUnit.Should().Be(Qontract.Domain.Models.NoticePeriodUnit.Months);
        created.AutoRenews.Should().BeTrue();
        created.RenewalMonths.Should().Be(12);

        var cleared = await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new { clearNoticePeriod = true, autoRenews = false });
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await cleared.Content.ReadFromJsonAsync<ContractDTO>(Json))!;
        after.NoticePeriodValue.Should().BeNull();
        after.AutoRenews.Should().BeFalse();
        after.RenewalMonths.Should().BeNull();

        (await client.PostAsJsonAsync("/api/contracts", new { title = "Bad", status = "Active", noticePeriodValue = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NoticeDeadlineIsCreatedOnceEscalatesAndResolves()
    {
        var (client, auth) = await Login();
        var tenantId = auth.User!.TenantId;
        var contract = await Create(client, new
        {
            title = "Notice " + Guid.NewGuid(), status = "Active", endDate = In(20),
            noticePeriodValue = 14, noticePeriodUnit = "Days"
        });

        await Run(tenantId);
        await Run(tenantId);
        var notice = (await Inbox(client)).Where(n => n.ContractId == contract.Id && n.Kind == NotificationKinds.NoticeDeadline).ToList();
        notice.Should().ContainSingle();
        notice[0].ContractTitle.Should().Be(contract.Title);
        notice[0].Params.GetProperty("daysLeft").GetInt32().Should().BeInRange(5, 7);
        notice[0].DueOn.Should().NotBeNull();

        // Moving the end date far away withdraws the notification.
        (await client.PutAsJsonAsync($"/api/contracts/{contract.Id}", new { endDate = In(300) })).StatusCode.Should().Be(HttpStatusCode.OK);
        await Run(tenantId);
        (await Inbox(client)).Should().NotContain(n => n.ContractId == contract.Id && n.Kind == NotificationKinds.NoticeDeadline);
    }

    [Fact]
    public async Task AutoRenewingContractRollsToTheNextTerm()
    {
        var (client, auth) = await Login();
        // Term ended 10 days ago and renews yearly; the next deadline is almost a year away, so nothing is due.
        var quiet = await Create(client, new
        {
            title = "Renews " + Guid.NewGuid(), status = "Active", endDate = In(-10),
            noticePeriodValue = 30, noticePeriodUnit = "Days", autoRenews = true, renewalMonths = 12
        });
        // A monthly renewal always has its next deadline within the coming month.
        var monthly = await Create(client, new
        {
            title = "Monthly " + Guid.NewGuid(), status = "Active", endDate = In(-45),
            noticePeriodValue = 7, noticePeriodUnit = "Days", autoRenews = true, renewalMonths = 1
        });
        await Run(auth.User!.TenantId);
        var inbox = await Inbox(client);
        inbox.Should().NotContain(n => n.ContractId == quiet.Id);
        inbox.Should().Contain(n => n.ContractId == monthly.Id && n.Kind == NotificationKinds.NoticeDeadline);
    }

    [Fact]
    public async Task DismissedNotificationsStayDismissedAndOtherUsersCannotTouchThem()
    {
        var (client, auth) = await Login();
        var contract = await Create(client, new { title = "Dismiss " + Guid.NewGuid(), status = "Active", endDate = In(5) });
        await Run(auth.User!.TenantId);
        var notification = (await Inbox(client)).Single(n => n.ContractId == contract.Id && n.Kind == NotificationKinds.EndDate);
        (await client.GetFromJsonAsync<NotificationSummaryDTO>("/api/notifications/summary", Json))!.Unread.Should().BeGreaterThan(0);

        var (other, _) = await Login("admin2@qontract.com");
        (await other.PostAsync($"/api/notifications/{notification.Id}/dismiss", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Inbox(other)).Should().NotContain(n => n.Id == notification.Id);

        (await client.PostAsync($"/api/notifications/{notification.Id}/dismiss", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await Run(auth.User.TenantId);
        (await Inbox(client)).Should().NotContain(n => n.Id == notification.Id);
    }

    [Fact]
    public async Task SnoozeHidesAndReadAllClearsUnread()
    {
        var (client, auth) = await Login();
        var contract = await Create(client, new { title = "Snooze " + Guid.NewGuid(), status = "Active", endDate = In(6) });
        await Run(auth.User!.TenantId);
        var notification = (await Inbox(client)).Single(n => n.ContractId == contract.Id && n.Kind == NotificationKinds.EndDate);

        (await client.PostAsJsonAsync($"/api/notifications/{notification.Id}/snooze", new { days = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/notifications/{notification.Id}/snooze", new { days = 7 })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Inbox(client)).Should().NotContain(n => n.Id == notification.Id);

        (await client.PostAsync("/api/notifications/read-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<NotificationSummaryDTO>("/api/notifications/summary", Json))!.Unread.Should().Be(0);
    }

    [Fact]
    public async Task MutedKindsAreHiddenAndPreferencesAreValidated()
    {
        var (client, auth) = await Login();
        var contract = await Create(client, new { title = "Muted " + Guid.NewGuid(), status = "Active", endDate = In(4) });
        await Run(auth.User!.TenantId);
        (await Inbox(client)).Should().Contain(n => n.ContractId == contract.Id && n.Kind == NotificationKinds.EndDate);

        (await client.PutAsJsonAsync("/api/notifications/preferences", new { digest = "Weekly", mutedKinds = new[] { "bogus" } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var saved = await client.PutAsJsonAsync("/api/notifications/preferences", new { digest = "Weekly", mutedKinds = new[] { NotificationKinds.EndDate } });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Inbox(client)).Should().NotContain(n => n.ContractId == contract.Id && n.Kind == NotificationKinds.EndDate);

        await client.PutAsJsonAsync("/api/notifications/preferences", new { digest = "Off", mutedKinds = Array.Empty<string>() });
        (await client.GetFromJsonAsync<NotificationPreferencesDTO>("/api/notifications/preferences", Json))!.MutedKinds.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkspaceSettingsControlRulesAndAreValidated()
    {
        var (client, auth) = await Login();
        var tenantId = auth.User!.TenantId;
        var contract = await Create(client, new { title = "Rules " + Guid.NewGuid(), status = "Active", endDate = In(6) });

        // Rule disabled by the workspace -> nothing is created.
        var off = """{"notifications":{"rules":{"endDate":{"enabled":false}}}}""";
        await Run(tenantId, off);
        (await Inbox(client)).Should().NotContain(n => n.ContractId == contract.Id && n.Kind == NotificationKinds.EndDate);

        // Custom lead days apply; the default (30, 7) would also fire, so use a lead that cannot be reached.
        var narrow = """{"notifications":{"rules":{"endDate":{"leadDays":[2]}}}}""";
        await Run(tenantId, narrow);
        (await Inbox(client)).Should().NotContain(n => n.ContractId == contract.Id && n.Kind == NotificationKinds.EndDate);

        await Run(tenantId, null);
        (await Inbox(client)).Should().Contain(n => n.ContractId == contract.Id && n.Kind == NotificationKinds.EndDate);

        foreach (var bad in new[]
        {
            """{"notifications":{"rules":{"nope":{}}}}""",
            """{"notifications":{"rules":{"endDate":{"leadDays":[0]}}}}""",
            """{"notifications":{"rules":{"endDate":{"leadDays":[]}}}}""",
            """{"notifications":{"timeZone":"Mars/Base"}}""",
            """{"notifications":{"unassignedGoTo":"everyone"}}""",
            """{"notifications":{"rules":{"priceIncrease":{"minPercent":-1}}}}""",
            """{"notifications":[]}"""
        })
        {
            var update = await client.PutAsJsonAsync($"/api/tenants/{tenantId}", new { settings = bad });
            update.StatusCode.Should().Be(HttpStatusCode.BadRequest, bad);
        }
        var good = await client.PutAsJsonAsync($"/api/tenants/{tenantId}", new
        {
            settings = """{"defaultCurrency":"EUR","notifications":{"timeZone":"Europe/Berlin","rules":{"endDate":{"leadDays":[30,7]}}}}"""
        });
        good.StatusCode.Should().Be(HttpStatusCode.OK, await good.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PriceIncreaseIsFlagged()
    {
        var (client, auth) = await Login();
        var contract = await Create(client, new
        {
            title = "Price " + Guid.NewGuid(), status = "Active", value = "10", billingPeriod = "Monthly", startDate = In(-200)
        });
        var raise = await client.PutAsJsonAsync($"/api/contracts/{contract.Id}", new { value = "12", billingPeriod = "Monthly", priceEffectiveFrom = In(-3) });
        raise.StatusCode.Should().Be(HttpStatusCode.OK, await raise.Content.ReadAsStringAsync());
        await Run(auth.User!.TenantId);
        var hit = (await Inbox(client)).Single(n => n.ContractId == contract.Id && n.Kind == NotificationKinds.PriceIncrease);
        hit.Params.GetProperty("percent").GetDecimal().Should().Be(20m);
    }
}
