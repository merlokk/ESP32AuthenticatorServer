using System.Net;
using AuthServer.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.IntegrationTests;

// UC-8: a user removes one of their devices, an admin any user's; a removed device signs in no more
// and its serial may register again.
public sealed class DeviceRemovalTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private Task<Guid> DeviceIdAsync(TestAccount account) =>
        fixture.QueryAsync(context => context.Devices.Where(device => device.UserId == account.Id).Select(device => device.Id).SingleAsync());

    [Fact]
    public async Task A_user_removes_their_device_and_it_signs_in_no_more()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var other = await fixture.CreateUserAsync("bob@example.com");
        var user = await AccessTokens.BearerAsync(fixture.Application, account);
        var id = await DeviceIdAsync(account);

        await (await user.DeleteAsync($"/api/identity/me/devices/{await DeviceIdAsync(other)}"))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "device.not-found");

        (await user.DeleteAsync($"/api/identity/me/devices/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await (await user.GetAsync("/api/identity/me/devices")).JsonAsync())["devices"]!.AsArray().ShouldBeEmpty();
        (await DeviceApi.TokenAsync(fixture.Application.CreateClient(), account.Device, id.ToString())).StatusCode
            .ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
        await (await user.DeleteAsync($"/api/identity/me/devices/{id}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "device.not-found");
        (await fixture.QueryAsync(context => context.Devices.CountAsync(device => device.UserId == other.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task A_removed_device_registers_again()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var user = await AccessTokens.BearerAsync(fixture.Application, account);
        (await user.DeleteAsync($"/api/identity/me/devices/{await DeviceIdAsync(account)}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await user.PutAsync("/api/identity/me/registration-window", null)).EnsureSuccessStatusCode();

        (await DeviceApi.RegisterAsync(fixture.Application.CreateClient(), account.Device, account.Email)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DeviceApi.TokenAsync(fixture.Application.CreateClient(), account.Device, (await DeviceIdAsync(account)).ToString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_admin_removes_a_device_of_a_user()
    {
        await fixture.ResetAsync();
        var admin = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin));
        var target = await fixture.CreateUserAsync("ada@example.com");
        var other = await fixture.CreateUserAsync("bob@example.com");
        var id = await DeviceIdAsync(target);

        await (await admin.DeleteAsync($"/api/identity/users/{other.Id}/devices/{id}"))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "device.not-found");

        (await admin.DeleteAsync($"/api/identity/users/{target.Id}/devices/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await (await admin.GetAsync($"/api/identity/users/{target.Id}/devices")).JsonAsync())["devices"]!.AsArray().ShouldBeEmpty();
        (await DeviceApi.TokenAsync(fixture.Application.CreateClient(), target.Device, id.ToString())).StatusCode
            .ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
        (await fixture.FindUserAsync(target.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Only_an_admin_removes_another_user_s_device()
    {
        await fixture.ResetAsync();
        var target = await fixture.CreateUserAsync("ada@example.com");
        var user = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("bob@example.com"));

        (await user.DeleteAsync($"/api/identity/users/{target.Id}/devices/{await DeviceIdAsync(target)}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.Application.CreateClient().DeleteAsync($"/api/identity/me/devices/{await DeviceIdAsync(target)}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.QueryAsync(context => context.Devices.CountAsync(device => device.UserId == target.Id))).ShouldBe(1);
    }
}
