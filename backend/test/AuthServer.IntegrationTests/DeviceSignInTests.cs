using System.Net;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Tests.Devices;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.IntegrationTests;

// UC-6: a device signs in with its private key (private_key_jwt, client_credentials).
public sealed class DeviceSignInTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task A_signed_assertion_gets_an_access_token_that_names_the_device_and_its_owner()
    {
        await fixture.ResetAsync();
        var (account, clientId) = await RegisteredAsync("owner@example.com");

        var response = await DeviceApi.TokenAsync(fixture.Application.CreateClient(), account.Device, clientId);

        var body = await response.JsonAsync();

        body["refresh_token"].ShouldBeNull("a device signs in again rather than renewing");

        var accessToken = body["access_token"]!.GetValue<string>();
        var token = AccessTokens.Read(accessToken);
        token.Subject.ShouldBe(account.Id.ToString());
        token.GetClaim("device_id").Value.ShouldBe(clientId);

        var device = await (await AccessTokens.Bearer(fixture.Application, accessToken).GetAsync("/api/identity/device")).JsonAsync();
        device["deviceId"]!.GetValue<string>().ShouldBe(clientId);
        device["userId"]!.GetValue<string>().ShouldBe(account.Id.ToString());
        device["hardwareId"]!.GetValue<string>().ShouldBe(account.Device.HardwareId);
    }

    [Fact]
    public async Task An_assertion_for_another_audience_is_refused()
    {
        await fixture.ResetAsync();
        var (account, clientId) = await RegisteredAsync("owner@example.com");

        var response = await DeviceApi.TokenAsync(
            fixture.Application.CreateClient(), account.Device, clientId, TestHost.PublicOrigin + "/connect/token");

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_assertion_signed_by_another_key_is_refused()
    {
        await fixture.ResetAsync();
        var (_, clientId) = await RegisteredAsync("owner@example.com");
        using var impostor = new VirtualDevice();

        var response = await DeviceApi.TokenAsync(fixture.Application.CreateClient(), impostor, clientId);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task After_re_registration_the_old_key_is_refused_and_the_new_one_works()
    {
        await fixture.ResetAsync();
        var (account, clientId) = await RegisteredAsync("owner@example.com");
        using var oldKey = ECDsaCopy(account.Device);

        await fixture.QueryAsync(context => context.Users.Where(user => user.Id == account.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.RegistrationOpenUntil, DateTimeOffset.UtcNow.AddHours(1))));
        account.Device.Reset();
        (await DeviceApi.RegisterAsync(fixture.Application.CreateClient(), account.Device, account.Email)).EnsureSuccessStatusCode();

        (await DeviceApi.TokenAsync(fixture.Application.CreateClient(), account.Device, clientId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DeviceApi.TokenAsync(fixture.Application.CreateClient(), oldKey, clientId)).StatusCode
            .ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_blocked_owner_s_device_is_refused()
    {
        await fixture.ResetAsync();
        var (account, clientId) = await RegisteredAsync("owner@example.com");
        await fixture.QueryAsync(context => context.Users.Where(user => user.Id == account.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.BlockedAt, DateTimeOffset.UtcNow)));

        var response = await DeviceApi.TokenAsync(fixture.Application.CreateClient(), account.Device, clientId);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_device_token_is_not_a_web_session_and_a_web_token_is_not_a_device()
    {
        await fixture.ResetAsync();
        var (account, clientId) = await RegisteredAsync("owner@example.com");
        var deviceToken = await AccessTokens.AccessTokenOf(await DeviceApi.TokenAsync(fixture.Application.CreateClient(), account.Device, clientId));
        var web = await AccessTokens.BearerAsync(fixture.Application, account);

        (await AccessTokens.Bearer(fixture.Application, deviceToken).GetAsync("/api/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await web.GetAsync("/api/identity/device")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_device_endpoint_needs_a_token()
    {
        (await fixture.Application.CreateClient().GetAsync("/api/identity/device")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<(TestAccount Account, string ClientId)> RegisteredAsync(string email)
    {
        var account = await fixture.CreateUserAsync(email, UserRoles.User);
        var clientId = await fixture.QueryAsync(context =>
            context.Devices.Where(device => device.UserId == account.Id).Select(device => device.Id.ToString()).SingleAsync());

        return (account, clientId);
    }

    // The same hardware with the key it had before a reset.
    private static VirtualDevice ECDsaCopy(VirtualDevice device)
    {
        var copy = new VirtualDevice(device.HardwareId, device.Mac);
        copy.Key.ImportParameters(device.Key.ExportParameters(includePrivateParameters: true));
        return copy;
    }
}
