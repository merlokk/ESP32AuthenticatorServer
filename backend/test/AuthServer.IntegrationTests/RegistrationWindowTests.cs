using System.Net;
using System.Net.Http.Json;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Tests.Devices;

namespace AuthServer.IntegrationTests;

// UC-2, UC-3: invitations and registration windows.
public sealed class RegistrationWindowTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task An_admin_invites_a_user_whose_window_is_a_day_and_who_then_registers_a_device()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();

        var created = await admin.PostAsJsonAsync("/api/identity/users", new { email = "new@example.com", userName = "newbie", role = "user" });

        var body = await created.JsonAsync(HttpStatusCode.Created);
        var until = body["registrationOpenUntil"]!.GetValue<DateTimeOffset>();
        until.ShouldBe(DateTimeOffset.UtcNow.AddDays(1), Tolerance);

        using var device = new VirtualDevice();
        (await DeviceApi.RegisterAsync(fixture.Application.CreateClient(), device, "new@example.com")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var user = await fixture.FindUserAsync(Guid.Parse(body["id"]!.GetValue<string>()));
        user!.UserName.ShouldBe("newbie");
        user.Role.ShouldBe(UserRoles.User);
        user.RegistrationOpenUntil.ShouldBeNull();
    }

    [Fact]
    public async Task An_invitation_refuses_a_taken_address_or_name_and_bad_fields()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        (await admin.PostAsJsonAsync("/api/identity/users", new { email = "a@example.com", userName = "alice", role = "user" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        await (await admin.PostAsJsonAsync("/api/identity/users", new { email = "A@EXAMPLE.COM", userName = "other", role = "user" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "user.email-taken");
        await (await admin.PostAsJsonAsync("/api/identity/users", new { email = "b@example.com", userName = "ALICE", role = "user" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "user.name-taken");
        await (await admin.PostAsJsonAsync("/api/identity/users", new { email = "nope", userName = "bob", role = "user" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "user.email-invalid");
        await (await admin.PostAsJsonAsync("/api/identity/users", new { email = "b@example.com", userName = "b o b", role = "user" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "user.name-invalid");
        await (await admin.PostAsJsonAsync("/api/identity/users", new { email = "b@example.com", userName = "bob", role = "owner" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "user.role-invalid");
    }

    [Fact]
    public async Task An_invitation_is_extended_by_another_day_and_closed_on_request()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var id = await fixture.CreateAccountAsync("late@example.com", window: TimeSpan.FromMinutes(5));

        var extended = await (await admin.PutAsync($"/api/identity/users/{id}/registration-window", null)).JsonAsync();

        extended["registrationOpenUntil"]!.GetValue<DateTimeOffset>().ShouldBe(DateTimeOffset.UtcNow.AddDays(1), Tolerance);

        (await admin.DeleteAsync($"/api/identity/users/{id}/registration-window")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.FindUserAsync(id))!.RegistrationOpenUntil.ShouldBeNull();
    }

    [Fact]
    public async Task An_admin_chooses_an_hour_or_a_day_whatever_the_devices()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var invited = await fixture.CreateAccountAsync("invited@example.com");
        var withDevice = await fixture.CreateUserAsync("user@example.com");

        var hour = await (await admin.PutAsync($"/api/identity/users/{invited}/registration-window?hours=1", null)).JsonAsync();
        var day = await (await admin.PutAsync($"/api/identity/users/{withDevice.Id}/registration-window?hours=24", null)).JsonAsync();

        hour["registrationOpenUntil"]!.GetValue<DateTimeOffset>().ShouldBe(DateTimeOffset.UtcNow.AddHours(1), Tolerance);
        day["registrationOpenUntil"]!.GetValue<DateTimeOffset>().ShouldBe(DateTimeOffset.UtcNow.AddHours(24), Tolerance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25)]
    public async Task A_window_is_1_to_24_hours(int hours)
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var id = await fixture.CreateAccountAsync("invited@example.com");

        await (await admin.PutAsync($"/api/identity/users/{id}/registration-window?hours={hours}", null))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "registration-window.invalid");
        (await fixture.FindUserAsync(id))!.RegistrationOpenUntil.ShouldBeNull();
    }

    [Fact]
    public async Task A_user_with_a_device_opens_an_hour_for_themselves_and_registers_another()
    {
        await fixture.ResetAsync();
        await AdminAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);

        var opened = await (await client.PutAsync("/api/identity/me/registration-window", null)).JsonAsync();

        opened["registrationOpenUntil"]!.GetValue<DateTimeOffset>().ShouldBe(DateTimeOffset.UtcNow.AddHours(1), Tolerance);
        (await (await client.GetAsync("/api/identity/me")).JsonAsync())["registrationOpenUntil"].ShouldNotBeNull();

        using var second = new VirtualDevice();
        (await DeviceApi.RegisterAsync(fixture.Application.CreateClient(), second, account.Email)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var devices = await (await client.GetAsync("/api/identity/me/devices")).JsonAsync();
        devices["devices"]!.AsArray().Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_user_closes_their_own_window()
    {
        await fixture.ResetAsync();
        await AdminAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);
        await client.PutAsync("/api/identity/me/registration-window", null);

        (await client.DeleteAsync("/api/identity/me/registration-window")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await fixture.FindUserAsync(account.Id))!.RegistrationOpenUntil.ShouldBeNull();
    }

    [Fact]
    public async Task A_user_cannot_open_a_window_for_someone_else_or_invite()
    {
        await fixture.ResetAsync();
        var admin = await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin);
        var account = await fixture.CreateUserAsync("user@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);

        (await client.PutAsync($"/api/identity/users/{admin.Id}/registration-window", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/identity/users", new { email = "x@example.com", userName = "x", role = "user" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_window_for_an_unknown_user_is_not_found()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();

        await (await admin.PutAsync($"/api/identity/users/{Guid.NewGuid()}/registration-window", null))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "user.not-found");
    }

    private async Task<HttpClient> AdminAsync() =>
        await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin));
}
