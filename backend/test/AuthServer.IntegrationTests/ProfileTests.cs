using System.Net;
using System.Net.Http.Json;
using AuthServer.Identity.Tests.Passkeys;

namespace AuthServer.IntegrationTests;

// UC-20, UC-21: the signed-in user's own profile.
public sealed class ProfileTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task A_user_changes_their_own_user_name()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);

        (await client.PutAsJsonAsync("/api/identity/me", new { userName = "  ada.lovelace " }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await (await client.GetAsync("/api/identity/me")).JsonAsync())["userName"]!.GetValue<string>().ShouldBe("ada.lovelace");
        (await fixture.FindUserAsync(account.Id))!.Email.ShouldBe("ada@example.com");
    }

    [Fact]
    public async Task A_taken_or_invalid_user_name_is_refused()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var taken = (await fixture.FindUserAsync(await fixture.CreateAccountAsync("taken@example.com")))!.UserName!;
        var before = (await fixture.FindUserAsync(account.Id))!.UserName;
        var client = await AccessTokens.BearerAsync(fixture.Application, account);

        await (await client.PutAsJsonAsync("/api/identity/me", new { userName = taken.ToUpperInvariant() }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "user.name-taken");
        await (await client.PutAsJsonAsync("/api/identity/me", new { userName = "two words" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "user.name-invalid");
        await (await client.PutAsJsonAsync("/api/identity/me", new { userName = "" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "user.name-invalid");

        (await fixture.FindUserAsync(account.Id))!.UserName.ShouldBe(before);
    }

    [Fact]
    public async Task Keeping_the_same_user_name_is_not_a_conflict()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var own = (await fixture.FindUserAsync(account.Id))!.UserName;
        var client = await AccessTokens.BearerAsync(fixture.Application, account);

        (await client.PutAsJsonAsync("/api/identity/me", new { userName = own })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Changing_the_user_name_needs_a_user_token()
    {
        await fixture.ResetAsync();

        (await fixture.Application.CreateClient().PutAsJsonAsync("/api/identity/me", new { userName = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_user_renames_their_passkey()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);
        using var authenticator = new VirtualAuthenticator();
        await PasskeyCeremony.RegisterAsync(client, authenticator, "Old name");
        var id = (await PasskeyCeremony.ListAsync(client))[0].Id;

        (await client.PutAsJsonAsync($"/api/identity/me/passkeys/{id}", new { name = " Laptop " }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await PasskeyCeremony.ListAsync(client))[0].Name.ShouldBe("Laptop");
    }

    [Fact]
    public async Task Renaming_needs_a_name_and_a_passkey_of_ones_own()
    {
        await fixture.ResetAsync();
        var owner = await fixture.CreateUserAsync("owner@example.com");
        var other = await fixture.CreateUserAsync("other@example.com");
        var ownerClient = await AccessTokens.BearerAsync(fixture.Application, owner);
        var otherClient = await AccessTokens.BearerAsync(fixture.Application, other);
        using var authenticator = new VirtualAuthenticator();
        await PasskeyCeremony.RegisterAsync(ownerClient, authenticator, "Mine");
        var id = (await PasskeyCeremony.ListAsync(ownerClient))[0].Id;

        await (await ownerClient.PutAsJsonAsync($"/api/identity/me/passkeys/{id}", new { name = "  " }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.name-invalid");
        await (await otherClient.PutAsJsonAsync($"/api/identity/me/passkeys/{id}", new { name = "Stolen" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "passkey.not-found");
        await (await ownerClient.PutAsJsonAsync("/api/identity/me/passkeys/AAAA", new { name = "x" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "passkey.not-found");

        (await PasskeyCeremony.ListAsync(ownerClient))[0].Name.ShouldBe("Mine");
    }
}
