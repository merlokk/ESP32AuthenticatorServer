using System.Net;
using AuthServer.Sync.Tests;

namespace AuthServer.IntegrationTests;

// UC-46: the owner deletes a store with all its copies, or one copy; the head falls to the copy
// below, and a device that holds more uploads it again on its next sync.
public sealed class StoreDeletionTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static readonly StoreFile Vault = new() { Kind = 2 };

    private static async Task<string> StoreIdAsync(HttpClient user) =>
        (await (await user.GetAsync("/api/sync/stores")).JsonAsync())["stores"]!.AsArray().Single()!["id"]!.GetValue<string>();

    private static async Task<long[]> VersionsAsync(HttpClient user, string id) =>
        [.. (await (await user.GetAsync($"/api/sync/stores/{id}/versions")).JsonAsync())["versions"]!.AsArray()
            .Select(version => version!["version"]!.GetValue<long>())];

    [Fact]
    public async Task The_owner_deletes_a_store_and_devices_see_nothing()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var (device, _) = await SyncApi.DeviceAsync(fixture, account);
        await SyncApi.UploadAsync(device, Vault with { Version = 1 });
        await SyncApi.UploadAsync(device, Vault with { Version = 2 });
        var user = await AccessTokens.BearerAsync(fixture.Application, account);
        var id = await StoreIdAsync(user);

        (await user.DeleteAsync($"/api/sync/stores/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await (await user.GetAsync("/api/sync/stores")).JsonAsync())["stores"]!.AsArray().ShouldBeEmpty();
        (await (await SyncApi.HeadsAsync(device, Vault)).JsonAsync())["stores"]![0]!["version"].ShouldBeNull();
        await (await SyncApi.DownloadAsync(device, Vault)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "sync.store-not-found");
        await (await user.DeleteAsync($"/api/sync/stores/{id}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "sync.store-not-found");

        // A device that still holds it starts the store again.
        (await SyncApi.UploadAsync(device, Vault with { Version = 2 })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_the_newest_copy_makes_the_one_below_the_head()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var (device, _) = await SyncApi.DeviceAsync(fixture, account);
        await SyncApi.UploadAsync(device, Vault with { Version = 1 });
        await SyncApi.UploadAsync(device, Vault with { Version = 2, Modified = 1_790_000_200 });
        await SyncApi.UploadAsync(device, Vault with { Version = 3 });
        var user = await AccessTokens.BearerAsync(fixture.Application, account);
        var id = await StoreIdAsync(user);

        (await user.DeleteAsync($"/api/sync/stores/{id}/versions/1")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.DeleteAsync($"/api/sync/stores/{id}/versions/3")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await VersionsAsync(user, id)).ShouldBe([2]);
        var head = (await (await SyncApi.HeadsAsync(device, Vault)).JsonAsync())["stores"]![0]!;
        head["version"]!.GetValue<long>().ShouldBe(2);
        head["modified"]!.GetValue<long>().ShouldBe(1_790_000_200);
        (await (await user.GetAsync("/api/sync/stores")).JsonAsync())["stores"]![0]!["version"]!.GetValue<long>().ShouldBe(2);
        (await SyncApi.UploadAsync(device, Vault with { Version = 3 })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_the_last_copy_deletes_the_store()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var (device, _) = await SyncApi.DeviceAsync(fixture, account);
        await SyncApi.UploadAsync(device, Vault with { Version = 4 });
        var user = await AccessTokens.BearerAsync(fixture.Application, account);
        var id = await StoreIdAsync(user);

        await (await user.DeleteAsync($"/api/sync/stores/{id}/versions/3"))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "sync.version-not-found");
        (await user.DeleteAsync($"/api/sync/stores/{id}/versions/4")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await (await user.GetAsync("/api/sync/stores")).JsonAsync())["stores"]!.AsArray().ShouldBeEmpty();
        await (await user.GetAsync($"/api/sync/stores/{id}/versions")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "sync.store-not-found");
    }

    [Fact]
    public async Task Only_the_owner_deletes_and_only_with_a_person_s_token()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("ada@example.com");
        var (device, _) = await SyncApi.DeviceAsync(fixture, account);
        await SyncApi.UploadAsync(device, Vault with { Version = 1 });
        var id = await StoreIdAsync(await AccessTokens.BearerAsync(fixture.Application, account));
        var other = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("eve@example.com"));

        await (await other.DeleteAsync($"/api/sync/stores/{id}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "sync.store-not-found");
        await (await other.DeleteAsync($"/api/sync/stores/{id}/versions/1")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "sync.store-not-found");
        (await device.DeleteAsync($"/api/sync/stores/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.Application.CreateClient().DeleteAsync($"/api/sync/stores/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await (await SyncApi.HeadsAsync(device, Vault)).JsonAsync())["stores"]![0]!["version"]!.GetValue<long>().ShouldBe(1);
    }
}
