using System.Net;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Tests.Devices;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.IntegrationTests;

// UC-1, UC-4, UC-5, UC-7.
public sealed class DeviceRegistrationTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task The_first_device_on_an_empty_server_makes_an_admin_without_a_window()
    {
        await fixture.ResetAsync();
        using var device = new VirtualDevice();

        var registered = await DeviceApi.RegisterAsync(fixture.Application.CreateClient(), device, "Founder@Example.com");

        var body = await registered.JsonAsync();
        body["clientId"]!.GetValue<string>().ShouldBe(body["deviceId"]!.GetValue<string>());

        var user = await fixture.QueryAsync(context => context.Users.AsNoTracking().SingleAsync());
        user.Email.ShouldBe("Founder@Example.com");
        user.UserName.ShouldBe("Founder");
        user.Role.ShouldBe(UserRoles.Admin);
        user.RegistrationOpenUntil.ShouldBeNull();

        var account = new TestAccount(user.Id, user.Email!, device);
        var me = await (await AccessTokens.BearerAsync(fixture.Application, account)).GetAsync("/api/identity/me");
        (await me.JsonAsync())["role"]!.GetValue<string>().ShouldBe(UserRoles.Admin);
    }

    [Fact]
    public async Task Racing_first_registrations_leave_exactly_one_admin()
    {
        await fixture.ResetAsync();
        var devices = Enumerable.Range(0, 5).Select(_ => new VirtualDevice()).ToArray();

        var responses = await Task.WhenAll(devices.Select((device, index) =>
            DeviceApi.RegisterAsync(fixture.Application.CreateClient(), device, $"first{index}@example.com")));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Forbidden).ShouldBe(4);
        (await fixture.QueryAsync(context => context.Users.CountAsync())).ShouldBe(1);

        foreach (var device in devices)
        {
            device.Dispose();
        }
    }

    [Fact]
    public async Task With_users_present_a_registration_needs_an_open_window_and_closes_it()
    {
        await fixture.ResetAsync();
        await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin);
        var id = await fixture.CreateAccountAsync("new@example.com", window: TimeSpan.FromHours(1));
        using var device = new VirtualDevice();

        var registered = await DeviceApi.RegisterAsync(fixture.Application.CreateClient(), device, "new@example.com");

        registered.StatusCode.ShouldBe(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());
        (await fixture.FindUserAsync(id))!.RegistrationOpenUntil.ShouldBeNull();

        using var second = new VirtualDevice();
        var again = await DeviceApi.RegisterAsync(fixture.Application.CreateClient(), second, "new@example.com");
        await again.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "device-registration.refused");
    }

    [Fact]
    public async Task Every_cause_of_refusal_looks_the_same()
    {
        await fixture.ResetAsync();
        await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin);
        await fixture.CreateAccountAsync("closed@example.com");
        await fixture.CreateAccountAsync("expired@example.com", window: TimeSpan.FromHours(-1));
        var blocked = await fixture.CreateAccountAsync("blocked@example.com", window: TimeSpan.FromHours(1));
        await fixture.QueryAsync(context => context.Users.Where(user => user.Id == blocked)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.BlockedAt, DateTimeOffset.UtcNow)));
        await fixture.CreateAccountAsync("open@example.com", window: TimeSpan.FromHours(1));
        using var device = new VirtualDevice();
        using var other = new VirtualDevice();
        var client = fixture.Application.CreateClient();

        var badSignature = device.Registration("open@example.com") with
        {
            Signature = other.Sign(VirtualDevice.Message("open@example.com", device.HardwareId, device.Mac, device.TotpSecretBase32, device.PublicKey)),
        };

        var refusals = new[]
        {
            await DeviceApi.RegisterAsync(client, device, "nobody@example.com"),
            await DeviceApi.RegisterAsync(client, device, "closed@example.com"),
            await DeviceApi.RegisterAsync(client, device, "expired@example.com"),
            await DeviceApi.RegisterAsync(client, device, "blocked@example.com"),
            await DeviceApi.RegisterAsync(client, badSignature),
        };

        var bodies = new List<string>();

        foreach (var refusal in refusals)
        {
            await refusal.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "device-registration.refused");
            bodies.Add((await refusal.Content.ReadAsStringAsync()).Replace(refusal.RequestMessage!.RequestUri!.AbsolutePath, ""));
        }

        bodies.Distinct().Count().ShouldBe(1);
        (await fixture.QueryAsync(context => context.Devices.CountAsync())).ShouldBe(1, "only the admin's device");
    }

    [Fact]
    public async Task A_malformed_registration_is_a_bad_request()
    {
        await fixture.ResetAsync();
        using var device = new VirtualDevice();

        var response = await DeviceApi.RegisterAsync(
            fixture.Application.CreateClient(), device.Registration("a@example.com") with { PublicKey = "AAAA" });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "device-registration.invalid");
    }

    [Fact]
    public async Task Re_registering_the_same_hardware_replaces_the_key_and_the_secret_and_keeps_the_id()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("owner@example.com");
        var before = await fixture.QueryAsync(context => context.Devices.AsNoTracking().SingleAsync());
        var oldCode = account.Device.Code(DateTimeOffset.UtcNow);

        await OpenOwnWindowAsync(account);
        account.Device.Reset();
        var again = await DeviceApi.RegisterAsync(fixture.Application.CreateClient(), account.Device, account.Email);

        (await again.JsonAsync())["deviceId"]!.GetValue<string>().ShouldBe(before.Id.ToString());

        var after = await fixture.QueryAsync(context => context.Devices.AsNoTracking().SingleAsync());
        after.Id.ShouldBe(before.Id);
        after.PublicKey.ShouldNotBe(before.PublicKey);
        after.TotpSecret.ShouldNotBe(before.TotpSecret);

        var refused = await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), account.Email, oldCode);
        if (oldCode != account.Device.Code(DateTimeOffset.UtcNow))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        await AccessTokens.SignInAsync(fixture.CreateBrowser(), account.Email, account.Device);
    }

    [Fact]
    public async Task Hardware_registered_to_another_user_is_refused()
    {
        await fixture.ResetAsync();
        var owner = await fixture.CreateUserAsync("owner@example.com");
        await fixture.CreateAccountAsync("thief@example.com", window: TimeSpan.FromHours(1));
        using var clone = new VirtualDevice(owner.Device.HardwareId);

        var response = await DeviceApi.RegisterAsync(fixture.Application.CreateClient(), clone, "thief@example.com");

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "device.taken");
        (await fixture.QueryAsync(context => context.Devices.SingleAsync(device => device.HardwareId == owner.Device.HardwareId)))
            .UserId.ShouldBe(owner.Id);
    }

    [Fact]
    public async Task The_TOTP_secret_is_not_stored_in_the_clear()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("owner@example.com");

        var stored = await fixture.QueryAsync(context => context.Devices.AsNoTracking().SingleAsync());

        stored.TotpSecret.AsSpan().IndexOf(account.Device.TotpSecret).ShouldBe(-1);
    }

    [Fact]
    public async Task My_devices_list_shows_the_device_and_never_its_secrets()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("owner@example.com");
        var client = await AccessTokens.BearerAsync(fixture.Application, account);

        var response = await client.GetAsync("/api/identity/me/devices");
        var text = await response.Content.ReadAsStringAsync();
        var devices = (await response.JsonAsync())["devices"]!.AsArray();

        devices.Count.ShouldBe(1);
        devices[0]!["hardwareId"]!.GetValue<string>().ShouldBe(account.Device.HardwareId);
        devices[0]!["mac"]!.GetValue<string>().ShouldBe(account.Device.Mac);
        devices[0]!["registeredAt"].ShouldNotBeNull();
        text.ShouldNotContain(account.Device.TotpSecretBase32);
        text.ShouldNotContain(account.Device.PublicKey);
        text.ShouldNotContain("totp", Case.Insensitive);
    }

    private async Task OpenOwnWindowAsync(TestAccount account)
    {
        var client = await AccessTokens.BearerAsync(fixture.Application, account);
        (await client.PutAsync("/api/identity/me/registration-window", null)).EnsureSuccessStatusCode();
    }
}
