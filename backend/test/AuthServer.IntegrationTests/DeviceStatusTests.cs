using System.Net;
using System.Net.Http.Json;

namespace AuthServer.IntegrationTests;

// UC-38: a device reports its state; the answer names the board's active firmware release (FirmwareTests).
public sealed class DeviceStatusTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static object Status(string firmware) => new
    {
        firmware,
        board = "x4pro",
        mac = "aa:bb:cc:dd:ee:ff",
        wifi = true,
        ble = false,
        battery = 87,
        uptime = 3600,
        time = 1_790_000_000,
        freeHeap = 123_456,
    };

    [Fact]
    public async Task Without_an_active_release_nothing_is_offered()
    {
        await fixture.ResetAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));

        var answer = await (await device.PostAsJsonAsync("/api/device/status", Status("0.5.0-174-g2f84ffa"))).JsonAsync();

        answer["update"].ShouldBeNull();
        answer["time"]!.GetValue<long>().ShouldBeGreaterThan(1_790_000_000);
    }

    [Fact]
    public async Task The_report_is_kept_and_the_device_seen()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var (device, deviceId) = await SyncApi.DeviceAsync(fixture, account);

        (await device.PostAsJsonAsync("/api/device/status", Status("0.5.0-174-g2f84ffa"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var user = await AccessTokens.BearerAsync(fixture.Application, account);
        var mine = (await (await user.GetAsync("/api/identity/me/devices")).JsonAsync())["devices"]!.AsArray().Single()!;
        mine["firmware"]!.GetValue<string>().ShouldBe("0.5.0-174-g2f84ffa");
        mine["lastSeenAt"].ShouldNotBeNull();
        mine["status"]!["wifi"]!.GetValue<bool>().ShouldBeTrue();
        mine["status"]!["ble"]!.GetValue<bool>().ShouldBeFalse();
        mine["status"]!["battery"]!.GetValue<int>().ShouldBe(87);
        mine["status"]!["freeHeap"]!.GetValue<int>().ShouldBe(123_456);

        var admin = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("admin@example.com", "admin"));
        var theirs = (await (await admin.GetAsync($"/api/identity/users/{account.Id}/devices")).JsonAsync())["devices"]!.AsArray().Single()!;
        theirs["id"]!.GetValue<string>().ShouldBe(deviceId.ToString());
        theirs["firmware"]!.GetValue<string>().ShouldBe("0.5.0-174-g2f84ffa");
    }

    [Fact]
    public async Task A_report_needs_a_firmware_version_and_stays_small()
    {
        await fixture.ResetAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));

        await (await device.PostAsJsonAsync("/api/device/status", new { wifi = true }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "device-status.invalid");
        await (await device.PostAsJsonAsync("/api/device/status", new { firmware = "0.5.0", note = new string('x', 5000) }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "device-status.invalid");
    }

    [Fact]
    public async Task Only_a_device_reports()
    {
        await fixture.ResetAsync();
        var user = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("ada@example.com"));

        (await user.PostAsJsonAsync("/api/device/status", Status("0.5.0"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.Application.CreateClient().PostAsJsonAsync("/api/device/status", Status("0.5.0")))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
