using System.Net;
using System.Net.Http.Json;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Tests.Passkeys;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.IntegrationTests;

// UC-10, UC-12, UC-13.
public sealed class PasskeyTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task A_registered_passkey_is_listed_and_signs_in_to_the_same_kind_of_session()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("keyholder@example.com", UserRoles.Admin);
        var client = await AccessTokens.BearerAsync(fixture.Application, account);
        using var authenticator = new VirtualAuthenticator();

        (await PasskeyCeremony.RegisterAsync(client, authenticator, "YubiKey 5C")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var keys = await PasskeyCeremony.ListAsync(client);
        keys.Count.ShouldBe(1);
        keys[0].Name.ShouldBe("YubiKey 5C");
        keys[0].LastUsedAt.ShouldBeNull();

        var browser = fixture.CreateBrowser();
        var signedIn = await PasskeyCeremony.SignInAsync(browser, authenticator);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.OK, await signedIn.Content.ReadAsStringAsync());

        var (accessToken, refreshToken) = await AccessTokens.ObtainFromCookieAsync(browser);
        var token = AccessTokens.Read(accessToken);
        token.Subject.ShouldBe(account.Id.ToString());
        token.GetClaim("role").Value.ShouldBe(UserRoles.Admin);
        (await AccessTokens.RenewAsync(browser, refreshToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await PasskeyCeremony.ListAsync(client))[0].LastUsedAt.ShouldNotBeNull();
        (await fixture.FindUserAsync(account.Id))!.LastSignInAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_removed_passkey_no_longer_signs_in()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);
        using var authenticator = new VirtualAuthenticator();
        await PasskeyCeremony.RegisterAsync(client, authenticator);
        var id = (await PasskeyCeremony.ListAsync(client))[0].Id;

        (await PasskeyCeremony.RemoveAsync(client, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await PasskeyCeremony.ListAsync(client)).ShouldBeEmpty();
        await (await PasskeyCeremony.SignInAsync(fixture.CreateBrowser(), authenticator))
            .ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "sign-in.refused");
    }

    [Fact]
    public async Task Removing_an_unknown_or_someone_elses_passkey_is_not_found()
    {
        await fixture.ResetAsync();
        var owner = await fixture.CreateUserAsync("owner@example.com");
        var other = await fixture.CreateUserAsync("other@example.com");
        var ownerClient = await AccessTokens.BearerAsync(fixture.Application, owner);
        var otherClient = await AccessTokens.BearerAsync(fixture.Application, other);
        using var authenticator = new VirtualAuthenticator();
        await PasskeyCeremony.RegisterAsync(ownerClient, authenticator);
        var id = (await PasskeyCeremony.ListAsync(ownerClient))[0].Id;

        await (await PasskeyCeremony.RemoveAsync(otherClient, id)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "passkey.not-found");
        await (await PasskeyCeremony.RemoveAsync(otherClient, "AAAA")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "passkey.not-found");

        (await PasskeyCeremony.ListAsync(ownerClient)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_blocked_user_s_passkey_is_refused()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);
        using var authenticator = new VirtualAuthenticator();
        await PasskeyCeremony.RegisterAsync(client, authenticator);
        await fixture.QueryAsync(context => context.Users.Where(user => user.Id == account.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.BlockedAt, DateTimeOffset.UtcNow)));

        await (await PasskeyCeremony.SignInAsync(fixture.CreateBrowser(), authenticator))
            .ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "sign-in.refused");
    }

    [Fact]
    public async Task The_same_authenticator_is_not_registered_twice()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);
        using var authenticator = new VirtualAuthenticator();
        await PasskeyCeremony.RegisterAsync(client, authenticator, "first");

        await (await PasskeyCeremony.RegisterAsync(client, authenticator, "again"))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.already-registered");
    }

    [Fact]
    public async Task The_eleventh_passkey_is_refused_before_the_ceremony()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);

        for (var index = 0; index < 10; index++)
        {
            using var authenticator = new VirtualAuthenticator();
            (await PasskeyCeremony.RegisterAsync(client, authenticator, $"key {index}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var eleventh = new VirtualAuthenticator();
        await (await PasskeyCeremony.RegisterAsync(client, eleventh, "one too many"))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.limit-reached");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_passkey_needs_a_name(string name)
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);

        await (await client.PostAsJsonAsync("/api/identity/me/passkeys/options", new { name }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.name-invalid");
    }

    [Fact]
    public async Task A_tampered_ceremony_stores_nothing()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);
        using var authenticator = new VirtualAuthenticator();
        var options = await client.PostAsJsonAsync("/api/identity/me/passkeys/options", new { name = "key" });
        var creation = (await options.Content.ReadFromJsonAsync<PasskeyCeremony.CreationOptionsBody>())!;

        var response = await client.PostAsJsonAsync("/api/identity/me/passkeys", new
        {
            credentialJson = authenticator.Register(creation.CreationOptions, "https://evil.example.com"),
            name = "key",
        });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.registration-failed");
        (await PasskeyCeremony.ListAsync(client)).ShouldBeEmpty();
    }
}
