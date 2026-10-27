using System.Net.Http.Headers;
using System.Net.Http.Json;
using AuthServer.Identity.Tests.Devices;
using AuthServer.Sync.Tests;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.IntegrationTests;

// The firmware's side of sync: a device token, the heads, a file up, a file down.
internal static class SyncApi
{
    public static async Task<(HttpClient Client, Guid DeviceId)> DeviceAsync(IdentityFixture fixture, TestAccount account) =>
        await SignInAsync(fixture, account.Device, account.Id);

    /// <summary>A second device of the same user: a window opened by the user, then a registration.</summary>
    public static async Task<(HttpClient Client, Guid DeviceId)> AnotherDeviceAsync(IdentityFixture fixture, TestAccount account)
    {
        var user = await AccessTokens.BearerAsync(fixture.Application, account);
        (await user.PutAsync("/api/identity/me/registration-window", null)).EnsureSuccessStatusCode();

        var device = new VirtualDevice();
        (await DeviceApi.RegisterAsync(fixture.Application.CreateClient(), device, account.Email)).EnsureSuccessStatusCode();

        return await SignInAsync(fixture, device, account.Id, newest: true);
    }

    public static Task<HttpResponseMessage> UploadAsync(HttpClient device, StoreFile file) => UploadAsync(device, file.Bytes());

    public static Task<HttpResponseMessage> UploadAsync(HttpClient device, byte[] bytes)
    {
        var body = new ByteArrayContent(bytes);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return device.PostAsync("/api/device/stores/files", body);
    }

    public static Task<HttpResponseMessage> HeadsAsync(HttpClient device, params StoreFile[] stores) =>
        device.PostAsJsonAsync("/api/device/stores/heads", new
        {
            stores = stores.Select(store => new
            {
                storeId = Convert.ToHexStringLower(store.StoreId),
                seedId = Convert.ToHexStringLower(store.SeedId),
                kind = (int)store.Kind,
            }),
        });

    public static Task<HttpResponseMessage> DownloadAsync(HttpClient device, StoreFile store, uint? above = null) =>
        device.GetAsync(
            $"/api/device/stores/files/{Convert.ToHexStringLower(store.StoreId)}/{Convert.ToHexStringLower(store.SeedId)}/{store.Kind}"
            + (above is { } held ? $"?above={held}" : string.Empty));

    private static async Task<(HttpClient Client, Guid DeviceId)> SignInAsync(
        IdentityFixture fixture, VirtualDevice device, Guid userId, bool newest = false)
    {
        var ids = await fixture.QueryAsync(context => context.Devices
            .Where(entity => entity.UserId == userId)
            .OrderBy(entity => entity.RegisteredAt)
            .Select(entity => entity.Id)
            .ToListAsync());
        var deviceId = newest ? ids[^1] : ids[0];

        var token = await AccessTokens.AccessTokenOf(
            await DeviceApi.TokenAsync(fixture.Application.CreateClient(), device, deviceId.ToString()));

        return (AccessTokens.Bearer(fixture.Application, token), deviceId);
    }
}
