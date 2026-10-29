using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AuthServer.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.IntegrationTests;

// UC-8: a user names their devices, an admin any user's; an empty name clears it.
public sealed class DeviceNameTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private Task<Guid> DeviceIdAsync(TestAccount account) =>
        fixture.QueryAsync(context => context.Devices.Where(device => device.UserId == account.Id).Select(device => device.Id).SingleAsync());

    private static async Task<JsonNode?> NameAsync(HttpClient client, string devicesPath) =>
        (await (await client.GetAsync(devicesPath)).JsonAsync())["devices"]!.AsArray().Single()!["name"];

    [Fact]
    public async Task A_user_names_their_device_and_clears_the_name()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var user = await AccessTokens.BearerAsync(fixture.Application, account);
        var path = $"/api/identity/me/devices/{await DeviceIdAsync(account)}";

        (await NameAsync(user, "/api/identity/me/devices")).ShouldBeNull();

        (await user.PutAsJsonAsync(path, new { name = "  Kitchen X4  " })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NameAsync(user, "/api/identity/me/devices"))!.GetValue<string>().ShouldBe("Kitchen X4");

        (await user.PutAsJsonAsync(path, new { name = "" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NameAsync(user, "/api/identity/me/devices")).ShouldBeNull();
    }

    [Fact]
    public async Task A_name_is_at_most_64_characters_and_another_user_s_device_is_not_found()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var other = await fixture.CreateUserAsync("bob@example.com");
        var user = await AccessTokens.BearerAsync(fixture.Application, account);

        await (await user.PutAsJsonAsync($"/api/identity/me/devices/{await DeviceIdAsync(account)}", new { name = new string('x', 65) }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "device.name-invalid");
        await (await user.PutAsJsonAsync($"/api/identity/me/devices/{await DeviceIdAsync(other)}", new { name = "Mine" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "device.not-found");
    }

    [Fact]
    public async Task An_admin_names_a_device_of_a_user()
    {
        await fixture.ResetAsync();
        var admin = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin));
        var target = await fixture.CreateUserAsync("ada@example.com");
        var other = await fixture.CreateUserAsync("bob@example.com");
        var id = await DeviceIdAsync(target);

        await (await admin.PutAsJsonAsync($"/api/identity/users/{other.Id}/devices/{id}", new { name = "Office" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "device.not-found");
        (await admin.PutAsJsonAsync($"/api/identity/users/{target.Id}/devices/{id}", new { name = "Office" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await NameAsync(admin, $"/api/identity/users/{target.Id}/devices"))!.GetValue<string>().ShouldBe("Office");
        (await NameAsync(await AccessTokens.BearerAsync(fixture.Application, target), "/api/identity/me/devices"))!
            .GetValue<string>().ShouldBe("Office");
    }

    [Fact]
    public async Task Only_an_admin_names_another_user_s_device()
    {
        await fixture.ResetAsync();
        var target = await fixture.CreateUserAsync("ada@example.com");
        var user = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("bob@example.com"));

        (await user.PutAsJsonAsync($"/api/identity/users/{target.Id}/devices/{await DeviceIdAsync(target)}", new { name = "Mine" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
