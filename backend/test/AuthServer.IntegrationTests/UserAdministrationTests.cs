using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Tests.Passkeys;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.IntegrationTests;

// UC-14 to UC-19.
public sealed class UserAdministrationTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task A_user_is_refused_the_register_and_every_write()
    {
        await fixture.ResetAsync();
        var admin = await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin);
        var user = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("user@example.com"));

        (await user.GetAsync("/api/identity/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.PutAsJsonAsync($"/api/identity/users/{admin.Id}/role", new { role = "user" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.PutAsJsonAsync($"/api/identity/users/{admin.Id}/blocked", new { blocked = true })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.PutAsJsonAsync($"/api/identity/users/{admin.Id}", new { userName = "x", email = "x@example.com" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.DeleteAsync($"/api/identity/users/{admin.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.GetAsync($"/api/identity/users/{admin.Id}/devices")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.Application.CreateClient().GetAsync("/api/identity/users")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_admin_sees_every_account_with_its_facts()
    {
        await fixture.ResetAsync();
        var adminAccount = await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin);
        var admin = await AccessTokens.BearerAsync(fixture.Application, adminAccount);
        using var authenticator = new VirtualAuthenticator();
        await PasskeyCeremony.RegisterAsync(admin, authenticator);
        var invited = await fixture.CreateAccountAsync("invited@example.com", window: TimeSpan.FromHours(3));

        var users = (await (await admin.GetAsync("/api/identity/users")).JsonAsync())["users"]!.AsArray();

        users.Count.ShouldBe(2);
        var adminRow = Row(users, adminAccount.Id);
        adminRow["email"]!.GetValue<string>().ShouldBe("admin@example.com");
        adminRow["role"]!.GetValue<string>().ShouldBe(UserRoles.Admin);
        adminRow["blocked"]!.GetValue<bool>().ShouldBeFalse();
        adminRow["devices"]!.GetValue<int>().ShouldBe(1);
        adminRow["passkeys"]!.GetValue<int>().ShouldBe(1);
        adminRow["lastSignInAt"].ShouldNotBeNull();
        adminRow["registrationOpenUntil"].ShouldBeNull();

        var invitedRow = Row(users, invited);
        invitedRow["devices"]!.GetValue<int>().ShouldBe(0);
        invitedRow["registrationOpenUntil"].ShouldNotBeNull();
        invitedRow["lastSignInAt"].ShouldBeNull();

        var devices = (await (await admin.GetAsync($"/api/identity/users/{adminAccount.Id}/devices")).JsonAsync())["devices"]!.AsArray();
        devices.Single()!["hardwareId"]!.GetValue<string>().ShouldBe(adminAccount.Device.HardwareId);
    }

    [Fact]
    public async Task An_admin_edits_the_name_and_address_and_uniqueness_holds()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var target = await fixture.CreateUserAsync("old@example.com");
        await fixture.CreateAccountAsync("taken@example.com");

        (await admin.PutAsJsonAsync($"/api/identity/users/{target.Id}", new { userName = "renamed", email = "new@example.com" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var user = await fixture.FindUserAsync(target.Id);
        user!.UserName.ShouldBe("renamed");
        user.Email.ShouldBe("new@example.com");
        await AccessTokens.SignInAsync(fixture.CreateBrowser(), "new@example.com", target.Device);

        await (await admin.PutAsJsonAsync($"/api/identity/users/{target.Id}", new { userName = "renamed", email = "TAKEN@example.com" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "user.email-taken");
        await (await admin.PutAsJsonAsync($"/api/identity/users/{Guid.NewGuid()}", new { userName = "x", email = "x@example.com" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "user.not-found");
    }

    [Fact]
    public async Task Promotion_reaches_the_next_renewal_and_demotion_ends_the_sessions()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var target = await fixture.CreateUserAsync("user@example.com");
        var browser = fixture.CreateBrowser();
        var (_, refreshToken) = await AccessTokens.ObtainAsync(browser, target);

        (await admin.PutAsJsonAsync($"/api/identity/users/{target.Id}/role", new { role = "admin" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var renewed = await AccessTokens.RenewAsync(browser, refreshToken);
        renewed.StatusCode.ShouldBe(HttpStatusCode.OK);
        AccessTokens.Read(await AccessTokens.AccessTokenOf(renewed)).GetClaim("role").Value.ShouldBe(UserRoles.Admin);

        (await admin.PutAsJsonAsync($"/api/identity/users/{target.Id}/role", new { role = "user" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await AccessTokens.RenewAsync(browser, refreshToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await (await admin.PutAsJsonAsync($"/api/identity/users/{target.Id}/role", new { role = "owner" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "user.role-invalid");
    }

    [Fact]
    public async Task The_only_admin_cannot_demote_block_or_delete_themselves()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin);
        var admin = await AccessTokens.BearerAsync(fixture.Application, account);

        await (await admin.PutAsJsonAsync($"/api/identity/users/{account.Id}/role", new { role = "user" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "admin.last");
        await (await admin.PutAsJsonAsync($"/api/identity/users/{account.Id}/blocked", new { blocked = true }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "admin.last");
        await (await admin.DeleteAsync($"/api/identity/users/{account.Id}"))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "admin.last");

        (await fixture.FindUserAsync(account.Id))!.Role.ShouldBe(UserRoles.Admin);
    }

    [Fact]
    public async Task A_blocked_admin_does_not_count_as_the_other_admin()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin);
        var admin = await AccessTokens.BearerAsync(fixture.Application, account);
        var second = await fixture.CreateUserAsync("second@example.com", UserRoles.Admin);
        (await admin.PutAsJsonAsync($"/api/identity/users/{second.Id}/blocked", new { blocked = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await (await admin.PutAsJsonAsync($"/api/identity/users/{account.Id}/role", new { role = "user" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "admin.last");
    }

    [Fact]
    public async Task With_a_second_admin_one_may_step_down()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin);
        var admin = await AccessTokens.BearerAsync(fixture.Application, account);
        await fixture.CreateUserAsync("second@example.com", UserRoles.Admin);

        (await admin.PutAsJsonAsync($"/api/identity/users/{account.Id}/role", new { role = "user" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await fixture.FindUserAsync(account.Id))!.Role.ShouldBe(UserRoles.User);
    }

    [Fact]
    public async Task Two_admins_demoting_each_other_at_once_leave_one_standing()
    {
        await fixture.ResetAsync();
        var first = await fixture.CreateUserAsync("first@example.com", UserRoles.Admin);
        var second = await fixture.CreateUserAsync("second@example.com", UserRoles.Admin);
        var firstClient = await AccessTokens.BearerAsync(fixture.Application, first);
        var secondClient = await AccessTokens.BearerAsync(fixture.Application, second);

        var responses = await Task.WhenAll(
            firstClient.PutAsJsonAsync($"/api/identity/users/{second.Id}/role", new { role = "user" }),
            secondClient.PutAsJsonAsync($"/api/identity/users/{first.Id}/role", new { role = "user" }));

        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        (await fixture.QueryAsync(context => context.Users.CountAsync(user => user.Role == UserRoles.Admin))).ShouldBe(1);
    }

    [Fact]
    public async Task A_blocked_user_cannot_sign_in_or_renew_and_unblocking_restores_sign_in()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var target = await fixture.CreateUserAsync("user@example.com");
        var browser = fixture.CreateBrowser();
        var (_, refreshToken) = await AccessTokens.ObtainAsync(browser, target);

        (await admin.PutAsJsonAsync($"/api/identity/users/{target.Id}/blocked", new { blocked = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await AccessTokens.RenewAsync(browser, refreshToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await Should.ThrowAsync<InvalidOperationException>(() => AccessTokens.SignInAsync(fixture.CreateBrowser(), target.Email, target.Device));

        var users = (await (await admin.GetAsync("/api/identity/users")).JsonAsync())["users"]!.AsArray();
        Row(users, target.Id)["blocked"]!.GetValue<bool>().ShouldBeTrue();

        (await admin.PutAsJsonAsync($"/api/identity/users/{target.Id}/blocked", new { blocked = false })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await AccessTokens.SignInAsync(fixture.CreateBrowser(), target.Email, target.Device);
    }

    [Fact]
    public async Task An_existing_identity_cookie_of_a_blocked_user_issues_no_code()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var target = await fixture.CreateUserAsync("user@example.com");
        var browser = fixture.CreateBrowser();
        await AccessTokens.SignInAsync(browser, target.Email, target.Device);

        await admin.PutAsJsonAsync($"/api/identity/users/{target.Id}/blocked", new { blocked = true });

        var (_, challenge) = AccessTokens.CreatePkcePair();
        var authorize = await browser.GetAsync(AccessTokens.AuthorizeUrl(challenge, "s"));
        authorize.Headers.Location!.ToString().ShouldStartWith(TestHost.PublicOrigin + "/sign-in");
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_devices_and_ends_their_sessions()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var target = await fixture.CreateUserAsync("user@example.com");
        var browser = fixture.CreateBrowser();
        var (_, refreshToken) = await AccessTokens.ObtainAsync(browser, target);
        var clientId = await fixture.QueryAsync(context => context.Devices.Where(device => device.UserId == target.Id).Select(device => device.Id.ToString()).SingleAsync());

        (await admin.DeleteAsync($"/api/identity/users/{target.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await fixture.FindUserAsync(target.Id)).ShouldBeNull();
        (await fixture.QueryAsync(context => context.Devices.CountAsync(device => device.UserId == target.Id))).ShouldBe(0);
        (await AccessTokens.RenewAsync(browser, refreshToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DeviceApi.TokenAsync(fixture.Application.CreateClient(), target.Device, clientId)).StatusCode
            .ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
        await (await admin.DeleteAsync($"/api/identity/users/{target.Id}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "user.not-found");
    }

    private async Task<HttpClient> AdminAsync() =>
        await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin));

    private static JsonNode Row(JsonArray users, Guid id) =>
        users.Single(row => row!["id"]!.GetValue<string>() == id.ToString())!;
}
