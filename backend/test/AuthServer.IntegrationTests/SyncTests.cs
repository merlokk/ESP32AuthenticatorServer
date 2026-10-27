using System.Net;
using System.Net.Http.Json;
using AuthServer.Sync.Tests;

namespace AuthServer.IntegrationTests;

// UC-30 to UC-35: devices sync their stores through the server; the counter orders the copies.
public sealed class SyncTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static readonly StoreFile Vault = new() { Kind = 2 };

    [Fact]
    public async Task Heads_name_what_the_server_holds_and_null_for_the_rest()
    {
        await fixture.ResetAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));
        var fido = new StoreFile { Kind = 1, StoreId = Convert.FromHexString("b1b2b3b4b5b6b7b8") };
        (await SyncApi.UploadAsync(device, Vault with { Version = 7, Modified = 1_790_000_123 })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var heads = (await (await SyncApi.HeadsAsync(device, Vault, fido)).JsonAsync())["stores"]!.AsArray();

        heads.Count.ShouldBe(2);
        heads[0]!["storeId"]!.GetValue<string>().ShouldBe("a1a2a3a4a5a6a7a8");
        heads[0]!["seedId"]!.GetValue<string>().ShouldBe("c1c2c3c4");
        heads[0]!["kind"]!.GetValue<int>().ShouldBe(2);
        heads[0]!["version"]!.GetValue<long>().ShouldBe(7);
        heads[0]!["modified"]!.GetValue<long>().ShouldBe(1_790_000_123);
        heads[1]!["version"].ShouldBeNull();
        heads[1]!["modified"].ShouldBeNull();
    }

    [Fact]
    public async Task Only_a_higher_counter_is_accepted_whatever_the_time()
    {
        await fixture.ResetAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));
        (await SyncApi.UploadAsync(device, Vault with { Version = 5, Modified = 1_790_000_000 })).StatusCode.ShouldBe(HttpStatusCode.OK);

        await (await SyncApi.UploadAsync(device, Vault with { Version = 5, Modified = 1_790_009_999 }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "sync.not-newer");
        await (await SyncApi.UploadAsync(device, Vault with { Version = 4, Modified = 1_799_000_000 }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "sync.not-newer");

        // A later counter wins even with an earlier (drifted) time.
        var accepted = await (await SyncApi.UploadAsync(device, Vault with { Version = 6, Modified = 1_780_000_000 })).JsonAsync();
        accepted["version"]!.GetValue<long>().ShouldBe(6);
    }

    [Fact]
    public async Task A_refusal_names_the_head_the_device_must_beat()
    {
        await fixture.ResetAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));
        await SyncApi.UploadAsync(device, Vault with { Version = 9, Modified = 1_790_000_900 });

        var refusal = await (await SyncApi.UploadAsync(device, Vault with { Version = 8 })).JsonAsync(HttpStatusCode.Conflict);

        refusal["version"]!.GetValue<long>().ShouldBe(9);
        refusal["modified"]!.GetValue<long>().ShouldBe(1_790_000_900);
    }

    [Fact]
    public async Task Another_device_of_the_owner_downloads_the_newest_copy_above_what_it_has()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var (first, _) = await SyncApi.DeviceAsync(fixture, account);
        var (second, _) = await SyncApi.AnotherDeviceAsync(fixture, account);
        var file = (Vault with { Version = 3 }).Bytes();
        (await SyncApi.UploadAsync(first, file)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var download = await SyncApi.DownloadAsync(second, Vault, above: 2);

        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(file);
        (await SyncApi.DownloadAsync(second, Vault)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Never at or below what the device holds (rollback).
        (await SyncApi.DownloadAsync(second, Vault, above: 3)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SyncApi.DownloadAsync(second, Vault, above: 4)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_unknown_store_is_not_found()
    {
        await fixture.ResetAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));

        await (await SyncApi.DownloadAsync(device, Vault)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "sync.store-not-found");
    }

    [Fact]
    public async Task Another_users_device_and_another_seed_see_nothing()
    {
        await fixture.ResetAsync();
        var (owner, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));
        var (stranger, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("eve@example.com"));
        await SyncApi.UploadAsync(owner, Vault with { Version = 3 });

        await (await SyncApi.DownloadAsync(stranger, Vault)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "sync.store-not-found");
        (await (await SyncApi.HeadsAsync(stranger, Vault)).JsonAsync())["stores"]![0]!["version"].ShouldBeNull();

        // The same store id under another seed phrase is another store: its devices cannot open this one.
        var otherSeed = Vault with { SeedId = Convert.FromHexString("0f0f0f0f") };
        (await (await SyncApi.HeadsAsync(owner, otherSeed)).JsonAsync())["stores"]![0]!["version"].ShouldBeNull();
        (await SyncApi.UploadAsync(owner, otherSeed with { Version = 1 })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_server_keeps_the_last_versions_only()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var (device, deviceId) = await SyncApi.DeviceAsync(fixture, account);

        for (uint version = 1; version <= TestHost.SyncVersionsToKeep + 2; version++)
        {
            (await SyncApi.UploadAsync(device, Vault with { Version = version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var user = await AccessTokens.BearerAsync(fixture.Application, account);
        var store = (await (await user.GetAsync("/api/sync/stores")).JsonAsync())["stores"]!.AsArray().Single()!;
        var versions = (await (await user.GetAsync($"/api/sync/stores/{store["id"]!.GetValue<string>()}/versions")).JsonAsync())["versions"]!.AsArray();

        versions.Select(version => version!["version"]!.GetValue<long>()).ShouldBe([5, 4, 3]);
        versions[0]!["uploadedByDeviceId"]!.GetValue<string>().ShouldBe(deviceId.ToString());
    }

    [Fact]
    public async Task The_owner_sees_each_store_its_head_and_who_wrote_it()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var (device, deviceId) = await SyncApi.DeviceAsync(fixture, account);
        await SyncApi.UploadAsync(device, Vault with { Version = 2, Modified = 1_790_000_200, KdfIterations = 300_000, PayloadLength = 100 });

        var user = await AccessTokens.BearerAsync(fixture.Application, account);
        var store = (await (await user.GetAsync("/api/sync/stores")).JsonAsync())["stores"]!.AsArray().Single()!;

        store["storeId"]!.GetValue<string>().ShouldBe("a1a2a3a4a5a6a7a8");
        store["seedId"]!.GetValue<string>().ShouldBe("c1c2c3c4");
        store["kind"]!.GetValue<int>().ShouldBe(2);
        store["version"]!.GetValue<long>().ShouldBe(2);
        store["modified"]!.GetValue<long>().ShouldBe(1_790_000_200);
        store["cipher"]!.GetValue<int>().ShouldBe(1);
        store["length"]!.GetValue<long>().ShouldBe(100);
        store["kdfIterations"]!.GetValue<long>().ShouldBe(300_000);
        store["keyId"]!.GetValue<string>().ShouldBe("0102030405060708");
        store["uploadedByDeviceId"]!.GetValue<string>().ShouldBe(deviceId.ToString());
        store["versions"]!.GetValue<int>().ShouldBe(1);
        store.AsObject().ContainsKey("content").ShouldBeFalse();
    }

    [Fact]
    public async Task Devices_and_people_each_use_their_own_endpoints()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var (device, _) = await SyncApi.DeviceAsync(fixture, account);
        var user = await AccessTokens.BearerAsync(fixture.Application, account);

        (await device.GetAsync("/api/sync/stores")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SyncApi.UploadAsync(user, Vault)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SyncApi.HeadsAsync(user, Vault)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SyncApi.UploadAsync(fixture.Application.CreateClient(), Vault)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_file_that_is_not_an_encrypted_store_is_refused()
    {
        await fixture.ResetAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));

        await (await SyncApi.UploadAsync(device, Vault with { Cipher = 0 })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "sync.file-plain");
        await (await SyncApi.UploadAsync(device, Vault with { Magic = "NOPE" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "sync.file-invalid");
        await (await SyncApi.UploadAsync(device, [1, 2, 3])).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "sync.file-invalid");
        await (await SyncApi.UploadAsync(device, Vault with { PayloadLength = TestHost.SyncMaxFileBytes }))
            .ShouldBeProblemAsync(HttpStatusCode.RequestEntityTooLarge, "sync.file-too-large");
    }

    [Fact]
    public async Task A_head_request_names_valid_stores_only()
    {
        await fixture.ResetAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));

        await (await device.PostAsJsonAsync("/api/sync/device/heads", new { stores = new[] { new { storeId = "xyz", seedId = "c1c2c3c4", kind = 2 } } }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "sync.request-invalid");
        await (await device.PostAsJsonAsync("/api/sync/device/heads", new { stores = new[] { new { storeId = "a1a2a3a4a5a6a7a8", seedId = "c1c2c3c4", kind = 9 } } }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "sync.request-invalid");
    }

    [Fact]
    public async Task Of_two_devices_racing_with_one_counter_exactly_one_wins()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var (first, _) = await SyncApi.DeviceAsync(fixture, account);
        var (second, _) = await SyncApi.AnotherDeviceAsync(fixture, account);
        await SyncApi.UploadAsync(first, Vault with { Version = 1 });

        for (var round = 0; round < 5; round++)
        {
            var version = (uint)(2 + round);
            var results = await Task.WhenAll(
                SyncApi.UploadAsync(first, Vault with { Version = version }),
                SyncApi.UploadAsync(second, Vault with { Version = version }));

            results.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            results.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        }
    }
}
