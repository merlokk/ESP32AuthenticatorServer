using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using AuthServer.Firmware.Tests;

namespace AuthServer.IntegrationTests;

// UC-41 to 44: firmware releases per board; the active one is offered in the status answer, and
// a device fetches any release in pieces (OTA).
public sealed class FirmwareTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private async Task<HttpClient> AdminAsync() =>
        await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("admin@example.com", "admin"));

    private static Task<HttpResponseMessage> UploadAsync(HttpClient admin, string board, byte[] image)
    {
        var body = new ByteArrayContent(image);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return admin.PostAsync($"/api/firmware/releases?board={board}", body);
    }

    private static async Task<string> UploadedAsync(HttpClient admin, string board, string version)
    {
        var uploaded = await UploadAsync(admin, board, new FirmwareImage { Version = version }.Bytes());
        uploaded.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await uploaded.JsonAsync())["id"]!.GetValue<string>();
    }

    private static async Task<JsonNode?> OfferAsync(HttpClient device, string firmware, string? board = "x4pro") =>
        (await (await device.PostAsJsonAsync("/api/device/status", new { firmware, board, wifi = true })).JsonAsync())["update"];

    [Fact]
    public async Task An_admin_uploads_a_release_named_by_its_image()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var image = new FirmwareImage { Version = "0.6.0-0-gabc1234" }.Bytes();

        var uploaded = await (await UploadAsync(admin, "x4pro", image)).JsonAsync();

        uploaded["version"]!.GetValue<string>().ShouldBe("0.6.0-0-gabc1234");
        uploaded["board"]!.GetValue<string>().ShouldBe("x4pro");
        uploaded["active"]!.GetValue<bool>().ShouldBeFalse();
        uploaded["size"]!.GetValue<long>().ShouldBe(image.Length);
        uploaded["sha256"]!.GetValue<string>().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(image)));

        var listed = await (await admin.GetAsync("/api/firmware/releases")).JsonAsync();
        listed["releases"]!.AsArray().Single()!["id"]!.GetValue<string>().ShouldBe(uploaded["id"]!.GetValue<string>());
        listed["boards"]!.AsArray().Select(board => board!["id"]!.GetValue<string>()).ShouldBe(["x4pro", "dev"]);
    }

    [Fact]
    public async Task A_version_is_on_a_board_once()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        await UploadedAsync(admin, "x4pro", "0.6.0-0-gabc1234");

        await (await UploadAsync(admin, "x4pro", new FirmwareImage { Version = "0.6.0-0-gabc1234" }.Bytes()))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "firmware.release-exists");
        (await UploadAsync(admin, "dev", new FirmwareImage { Version = "0.6.0-0-gabc1234" }.Bytes())).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_bad_image_an_unknown_board_or_a_large_file_is_refused()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();

        await (await UploadAsync(admin, "x4pro", new FirmwareImage { BreakChecksum = true }.Bytes()))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "firmware.image-invalid");
        await (await UploadAsync(admin, "x4pro", new FirmwareImage { Project = "another" }.Bytes()))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "firmware.image-invalid");
        await (await UploadAsync(admin, "toaster", new FirmwareImage().Bytes()))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "firmware.board-unknown");
        await (await UploadAsync(admin, "x4pro", new FirmwareImage { SegmentLengths = [TestHost.FirmwareMaxImageBytes] }.Bytes()))
            .ShouldBeProblemAsync(HttpStatusCode.RequestEntityTooLarge, "firmware.image-too-large");
    }

    [Fact]
    public async Task Only_an_admin_manages_releases()
    {
        await fixture.ResetAsync();
        var user = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("ada@example.com"));

        (await user.GetAsync("/api/firmware/releases")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await UploadAsync(user, "x4pro", new FirmwareImage().Bytes())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_active_release_of_the_board_is_offered_to_a_device_on_another_version()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));
        var id = await UploadedAsync(admin, "x4pro", "0.6.0-0-gabc1234");
        await UploadedAsync(admin, "dev", "0.7.0-0-gdef5678");

        (await OfferAsync(device, "0.5.0-174-g2f84ffa")).ShouldBeNull();

        (await admin.PutAsync($"/api/firmware/releases/{id}/active", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var offer = await OfferAsync(device, "0.5.0-174-g2f84ffa");
        offer!["id"]!.GetValue<string>().ShouldBe(id);
        offer["version"]!.GetValue<string>().ShouldBe("0.6.0-0-gabc1234");
        offer["size"]!.GetValue<long>().ShouldBeGreaterThan(0);
        offer["sha256"]!.GetValue<string>().Length.ShouldBe(64);

        (await OfferAsync(device, "0.6.0-0-gabc1234")).ShouldBeNull();
        (await OfferAsync(device, "0.5.0-174-g2f84ffa", board: "dev")).ShouldBeNull();
        (await OfferAsync(device, "0.5.0-174-g2f84ffa", board: null)).ShouldBeNull();
    }

    [Fact]
    public async Task Activating_another_release_rolls_the_board_to_it_and_deactivating_offers_none()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));
        var older = await UploadedAsync(admin, "x4pro", "0.6.0-0-gabc1234");
        var newer = await UploadedAsync(admin, "x4pro", "0.7.0-0-gdef5678");

        await admin.PutAsync($"/api/firmware/releases/{newer}/active", null);
        (await admin.PutAsync($"/api/firmware/releases/{older}/active", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await OfferAsync(device, "0.7.0-0-gdef5678"))!["id"]!.GetValue<string>().ShouldBe(older);
        var releases = (await (await admin.GetAsync("/api/firmware/releases")).JsonAsync())["releases"]!.AsArray();
        releases.Single(release => release!["active"]!.GetValue<bool>())!["id"]!.GetValue<string>().ShouldBe(older);

        (await admin.DeleteAsync($"/api/firmware/releases/{older}/active")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await OfferAsync(device, "0.7.0-0-gdef5678")).ShouldBeNull();
    }

    [Fact]
    public async Task A_device_lists_the_releases_of_its_board()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));
        var active = await UploadedAsync(admin, "x4pro", "0.6.0-0-gabc1234");
        await UploadedAsync(admin, "x4pro", "0.7.0-0-gdef5678");
        await UploadedAsync(admin, "dev", "0.7.0-0-gdef5678");
        await admin.PutAsync($"/api/firmware/releases/{active}/active", null);

        var listed = (await (await device.GetAsync("/api/device/firmware?board=x4pro")).JsonAsync())["releases"]!.AsArray();

        listed.Select(release => release!["version"]!.GetValue<string>()).ShouldBe(["0.7.0-0-gdef5678", "0.6.0-0-gabc1234"]);
        listed.Single(release => release!["active"]!.GetValue<bool>())!["id"]!.GetValue<string>().ShouldBe(active);
        (await (await device.GetAsync("/api/device/firmware")).JsonAsync())["releases"]!.AsArray().Count.ShouldBe(3);
        await (await device.GetAsync("/api/device/firmware?board=toaster"))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "firmware.board-unknown");
    }

    [Fact]
    public async Task A_device_downloads_a_release_in_pieces()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));
        var image = new FirmwareImage { SegmentLengths = [1500, 700] }.Bytes();
        var uploaded = await UploadAsync(admin, "x4pro", image);
        var id = (await uploaded.JsonAsync())["id"]!.GetValue<string>();

        var whole = await device.GetAsync($"/api/device/firmware/{id}/image");
        whole.StatusCode.ShouldBe(HttpStatusCode.OK);
        whole.Headers.AcceptRanges.ShouldContain("bytes");
        (await whole.Content.ReadAsByteArrayAsync()).ShouldBe(image);

        using var pieces = new MemoryStream();

        for (var offset = 0; offset < image.Length; offset += 1024)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/device/firmware/{id}/image");
            request.Headers.Range = new RangeHeaderValue(offset, offset + 1023);
            var piece = await device.SendAsync(request);

            piece.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
            piece.Content.Headers.ContentRange!.Length.ShouldBe(image.Length);
            piece.Content.Headers.ContentRange.From.ShouldBe(offset);
            pieces.Write(await piece.Content.ReadAsByteArrayAsync());
        }

        pieces.ToArray().ShouldBe(image);

        using var past = new HttpRequestMessage(HttpMethod.Get, $"/api/device/firmware/{id}/image");
        past.Headers.Range = new RangeHeaderValue(image.Length, null);
        var refused = await device.SendAsync(past);
        refused.StatusCode.ShouldBe(HttpStatusCode.RequestedRangeNotSatisfiable);
        refused.Content.Headers.ContentRange!.Length.ShouldBe(image.Length);

        await (await device.GetAsync($"/api/device/firmware/{Guid.NewGuid()}/image"))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "firmware.release-not-found");
    }

    [Fact]
    public async Task A_deleted_release_is_gone()
    {
        await fixture.ResetAsync();
        var admin = await AdminAsync();
        var (device, _) = await SyncApi.DeviceAsync(fixture, await fixture.CreateUserAsync("ada@example.com"));
        var id = await UploadedAsync(admin, "x4pro", "0.6.0-0-gabc1234");
        await admin.PutAsync($"/api/firmware/releases/{id}/active", null);

        (await admin.DeleteAsync($"/api/firmware/releases/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await OfferAsync(device, "0.5.0")).ShouldBeNull();
        (await device.GetAsync($"/api/device/firmware/{id}/image")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await (await admin.DeleteAsync($"/api/firmware/releases/{id}"))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "firmware.release-not-found");
    }

    [Fact]
    public async Task Only_a_device_downloads()
    {
        await fixture.ResetAsync();
        var user = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("ada@example.com"));

        (await user.GetAsync("/api/device/firmware")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.Application.CreateClient().GetAsync($"/api/device/firmware/{Guid.NewGuid()}/image"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
